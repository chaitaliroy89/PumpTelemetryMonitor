using Pump.Telemetry.Monitor.Models;
using System.Threading.Channels;

namespace Pump.Telemetry.Monitor;

/// <summary>
/// Simulates an industrial device that emits telemetry readings at random intervals.
/// </summary>
public class DeviceSimulator
{
    private readonly string _deviceId;
    private readonly Channel<TelemetryReading> _channel;
    private readonly DataLossTracker? _dataLossTracker;
    private readonly Random _random;
    private long _sequenceNumber = 0;

    /// <summary>
    /// Creates a new device simulator.
    /// </summary>
    /// <param name="deviceId">Unique identifier for this device</param>
    /// <param name="channel">Channel to write readings to</param>
    /// <param name="dataLossTracker">Optional tracker for data loss monitoring</param>
    public DeviceSimulator(string deviceId, Channel<TelemetryReading> channel, DataLossTracker? dataLossTracker = null)
    {
        _deviceId = deviceId;
        _channel = channel;
        _dataLossTracker = dataLossTracker;
        _random = new Random();
    }

    /// <summary>
    /// Runs the simulator, emitting readings asynchronously.
    /// </summary>
    /// <param name="duration">How long to run the simulator</param>
    /// <param name="cancellationToken">Token to signal shutdown</param>
    public async Task RunAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            while (stopwatch.Elapsed < duration && !cancellationToken.IsCancellationRequested)
            {
                // Random interval between 100ms and 500ms
                int delayMs = _random.Next(100, 501);
                await Task.Delay(delayMs, cancellationToken);

                // Generate reading with slight random variations
                var reading = new TelemetryReading
                {
                    DeviceId = _deviceId,
                    Timestamp = DateTime.UtcNow,
                    Pressure = 50 + (_random.NextDouble() * 10), // 50-60 PSI nominal
                    Temperature = 25 + (_random.NextDouble() * 5), // 25-30°C nominal
                    SequenceNumber = Interlocked.Increment(ref _sequenceNumber)
                };

                // Write to channel
                try
                {
                    _dataLossTracker?.RecordWriteAttempt();
                    await _channel.Writer.WriteAsync(reading, cancellationToken);
                }
                catch (ChannelClosedException)
                {
                    Console.WriteLine($"[{_deviceId}] Channel closed, stopping simulator");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        finally
        {
            Console.WriteLine($"[{_deviceId}] Simulator stopped. Total readings: {_sequenceNumber}");
        }
    }
}
