using System.Collections.Concurrent;

namespace VotschVc3.App.Thermometers;

/// <summary>
/// Process-wide ownership gate for physical COM ports. A F100Client instance has
/// its own operation gate, while this lease prevents different clients and
/// diagnostics in the same process from opening/probing the same COM port at once.
/// </summary>
internal sealed class SerialPortLease : IDisposable
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _portName;
    private readonly SemaphoreSlim _gate;
    private int _released;

    private SerialPortLease(string portName, SemaphoreSlim gate)
    {
        _portName = portName;
        _gate = gate;
    }

    public static async Task<SerialPortLease> AcquireAsync(
        string portName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        SemaphoreSlim gate = Gates.GetOrAdd(portName, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SerialPortLease(portName, gate);
    }

    public static bool TryAcquire(string portName, out SerialPortLease? lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        SemaphoreSlim gate = Gates.GetOrAdd(portName, static _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0))
        {
            lease = null;
            return false;
        }

        lease = new SerialPortLease(portName, gate);
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0) return;
        _gate.Release();
    }

    public override string ToString() => _portName;
}

internal sealed class SerialPortBusyException : IOException
{
    public SerialPortBusyException(string portName, Exception innerException)
        : base($"Port {portName} je obsadený inou aplikáciou, inštanciou alebo diagnostikou.", innerException)
    {
        PortName = portName;
    }

    public string PortName { get; }
}
