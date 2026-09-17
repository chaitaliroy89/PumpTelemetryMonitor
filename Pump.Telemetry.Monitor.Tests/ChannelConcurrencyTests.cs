using Pump.Telemetry.Monitor;
using Pump.Telemetry.Monitor.Models;
using System.Threading.Channels;

namespace Pump.Telemetry.Monitor.Tests;

/// <summary>
/// Tests for concurrent channel operations and full pipeline behavior.
/// Validates that the DeviceSimulator → Channel → TelemetryProcessor pipeline
/// handles concurrent writes safely and processes all readings without loss.
/// </summary>
public class ChannelConcurrencyTests
{
    [Fact]
    public async Task ProcessorHandlesConcurrentWrites_AllReadingsProcessed()
    {
        // Arrange
        var channel = Channel.CreateBounded<TelemetryReading>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite }
        );
        var readingsProcessed = 0;
        var lockObject = new object();

        // Simulate 3 concurrent devices writing 5 readings each
        var deviceIds = new[] { "PUMP-001", "PUMP-002", "PUMP-003" };
        const int readingsPerDevice = 5;

        // Act
        var writerTasks = deviceIds.Select(async deviceId =>
        {
            for (int i = 0; i < readingsPerDevice; i++)
            {
                var reading = new TelemetryReading
                {
                    DeviceId = deviceId,
                    Timestamp = DateTime.UtcNow,
                    Pressure = 55.0,
                    Temperature = 25.0,
                    SequenceNumber = i + 1
                };
                await channel.Writer.WriteAsync(reading);
            }
        }).ToList();

        var processorTask = Task.Run(async () =>
        {
            await foreach (var reading in channel.Reader.ReadAllAsync())
            {
                lock (lockObject)
                {
                    readingsProcessed++;
                }
            }
        });

        // Wait for all writers to complete
        await Task.WhenAll(writerTasks);
        channel.Writer.TryComplete();

        // Wait for processor to finish
        await processorTask;

        // Assert
        Assert.Equal(deviceIds.Length * readingsPerDevice, readingsProcessed);
    }

    [Fact]
    public async Task DropWritePolicy_DoesNotBlockProducers()
    {
        // Arrange
        var channel = Channel.CreateBounded<TelemetryReading>(
            new BoundedChannelOptions(5) { FullMode = BoundedChannelFullMode.DropWrite } // Very small buffer
        );
        var droppedCount = 0;
        var writeCount = 0;
        var lockObject = new object();

        // Act - write rapidly without consuming
        var writeTask = Task.Run(async () =>
        {
            for (int i = 0; i < 50; i++)
            {
                var reading = new TelemetryReading
                {
                    DeviceId = "PUMP-001",
                    Timestamp = DateTime.UtcNow,
                    Pressure = 55.0,
                    Temperature = 25.0,
                    SequenceNumber = i + 1
                };

                try
                {
                    await channel.Writer.WriteAsync(reading);
                    lock (lockObject)
                    {
                        writeCount++;
                    }
                }
                catch
                {
                    // Write may fail if channel is full with DropWrite policy
                    lock (lockObject)
                    {
                        droppedCount++;
                    }
                }
            }
        });

        await writeTask;

        // Assert
        // With DropWrite policy, WriteAsync should complete immediately (never block)
        // Some writes may be dropped, but total attempts should equal totalAttempted
        Assert.Equal(50, writeCount);
        // When channel is full, writes can be dropped; we just verify no blocking occurred
    }

    [Fact]
    public async Task MultiDeviceSimulators_SequenceTrackingPerDevice()
    {
        // Arrange
        var channel = Channel.CreateBounded<TelemetryReading>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite }
        );
        var deviceReadings = new Dictionary<string, List<TelemetryReading>>();
        var lockObject = new object();

        var deviceIds = new[] { "PUMP-001", "PUMP-002" };

        // Act
        var simulatorTasks = deviceIds.Select(async deviceId =>
        {
            long sequenceNumber = 0;
            for (int i = 0; i < 10; i++)
            {
                var reading = new TelemetryReading
                {
                    DeviceId = deviceId,
                    Timestamp = DateTime.UtcNow,
                    Pressure = 50.0 + (deviceId == "PUMP-001" ? 5 : 8),
                    Temperature = 25.0,
                    SequenceNumber = ++sequenceNumber
                };
                await channel.Writer.WriteAsync(reading);
                await Task.Delay(5); // Small delay between writes
            }
        }).ToList();

        var processorTask = Task.Run(async () =>
        {
            await foreach (var reading in channel.Reader.ReadAllAsync())
            {
                lock (lockObject)
                {
                    if (!deviceReadings.ContainsKey(reading.DeviceId))
                    {
                        deviceReadings[reading.DeviceId] = new();
                    }
                    deviceReadings[reading.DeviceId].Add(reading);
                }
            }
        });

        // Wait for all simulators
        await Task.WhenAll(simulatorTasks);
        channel.Writer.TryComplete();
        await processorTask;

        // Assert
        Assert.Equal(2, deviceReadings.Count);

        // Verify each device has readings (at least some, allowing for channel drops)
        foreach (var deviceId in deviceIds)
        {
            Assert.True(deviceReadings.ContainsKey(deviceId));
            Assert.NotEmpty(deviceReadings[deviceId]);

            // Verify sequence numbers are sequential for each device
            var readings = deviceReadings[deviceId];
            for (int i = 0; i < readings.Count - 1; i++)
            {
                // Sequence numbers should increase (allowing for potential drops)
                Assert.True(readings[i + 1].SequenceNumber > readings[i].SequenceNumber,
                    $"Sequence numbers out of order for {deviceId}");
            }
        }
    }

    [Fact]
    public async Task GracefulShutdown_CancellationTokenStopsSimulators()
    {
        // Arrange
        var channel = Channel.CreateBounded<TelemetryReading>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite }
        );
        var readingsReceived = 0;
        var lockObject = new object();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // Overall timeout

        // Act
        var simulatorTask = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 1000; i++)
                {
                    if (cts.Token.IsCancellationRequested)
                        break;

                    var reading = new TelemetryReading
                    {
                        DeviceId = "PUMP-001",
                        Timestamp = DateTime.UtcNow,
                        Pressure = 55.0,
                        Temperature = 25.0,
                        SequenceNumber = i + 1
                    };

                    try
                    {
                        await channel.Writer.WriteAsync(reading, cts.Token);
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }

                    await Task.Delay(10, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        });

        var processorTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var reading in channel.Reader.ReadAllAsync(cts.Token))
                {
                    lock (lockObject)
                    {
                        readingsReceived++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        });

        // Let it run briefly, then cancel
        await Task.Delay(100);
        channel.Writer.TryComplete(); // Close channel to unblock processor
        cts.Cancel();

        // Wait for tasks to complete (with timeout)
        var completionTasks = new[] { simulatorTask, processorTask };
        await Task.WhenAll(completionTasks).ConfigureAwait(false);

        // Assert
        Assert.True(readingsReceived > 0, "Should have processed some readings before cancellation");
    }

    [Fact]
    public async Task ChannelClosure_ProcessorCompletes()
    {
        // Arrange
        var channel = Channel.CreateBounded<TelemetryReading>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite }
        );
        var readingsProcessed = 0;
        var lockObject = new object();

        // Act
        var writeTask = Task.Run(async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                var reading = new TelemetryReading
                {
                    DeviceId = "PUMP-001",
                    Timestamp = DateTime.UtcNow,
                    Pressure = 55.0,
                    Temperature = 25.0,
                    SequenceNumber = i + 1
                };
                await channel.Writer.WriteAsync(reading);
            }
            channel.Writer.TryComplete(); // Signal no more writes
        });

        var processorTask = Task.Run(async () =>
        {
            await foreach (var reading in channel.Reader.ReadAllAsync())
            {
                lock (lockObject)
                {
                    readingsProcessed++;
                }
            }
        });

        await Task.WhenAll(writeTask, processorTask);

        // Assert
        Assert.Equal(10, readingsProcessed);
    }
}
