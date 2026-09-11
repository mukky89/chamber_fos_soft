namespace VotschVc3.Core.Thermometers;

public sealed record Testo645Reading(double? HumidityPercent, double? TemperatureC, string Status)
{
    public bool IsValid => HumidityPercent.HasValue && TemperatureC.HasValue;
}

/// <summary>
/// Observed Testo645 positive-value frame layout, NOT a complete protocol specification.
/// No checksum or device error-code interpretation is known. Signed temperatures are unsupported.
/// Temperature acceptance is deliberately limited to 0..200 C (software validation envelope,
/// not a claim about the connected probe's rated range).
/// </summary>
public sealed class Testo645Parser
{
    public static ReadOnlySpan<byte> Request => [0x12, 0, 0, 0, 1, 1, 0x55, 0xD1, 0xB7, 0];
    private static ReadOnlySpan<byte> Header => [0x21, 0, 0, 0, 1];
    private readonly List<byte> _buffer = new(29);
    public int BufferedBytes => _buffer.Count;
    public void Reset() => _buffer.Clear();

    public IReadOnlyList<Testo645Reading> Feed(ReadOnlySpan<byte> bytes)
    {
        var result = new List<Testo645Reading>();
        foreach (byte value in bytes)
        {
            _buffer.Add(value);
            // Retain only a matching header prefix, including across calls. At most 29 bytes.
            while (_buffer.Count > 0 && !PrefixMatches()) _buffer.RemoveAt(0);
            if (_buffer.Count < 29) continue;
            int humidity = _buffer[14] * 256 + _buffer[15];
            int temperature = _buffer[19] * 256 + _buffer[20];
            result.Add(new Testo645Reading(
                humidity <= 1000 ? humidity / 10.0 : null,
                temperature <= 2000 ? temperature / 10.0 : null,
                humidity <= 1000 && temperature <= 2000 ? "OK_UNVERIFIED_PROTOCOL" :
                    $"UNSUPPORTED_VALUE RH_RAW={humidity} T_RAW={temperature}"));
            _buffer.Clear();
        }
        return result;
    }

    private bool PrefixMatches()
    {
        for (int i = 0; i < Math.Min(_buffer.Count, Header.Length); i++)
            if (_buffer[i] != Header[i]) return false;
        return true;
    }
}

/// <summary>Blocking bounded serial operations; Testo645Session moves them off the caller thread.</summary>
public interface ITesto645Transport : IDisposable
{
    void Open();
    void ClearInput();
    void Write(byte[] request);
    int Read(byte[] buffer);
}

/// <summary>Serializes open/query/close, including cancellation and partial initialization.</summary>
public sealed class Testo645Session(ITesto645Transport transport) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1);
    private bool _opened;
    private bool _disposed;

    public async Task<Testo645Reading> ReadAsync(TimeSpan timeout, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!_opened) { transport.Open(); _opened = true; }
                transport.ClearInput();
                transport.Write(Testo645Parser.Request.ToArray());
                var parser = new Testo645Parser();
                var buffer = new byte[256];
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.Elapsed < timeout)
                {
                    token.ThrowIfCancellationRequested();
                    int count;
                    try { count = transport.Read(buffer); }
                    catch (TimeoutException) { continue; }
                    if (count == 0) throw new IOException("Testo 645: spojenie bolo prerušené.");
                    var readings = parser.Feed(buffer.AsSpan(0, count));
                    token.ThrowIfCancellationRequested();
                    if (readings.Count > 0) return readings[^1];
                }
                throw new TimeoutException("Testo 645: neprišla úplná odpoveď v časovom limite.");
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await Task.Run(transport.Dispose).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
