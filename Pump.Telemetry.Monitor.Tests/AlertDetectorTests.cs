using Pump.Telemetry.Monitor;
using Pump.Telemetry.Monitor.Models;

namespace Pump.Telemetry.Monitor.Tests;

/// <summary>
/// Tests for AlertDetector alert detection logic.
/// Covers pressure threshold, sequence number continuity, and device silence detection.
/// </summary>
public class AlertDetectorTests
{
    [Fact]
    public void AnalyzeReading_PressureBelowThreshold_NoAlert()
    {
        // Arrange
        var detector = new AlertDetector();
        var reading = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = DateTime.UtcNow,
            Pressure = 60.0, // Below 65 PSI threshold
            Temperature = 25.0,
            SequenceNumber = 1
        };

        // Act
        var alerts = detector.AnalyzeReading(reading);

        // Assert
        // Should only have one alert for new device
        Assert.Single(alerts);
        Assert.Contains("New device added", alerts[0]);
    }

    [Fact]
    public void AnalyzeReading_PressureAboveThreshold_HighPressureAlert()
    {
        // Arrange
        var detector = new AlertDetector();
        var reading = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = DateTime.UtcNow,
            Pressure = 70.0, // Above 65 PSI threshold
            Temperature = 25.0,
            SequenceNumber = 1
        };

        // Act
        var alerts = detector.AnalyzeReading(reading);

        // Assert
        // Should have new device alert and high pressure alert
        Assert.Contains(2, new[] { alerts.Count });
        Assert.True(alerts.Any(a => a.Contains("HIGH PRESSURE ALERT")), "Should contain high pressure alert");
        Assert.True(alerts.Any(a => a.Contains("New device added")), "Should contain new device alert");
    }

    [Fact]
    public void AnalyzeReading_SequenceNumberContinuity_NoAlert()
    {
        // Arrange
        var detector = new AlertDetector();
        var reading1 = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = DateTime.UtcNow,
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 1
        };
        var reading2 = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = DateTime.UtcNow.AddSeconds(1),
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 2
        };

        // Act
        detector.AnalyzeReading(reading1);
        var alerts = detector.AnalyzeReading(reading2);

        // Assert
        // Should only have "New device" from first reading; second reading should have no sequence issues
        Assert.DoesNotContain(alerts, a => a.Contains("OUT-OF-ORDER SEQUENCE"));
    }

    [Fact]
    public void AnalyzeReading_SequenceNumberGap_OutOfOrderAlert()
    {
        // Arrange
        var detector = new AlertDetector();
        var reading1 = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = DateTime.UtcNow,
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 1
        };
        var reading2 = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = DateTime.UtcNow.AddSeconds(1),
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 5 // Gap: should be 2
        };

        // Act
        detector.AnalyzeReading(reading1);
        var alerts = detector.AnalyzeReading(reading2);

        // Assert
        Assert.True(alerts.Any(a => a.Contains("OUT-OF-ORDER SEQUENCE")), "Should contain out-of-order sequence alert");
        Assert.True(alerts.Any(a => a.Contains("Expected 2")), "Should contain expected sequence number");
    }

    [Fact]
    public void AnalyzeReading_MultipleDevices_IndependentTracking()
    {
        // Arrange
        var detector = new AlertDetector();
        var reading1A = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = DateTime.UtcNow,
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 1
        };
        var reading2A = new TelemetryReading
        {
            DeviceId = "PUMP-002",
            Timestamp = DateTime.UtcNow,
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 1
        };
        var reading1B = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = DateTime.UtcNow.AddSeconds(1),
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 2
        };
        var reading2B = new TelemetryReading
        {
            DeviceId = "PUMP-002",
            Timestamp = DateTime.UtcNow.AddSeconds(1),
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 3 // Out of order for PUMP-002
        };

        // Act
        detector.AnalyzeReading(reading1A);
        detector.AnalyzeReading(reading2A);
        detector.AnalyzeReading(reading1B);
        var alerts = detector.AnalyzeReading(reading2B);

        // Assert
        Assert.True(alerts.Any(a => a.Contains("OUT-OF-ORDER SEQUENCE") && a.Contains("PUMP-002")), 
            "Should contain out-of-order alert for PUMP-002 only");
        Assert.DoesNotContain(alerts, a => a.Contains("PUMP-001"));
    }

    [Fact]
    public void CheckForSilentDevices_NoSilence_NoAlert()
    {
        // Arrange
        var detector = new AlertDetector();
        var now = DateTime.UtcNow;
        var reading = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = now,
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 1
        };

        // Act
        detector.AnalyzeReading(reading);
        var alerts = detector.CheckForSilentDevices(now.AddSeconds(2)); // Only 2 seconds later

        // Assert
        Assert.Empty(alerts);
    }

    [Fact]
    public void CheckForSilentDevices_DeviceSilentMoreThan5Seconds_SilenceAlert()
    {
        // Arrange
        var detector = new AlertDetector();
        var now = DateTime.UtcNow;
        var reading = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = now,
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 1
        };

        // Act
        detector.AnalyzeReading(reading);
        var alerts = detector.CheckForSilentDevices(now.AddSeconds(6)); // 6 seconds later

        // Assert
        Assert.Single(alerts);
        Assert.True(alerts[0].Contains("DEVICE SILENT"), "Should contain device silent alert");
        Assert.True(alerts[0].Contains("PUMP-001"), "Should reference the silent device");
    }

    [Fact]
    public void CheckForSilentDevices_MultipleDevicesSilent_MultipleAlerts()
    {
        // Arrange
        var detector = new AlertDetector();
        var now = DateTime.UtcNow;
        var reading1 = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = now,
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 1
        };
        var reading2 = new TelemetryReading
        {
            DeviceId = "PUMP-002",
            Timestamp = now,
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 1
        };
        var reading3 = new TelemetryReading
        {
            DeviceId = "PUMP-003",
            Timestamp = now.AddSeconds(2),
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 1
        };

        // Act
        detector.AnalyzeReading(reading1);
        detector.AnalyzeReading(reading2);
        detector.AnalyzeReading(reading3);
        var alerts = detector.CheckForSilentDevices(now.AddSeconds(7)); // 7 seconds from initial reads

        // Assert
        Assert.Equal(2, alerts.Count); // PUMP-001 and PUMP-002 are silent
        Assert.True(alerts.Any(a => a.Contains("PUMP-001")));
        Assert.True(alerts.Any(a => a.Contains("PUMP-002")));
        Assert.DoesNotContain(alerts, a => a.Contains("PUMP-003")); // Recently updated
    }

    [Fact]
    public void CheckForSilentDevices_DeviceUpdated_RemovesSilenceAlert()
    {
        // Arrange
        var detector = new AlertDetector();
        var now = DateTime.UtcNow;
        var reading1 = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = now,
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 1
        };

        // Act - initial reading
        detector.AnalyzeReading(reading1);
        var silenceAlerts1 = detector.CheckForSilentDevices(now.AddSeconds(6));

        // Device sends update before check
        var reading2 = new TelemetryReading
        {
            DeviceId = "PUMP-001",
            Timestamp = now.AddSeconds(6),
            Pressure = 55.0,
            Temperature = 25.0,
            SequenceNumber = 2
        };
        detector.AnalyzeReading(reading2);
        var silenceAlerts2 = detector.CheckForSilentDevices(now.AddSeconds(7)); // Only 1 second after update

        // Assert
        Assert.Single(silenceAlerts1); // First check shows silence
        Assert.Empty(silenceAlerts2); // After update, no silence
    }
}
