using Pump.Telemetry.Monitor;
using System.Threading.Channels;

Console.WriteLine("=== Pump Telemetry Monitor ===");
Console.WriteLine("Starting telemetry simulation...");
Console.WriteLine("Press Ctrl+C to stop gracefully.\n");

// Create data loss tracker to monitor dropped items
var dataLossTracker = new DataLossTracker();

// Create a bounded channel for telemetry readings (non-blocking producer)
var channel = Channel.CreateBounded<Pump.Telemetry.Monitor.Models.TelemetryReading>(
    new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite }
);

// Create CancellationTokenSource for graceful shutdown
using var cts = new CancellationTokenSource();

// Setup Ctrl+C handler for graceful shutdown
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n\nShutdown signal received, stopping gracefully...");
    cts.Cancel();
};

try
{
    // Create simulators for 4 devices
    var devices = new[] { "PUMP-001", "PUMP-002", "PUMP-003", "PUMP-004" };
    var simulatorTasks = devices.Select(deviceId =>
        new DeviceSimulator(deviceId, channel, dataLossTracker).RunAsync(TimeSpan.FromMinutes(1), cts.Token)
    ).ToList();

    // Create processor task
    var processor = new TelemetryProcessor(channel, dataLossTracker);
    var processorTask = processor.ProcessAsync(cts.Token);

    // Run all tasks
    await Task.WhenAll(simulatorTasks);

    // Signal that no more readings will be added
    channel.Writer.TryComplete();

    // Wait for processor to finish consuming
    await processorTask;

    Console.WriteLine("\n=== Telemetry Monitor Stopped ===");

    // Print final data loss report
    dataLossTracker.PrintFinalReport();
}
catch (OperationCanceledException)
{
    Console.WriteLine("Application cancelled by user.");
    dataLossTracker.PrintFinalReport();
}
finally
{
    channel.Writer.TryComplete();
    cts.Dispose();
}
