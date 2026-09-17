namespace Pump.Telemetry.Monitor.Models;

/// <summary>
/// Represents a single telemetry reading from an industrial device.
/// </summary>
public class TelemetryReading
{
    /// <summary>
    /// Unique identifier for the device that produced this reading.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// The time at which this reading was recorded.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Pressure reading in PSI (or other unit).
    /// </summary>
    public double Pressure { get; set; }

    /// <summary>
    /// Temperature reading in Celsius (or other unit).
    /// </summary>
    public double Temperature { get; set; }

    /// <summary>
    /// Sequence number to detect out-of-order or missing readings.
    /// </summary>
    public long SequenceNumber { get; set; }

    /// <summary>
    /// Override ToString for readable console output.
    /// </summary>
    public override string ToString() =>
        $"[{DeviceId}] Seq:{SequenceNumber} | Pressure: {Pressure:F2} PSI | Temp: {Temperature:F2}°C | {Timestamp:yyyy-MM-dd HH:mm:ss.fff}";
}
