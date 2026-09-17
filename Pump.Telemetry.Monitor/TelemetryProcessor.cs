using Pump.Telemetry.Monitor.Models;
using System.Threading.Channels;

namespace Pump.Telemetry.Monitor;

/// <summary>
/// Processes telemetry readings from a channel and generates alerts.
/// Runs as a background task consuming from a Channel&lt;TelemetryReading&gt;.
/// </summary>
public class TelemetryProcessor
{
    private readonly Channel<TelemetryReading> _channel;
    private readonly AlertDetector _alertDetector;
    private readonly DataLossTracker? _dataLossTracker;

    /// <summary>
    /// Creates a new telemetry processor.
    /// </summary>
    /// <param name="channel">Channel to read readings from</param>
    /// <param name="dataLossTracker">Optional tracker for data loss monitoring</param>
    public TelemetryProcessor(Channel<TelemetryReading> channel, DataLossTracker? dataLossTracker = null)
    {
        _channel = channel;
        _dataLossTracker = dataLossTracker;
        _alertDetector = new AlertDetector();
    }

    /// <summary>
    /// Processes telemetry readings from the channel.
    /// This method runs until the channel is closed.
    /// </summary>
    /// <param name="cancellationToken">Token to signal shutdown</param>
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var lastSilenceCheckTime = DateTime.UtcNow;
        var lastDataLossCheckTime = DateTime.UtcNow;
        const int SilenceCheckIntervalMs = 1000; // Check every 1 second
        const int DataLossCheckIntervalMs = 2000; // Check data loss every 2 seconds

        try
        {
            await foreach (var reading in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                // Record successful read
                _dataLossTracker?.RecordSuccessfulRead();

                // Process the reading
                var readingAlerts = _alertDetector.AnalyzeReading(reading);

                // Print normal reading
                Console.WriteLine($"  {reading}");

                // Print any alerts from analysis
                foreach (var alert in readingAlerts)
                {
                    Console.WriteLine($"  {alert}");
                }

                // Periodically check for silent devices
                var now = DateTime.UtcNow;
                if ((now - lastSilenceCheckTime).TotalMilliseconds >= SilenceCheckIntervalMs)
                {
                    var silenceAlerts = _alertDetector.CheckForSilentDevices(now);
                    foreach (var alert in silenceAlerts)
                    {
                        Console.WriteLine($"  {alert}");
                    }
                    lastSilenceCheckTime = now;
                }

                // Periodically check for data loss
                if ((now - lastDataLossCheckTime).TotalMilliseconds >= DataLossCheckIntervalMs)
                {
                    _dataLossTracker?.CheckAndAlertDataLoss();
                    lastDataLossCheckTime = now;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        finally
        {
            Console.WriteLine("Processor stopped.");
        }
    }
}
