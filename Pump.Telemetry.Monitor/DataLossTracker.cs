namespace Pump.Telemetry.Monitor;

/// <summary>
/// Tracks data loss due to buffer overflow (DropWrite policy).
/// Monitors how many readings are dropped vs successfully processed.
/// </summary>
public class DataLossTracker
{
    private long _totalWriteAttempts = 0;
    private long _totalSuccessfulReads = 0;
    private long _lastReportedLoss = 0;
    private readonly object _lockObject = new();

    /// <summary>
    /// Records a write attempt by a simulator.
    /// </summary>
    public void RecordWriteAttempt()
    {
        Interlocked.Increment(ref _totalWriteAttempts);
    }

    /// <summary>
    /// Records a successful read/processing by the consumer.
    /// </summary>
    public void RecordSuccessfulRead()
    {
        Interlocked.Increment(ref _totalSuccessfulReads);
    }

    /// <summary>
    /// Calculates data loss and generates alert if significant loss detected.
    /// Should be called periodically during execution.
    /// </summary>
    public void CheckAndAlertDataLoss()
    {
        lock (_lockObject)
        {
            long currentLoss = _totalWriteAttempts - _totalSuccessfulReads;
            long newLoss = currentLoss - _lastReportedLoss;

            if (newLoss > 10)  // Alert if more than 10 items lost since last check
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"***DATA LOSS ALERT: {newLoss} readings dropped due to buffer overflow (DropWrite)");
                Console.WriteLine($"***Cumulative: Total written={_totalWriteAttempts} | Processed={_totalSuccessfulReads} | Lost={currentLoss}");
                Console.ResetColor();
                _lastReportedLoss = currentLoss;
            }
        }
    }

    /// <summary>
    /// Gets current data loss statistics.
    /// </summary>
    public (long Total, long Processed, long Lost, double LossPercent) GetStats()
    {
        lock (_lockObject)
        {
            long loss = _totalWriteAttempts - _totalSuccessfulReads;
            double lossPercent = _totalWriteAttempts > 0
                ? (loss / (double)_totalWriteAttempts) * 100
                : 0;

            return (_totalWriteAttempts, _totalSuccessfulReads, loss, lossPercent);
        }
    }

    /// <summary>
    /// Prints final data loss report to console.
    /// </summary>
    public void PrintFinalReport()
    {
        var (total, processed, lost, lossPercent) = GetStats();

        Console.WriteLine("\n╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║            DATA LOSS ANALYSIS - FINAL REPORT             ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"  Total Write Attempts:    {total:N0}");
        Console.WriteLine($"  Successfully Processed:  {processed:N0}");
        Console.WriteLine($"  Data Loss (DropWrite):   {lost:N0} items");
        Console.WriteLine($"  Loss Rate:               {lossPercent:F2}%");

        if (lost == 0)
        {
            Console.WriteLine("\n   No data loss detected - perfect system efficiency!");
        }
        else if (lossPercent < 1)
        {
            Console.WriteLine("\n   Low data loss (< 1%) - system performing well");
        }
        else if (lossPercent < 10)
        {
            Console.WriteLine("\n    Moderate data loss (1-10%) - consider increasing buffer size");
        }
        else
        {
            Console.WriteLine("\n   High data loss (> 10%) - processor cannot keep up with producers");
            Console.WriteLine("     Consider: increasing buffer size, optimizing processor, or reducing producer speed");
        }

        Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");
    }
}
