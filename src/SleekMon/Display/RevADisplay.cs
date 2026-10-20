using System.IO.Ports;

namespace SleekMon.Display;

/// Turing Smart Screen 3.5" revision A driver (protocol: RESEARCH § 2). Synchronous, single owner, not thread-safe.
internal sealed class RevADisplay : IDisposable {
    public const int NativeWidth = 320;
    public const int NativeHeight = 480;

    private enum Command : byte {
        Reset = 101,
        Clear = 102,
        ScreenOff = 108,
        ScreenOn = 109,
        SetBrightness = 110,
        SetOrientation = 121,
        DisplayBitmap = 197
    }

    private readonly SerialPort _port;
    private readonly RevAOptions _options;
    private readonly byte[] _commandBuffer = new byte[16];

    private RevADisplay(SerialPort port, RevAOptions options) {
        _port = port;
        _options = options;
    }

    public string PortName => _port.PortName;

    public static RevADisplay Open(string portName, RevAOptions options) {
        var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One) {
            Handshake = options.RtsCts ? Handshake.RequestToSend : Handshake.None,
            DtrEnable = options.DtrRts,
            WriteTimeout = options.WriteTimeoutMs
        };
        // RtsEnable throws while the handshake drives RTS
        if (!options.RtsCts) port.RtsEnable = options.DtrRts;

        try {
            port.Open();
        } catch {
            port.Dispose();
            throw;
        }
        return new RevADisplay(port, options);
    }

    /// The screen restarts and its COM port may change: the connection is unusable afterwards.
    public void Reset() => SendCommand(Command.Reset);

    /// Fills the screen with white.
    public void Clear() => SendCommand(Command.Clear);

    public void ScreenOff() => SendCommand(Command.ScreenOff);

    public void ScreenOn() => SendCommand(Command.ScreenOn);

    public void SetBrightness(int percent) {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);

        // The screen takes 0 as the brightest and 255 as the darkest
        SendCommand(Command.SetBrightness, x: (int)(255 - percent * 2.55));
    }

    /// Bitmap coordinates are then expressed in this orientation.
    public void SetOrientation(Orientation orientation) {
        var portrait = orientation is Orientation.Portrait or Orientation.ReversePortrait;
        var width = portrait ? NativeWidth : NativeHeight;
        var height = portrait ? NativeHeight : NativeWidth;

        var frame = _commandBuffer.AsSpan(0, 16);
        frame.Clear();
        frame[5] = (byte)Command.SetOrientation;
        frame[6] = (byte)(100 + orientation);
        frame[7] = (byte)(width >> 8);
        frame[8] = (byte)(width & 255);
        frame[9] = (byte)(height >> 8);
        frame[10] = (byte)(height & 255);
        _port.Write(_commandBuffer, 0, frame.Length);
    }

    /// Draws RGB565 little-endian pixels in the given rectangle.
    public void DisplayBitmap(int x, int y, int width, int height, ReadOnlySpan<byte> rgb565) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgb565.Length != width * height * 2) {
            throw new ArgumentException($"Expected {width * height * 2} bytes, got {rgb565.Length}", nameof(rgb565));
        }

        SendCommand(Command.DisplayBitmap, x, y, x + width - 1, y + height - 1);

        var chunkSize = _options.ChunkSize > 0 ? _options.ChunkSize : rgb565.Length;
        for (var offset = 0; offset < rgb565.Length; offset += chunkSize) {
            _port.BaseStream.Write(rgb565.Slice(offset, Math.Min(chunkSize, rgb565.Length - offset)));
        }

        if (_options.FrameGapMs > 0) Thread.Sleep(_options.FrameGapMs);
    }

    private void SendCommand(Command command, int x = 0, int y = 0, int ex = 0, int ey = 0) {
        _commandBuffer[0] = (byte)(x >> 2);
        _commandBuffer[1] = (byte)(((x & 3) << 6) + (y >> 4));
        _commandBuffer[2] = (byte)(((y & 15) << 4) + (ex >> 6));
        _commandBuffer[3] = (byte)(((ex & 63) << 2) + (ey >> 8));
        _commandBuffer[4] = (byte)(ey & 255);
        _commandBuffer[5] = (byte)command;
        _port.Write(_commandBuffer, 0, 6);
    }

    public void Dispose() {
        try {
            _port.Dispose();
        } catch (IOException e) {
            // The device may already be gone
            Log.Warn($"Closing {_port.PortName} failed: {e.Message}");
        }
    }
}
