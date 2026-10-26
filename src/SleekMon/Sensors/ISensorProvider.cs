namespace SleekMon.Sensors;

/// Reads the sensors on its own thread; Dispose stops it.
internal interface ISensorProvider : IDisposable {
    void Start();

    /// Latest reading, safe to read from any thread.
    SensorSnapshot Latest { get; }
}
