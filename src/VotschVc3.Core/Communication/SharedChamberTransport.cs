namespace VotschVc3.Core.Communication;

/// <summary>
/// Process-wide connection lease for controllers that answer only one TCP session.
/// Dashboard and calibration clients retain independent lifetimes but serialize all frames.
/// </summary>
public sealed class SharedChamberTransport : ITransport
{
    private static readonly SemaphoreSlim RegistryGate = new(1, 1);
    private static readonly Dictionary<(string Host, int Port), Entry> Entries = new();
    private readonly SemaphoreSlim _ownerGate = new(1, 1);
    private readonly ChamberConnectionSettings _settings;
    private readonly Func<ITransport> _factory;
    private readonly (string Host, int Port) _key;
    private Entry? _entry;

    public SharedChamberTransport(ChamberConnectionSettings settings, Func<ITransport>? factory = null)
    {
        _settings = settings.Clone();
        _key = (_settings.Host.Trim().ToUpperInvariant(), _settings.Port);
        _factory = factory ?? (() => new TcpTransport(_settings.Host, _settings.Port,
            _settings.ConnectTimeout, _settings.ReadTimeout,
            _settings.Terminator.Length > 0 ? _settings.Terminator[^1] : '\r'));
    }

    public bool IsConnected => _entry is { NeedsReconnect: false } entry && entry.Transport.IsConnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _ownerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_entry is null)
            {
                await RegistryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!Entries.TryGetValue(_key, out var entry))
                    {
                        entry = new Entry(_factory(), _settings);
                        Entries.Add(_key, entry);
                    }
                    else if (entry.Settings.Terminator != _settings.Terminator ||
                        entry.Settings.ConnectTimeout != _settings.ConnectTimeout || entry.Settings.ReadTimeout != _settings.ReadTimeout)
                        throw new InvalidOperationException("Tá istá komora už používa iné parametre komunikácie. Zjednoťte nastavenia pripojenia.");
                    entry.Owners++;
                    _entry = entry;
                }
                finally { RegistryGate.Release(); }
            }
            var current = _entry;
            try
            {
                await current.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try { await EnsureConnectedAsync(current, cancellationToken).ConfigureAwait(false); }
                finally { current.Gate.Release(); }
            }
            catch
            {
                await ReleaseAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { _ownerGate.Release(); }
    }

    public async Task<string> SendReceiveAsync(string command, CancellationToken cancellationToken = default)
    {
        await _ownerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = _entry ?? throw new InvalidOperationException("Not connected to a chamber.");
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureConnectedAsync(entry, cancellationToken).ConfigureAwait(false);
                return await entry.Transport.SendReceiveAsync(command, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // A timed-out/cancelled frame may still receive a late reply. Discard the
                // stream before the next exchange; never replay the failed command.
                entry.NeedsReconnect = true;
                throw;
            }
            finally { entry.Gate.Release(); }
        }
        finally { _ownerGate.Release(); }
    }

    private static async Task EnsureConnectedAsync(Entry entry, CancellationToken token)
    {
        if (!entry.NeedsReconnect && entry.Transport.IsConnected) return;
        entry.NeedsReconnect = true;
        await entry.Transport.DisconnectAsync().ConfigureAwait(false);
        await entry.Transport.ConnectAsync(token).ConfigureAwait(false);
        entry.NeedsReconnect = false;
    }

    public async Task DisconnectAsync()
    {
        await _ownerGate.WaitAsync().ConfigureAwait(false);
        try { await ReleaseAsync().ConfigureAwait(false); }
        finally { _ownerGate.Release(); }
    }

    private async Task ReleaseAsync()
    {
        var entry = _entry;
        if (entry is null) return;
        _entry = null;
        await RegistryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (--entry.Owners != 0) return;
            // Hold the registry until the old socket closes so a new owner cannot open
            // a competing session while the last exchange is still draining.
            await entry.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await entry.Transport.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Entries.Remove(_key);
                entry.Gate.Release();
            }
        }
        finally { RegistryGate.Release(); }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

    private sealed class Entry(ITransport transport, ChamberConnectionSettings settings)
    {
        public ITransport Transport { get; } = transport;
        public ChamberConnectionSettings Settings { get; } = settings;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Owners;
        public volatile bool NeedsReconnect = true;
    }
}
