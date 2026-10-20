namespace SleekMon.Display;

/// Serial link settings on which the reference implementations disagree (RESEARCH § 2).
/// Defaults are the theoretically fastest ones; the hardware tests decide.
internal sealed record RevAOptions {
    /// RTS/CTS flow control. USB CDC already has its own flow control.
    public bool RtsCts { get; init; }

    /// Assert DTR and RTS (RTS only without RTS/CTS flow control).
    public bool DtrRts { get; init; } = true;

    /// Pixel data write size in bytes; 0 writes the whole bitmap at once and lets the driver split it.
    public int ChunkSize { get; init; }

    /// Pause after the pixel data, before the next command.
    public int FrameGapMs { get; init; }

    /// Per write call: a full frame in a single write must fit in it.
    public int WriteTimeoutMs { get; init; } = 5000;
}
