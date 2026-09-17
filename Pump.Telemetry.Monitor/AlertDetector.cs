using Pump.Telemetry.Monitor.Models;

namespace Pump.Telemetry.Monitor;

/// <summary>
/// Detects abnormal conditions in telemetry readings.
/// Alerts are triggered for:
/// - Pressure above threshold
/// - Out-of-order sequence numbers
/// - Device silence for more than 5 seconds
/// </summary>
public class AlertDetector
{
    private const double PressureThreshold = 65.0; // PSI
    private const double SilenceThresholdSeconds = 5.0;

    // Track last sequence number per device to detect gaps
    private readonly Dictionary<string, long> _lastSequencePerDevice = new();

    // Track last seen timestamp per device to detect silence
    private readonly Dictionary<string, DateTime> _lastSeenPerDevice = new();

    private readonly object _lockObject = new();

    /// <summary>
    /// Analyzes a reading and generates alerts for any abnormal conditions.
    /// Returns a list of alert messages.
    /// </summary>
    public List<string> AnalyzeReading(TelemetryReading reading)
    {
        var alerts = new List<string>();

        lock (_lockObject)
        {
            // Check 1: Pressure threshold
            if (reading.Pressure > PressureThreshold)
            {
                alerts.Add($"🚨 HIGH PRESSURE ALERT [{reading.DeviceId}]: {reading.Pressure:F2} PSI (threshold: {PressureThreshold} PSI)");
            }

            // Check 2: Sequence number continuity
            if (_lastSequencePerDevice.TryGetValue(reading.DeviceId, out long lastSeq))
            {
                if (reading.SequenceNumber != lastSeq + 1)
                {
                    alerts.Add($"⚠️  OUT-OF-ORDER SEQUENCE [{reading.DeviceId}]: Expected {lastSeq + 1}, got {reading.SequenceNumber}");
                }
            }
            else
            {
                // First time seeing this device
                alerts.Add($"ℹ️  New device added: {reading.DeviceId}");
            }

            _lastSequencePerDevice[reading.DeviceId] = reading.SequenceNumber;
            _lastSeenPerDevice[reading.DeviceId] = reading.Timestamp;
        }

        return alerts;
    }

    /// <summary>
    /// Checks all tracked devices for silence and generates alerts for any silent devices.
    /// Should be called periodically (e.g., every second).
    /// </summary>
    public List<string> CheckForSilentDevices(DateTime currentTime)
    {
        var alerts = new List<string>();

        lock (_lockObject)
        {
            foreach (var (deviceId, lastSeen) in _lastSeenPerDevice.ToList())
            {
                var silenceDuration = (currentTime - lastSeen).TotalSeconds;
                if (silenceDuration > SilenceThresholdSeconds)
                {
                    alerts.Add($"🔇 DEVICE SILENT [{deviceId}]: No reading for {silenceDuration:F1} seconds");
                }
            }
        }

        return alerts;
    }
}
