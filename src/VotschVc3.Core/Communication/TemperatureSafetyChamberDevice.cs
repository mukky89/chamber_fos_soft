using VotschVc3.Core.Protocol;

namespace VotschVc3.Core.Communication;

public sealed class TemperatureSafetyPolicy
{
    private readonly object _sync = new();
    private double _minimumC;
    private double _maximumC;
    private bool _isTripped;

    public TemperatureSafetyPolicy(double minimumC, double maximumC) => Configure(minimumC, maximumC);

    public double MinimumC { get { lock (_sync) return _minimumC; } }
    public double MaximumC { get { lock (_sync) return _maximumC; } }
    public bool IsTripped { get { lock (_sync) return _isTripped; } }
    public event Action<double, double>? Configured;

    public void Configure(double minimumC, double maximumC)
    {
        if (!double.IsFinite(minimumC) || !double.IsFinite(maximumC) || minimumC >= maximumC)
            throw new ArgumentOutOfRangeException(nameof(minimumC), "Dolný limit poistky musí byť menší ako horný limit.");
        lock (_sync)
        {
            _minimumC = minimumC;
            _maximumC = maximumC;
            _isTripped = false;
        }
        Configured?.Invoke(minimumC, maximumC);
    }

    internal bool TryTrip(double actualC, out double minimumC, out double maximumC)
    {
        lock (_sync)
        {
            minimumC = _minimumC;
            maximumC = _maximumC;
            if (_isTripped || actualC >= _minimumC && actualC <= _maximumC) return false;
            _isTripped = true;
            return true;
        }
    }
}

public sealed class TemperatureSafetyTrippedEventArgs(
    double actualC, double minimumC, double maximumC, bool stopSucceeded, string? stopError) : EventArgs
{
    public double ActualC { get; } = actualC;
    public double MinimumC { get; } = minimumC;
    public double MaximumC { get; } = maximumC;
    public bool StopSucceeded { get; } = stopSucceeded;
    public string? StopError { get; } = stopError;
}

/// <summary>
/// Hard, protocol-independent temperature interlock. Every consumer (manual control,
/// profiles and FBG calibration) uses this decorator, so an actual temperature outside
/// the armed interval first switches chamber output off and then latches further writes.
/// </summary>
public sealed class TemperatureSafetyChamberDevice : IChamberDevice
{
    private readonly IChamberDevice _inner;
    private readonly TemperatureSafetyPolicy _policy;

    public TemperatureSafetyChamberDevice(IChamberDevice inner, TemperatureSafetyPolicy policy)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _inner.FrameExchanged += ForwardFrame;
    }

    public event EventHandler<FrameExchangedEventArgs>? FrameExchanged;
    public event EventHandler<TemperatureSafetyTrippedEventArgs>? SafetyTripped;
    public bool IsConnected => _inner.IsConnected;
    public ChamberConnectionSettings Settings => _inner.Settings;
    public Task ConnectAsync(ChamberConnectionSettings settings, CancellationToken cancellationToken = default) =>
        _inner.ConnectAsync(settings, cancellationToken);
    public Task DisconnectAsync() => _inner.DisconnectAsync();

    public async Task<ChamberReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        ChamberReading reading = await _inner.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (reading.Temperature is { } actual && _policy.TryTrip(actual, out double minimum, out double maximum))
        {
            bool stopped = false;
            string? error = null;
            for (int attempt = 1; attempt <= 3 && !stopped; attempt++)
            {
                try
                {
                    await _inner.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    stopped = true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    if (attempt < 3) await Task.Delay(250).ConfigureAwait(false);
                }
            }
            SafetyTripped?.Invoke(this, new(actual, minimum, maximum, stopped, error));
        }
        return reading;
    }

    public Task WriteSetpointsAsync(IReadOnlyList<double> setpoints, DigitalChannels digital, CancellationToken cancellationToken = default)
    {
        if (_policy.IsTripped)
            throw new InvalidOperationException("Teplotná poistka je aktivovaná. Skontrolujte komoru a znovu nastavte limity pred ďalším spustením.");
        if (setpoints.Count > 0 && (setpoints[0] < _policy.MinimumC || setpoints[0] > _policy.MaximumC))
            throw new InvalidOperationException($"Setpoint {setpoints[0]:0.###} °C je mimo teplotnej poistky [{_policy.MinimumC:0.###}; {_policy.MaximumC:0.###}] °C.");
        return _inner.WriteSetpointsAsync(setpoints, digital, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => _inner.StopAsync(cancellationToken);
    public Task<string> SendRawAsync(string frame, CancellationToken cancellationToken = default)
    {
        if (_policy.IsTripped)
            throw new InvalidOperationException("Teplotná poistka je aktivovaná; surové príkazy sú zablokované.");
        return _inner.SendRawAsync(frame, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _inner.FrameExchanged -= ForwardFrame;
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    private void ForwardFrame(object? sender, FrameExchangedEventArgs e) => FrameExchanged?.Invoke(this, e);
}
