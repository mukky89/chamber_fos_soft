namespace VotschVc3.Core.Protocol;

/// <summary>
/// Represents the 32 binary digital channels exchanged with a Vötsch / Weiss
/// climate chamber over the ASCII-2 protocol.
/// <para>
/// In the protocol the digital channels are transferred as a fixed block of
/// 32 ASCII characters, each being either <c>'0'</c> or <c>'1'</c>. The exact
/// meaning of every channel depends on the configuration of the individual
/// chamber, therefore this class keeps them as a generic, individually
/// addressable bit field. The most commonly used channel is the
/// "Start / system on" channel (see <see cref="StartChannelIndex"/>) which has
/// to be set to <c>true</c> for the chamber to actually run a set point.
/// </para>
/// </summary>
public sealed class DigitalChannels
{
    /// <summary>Number of digital channels defined by the ASCII-2 protocol.</summary>
    public const int Count = 32;

    private readonly bool[] _channels = new bool[Count];

    /// <summary>
    /// Default index (0-based) of the digital channel that switches the chamber
    /// on. This is configurable because the mapping differs from chamber to
    /// chamber; verify it against your unit using the raw terminal.
    /// </summary>
    public int StartChannelIndex { get; set; }

    /// <summary>Gets or sets the state of a single channel (0..31).</summary>
    public bool this[int index]
    {
        get
        {
            ValidateIndex(index);
            return _channels[index];
        }
        set
        {
            ValidateIndex(index);
            _channels[index] = value;
        }
    }

    /// <summary>Gets or sets the configured start / "system on" channel.</summary>
    public bool Start
    {
        get => this[StartChannelIndex];
        set => this[StartChannelIndex] = value;
    }

    /// <summary>Returns a defensive copy of all channel states.</summary>
    public bool[] ToArray() => (bool[])_channels.Clone();

    /// <summary>Sets every channel to <c>false</c>.</summary>
    public void Clear() => Array.Clear(_channels, 0, _channels.Length);

    /// <summary>
    /// Serialises the channels into the 32 character protocol representation
    /// (e.g. <c>"01000000000000000000000000000000"</c>).
    /// </summary>
    public string ToProtocolString()
    {
        Span<char> buffer = stackalloc char[Count];
        for (int i = 0; i < Count; i++)
        {
            buffer[i] = _channels[i] ? '1' : '0';
        }

        return new string(buffer);
    }

    /// <summary>
    /// Parses a 32 character digital block coming from the chamber. Shorter or
    /// longer strings are tolerated: missing channels default to <c>false</c>
    /// and surplus characters are ignored, which keeps the parser robust
    /// against firmware variations.
    /// </summary>
    public static DigitalChannels Parse(string? text, int startChannelIndex = 0)
    {
        var result = new DigitalChannels { StartChannelIndex = startChannelIndex };
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        int n = Math.Min(Count, text.Length);
        for (int i = 0; i < n; i++)
        {
            result._channels[i] = text[i] == '1';
        }

        return result;
    }

    private static void ValidateIndex(int index)
    {
        if ((uint)index >= Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"Digital channel index must be in the range 0..{Count - 1}.");
        }
    }

    /// <inheritdoc />
    public override string ToString() => ToProtocolString();
}
