namespace BenchmarkingDemo;

/// <summary>
/// Generates account snapshots in reporting-date order, one at a time — an in-memory
/// stand-in for EF Core's AsAsyncEnumerable() over a query with ORDER BY reporting_date.
/// Rows for the same reporting date arrive together. Being a generator, it lets the peak
/// runner scale to arbitrary row counts without touching the database.
/// </summary>
public static class AccountSnapshotGenerator
{
    public static async IAsyncEnumerable<AccountSnapshot> GenerateAsync(int rowsPerReportingDate, int reportingDates)
    {
        var firstQuarterStart = new DateTime(2016, 1, 1);
        var id = 0;

        for (var q = 0; q < reportingDates; q++)
        {
            var reportingDate = firstQuarterStart.AddMonths(3 * (q + 1)).AddDays(-1); // quarter end
            for (var r = 0; r < rowsPerReportingDate; r++)
            {
                id++;
                yield return new AccountSnapshot
                {
                    Id = id,
                    UserId = r % 10_000,
                    ReportingDate = reportingDate,
                    Balance = 1000m + id % 100_000,
                    InvestedAmount = 500m + id % 50_000,
                };

                if (id % 1000 == 0)
                {
                    await Task.Yield();
                }
            }
        }
    }
}

/// <summary>
/// The two consumption strategies over the same sorted stream.
/// </summary>
public static class ReportingDateAggregators
{
    // OLD: drain the whole stream into one list first, then aggregate.
    // The list roots every row until the method returns.
    public static async Task<List<ReportingDateSummary>> MaterializeAsync(
        IAsyncEnumerable<AccountSnapshot> source, CancellationToken ct)
    {
        List<AccountSnapshot> allSnapshots = [];
        await foreach (var snapshot in source.WithCancellation(ct))
        {
            allSnapshots.Add(snapshot);
        }

        return allSnapshots
            .GroupBy(s => s.ReportingDate)
            .Select(g => AggregateReportingDate(g.Key, g.ToList()))
            .ToList();
    }

    // NEW: hold one reporting date's rows at a time; aggregate and clear at each boundary.
    public static async Task<List<ReportingDateSummary>> StreamingAsync(
        IAsyncEnumerable<AccountSnapshot> source, CancellationToken ct)
    {
        List<AccountSnapshot> buffer = [];
        DateTime? currentReportingDate = null;
        List<ReportingDateSummary> aggregated = [];

        await foreach (var snapshot in source.WithCancellation(ct))
        {
            if (buffer.Count > 0 && snapshot.ReportingDate != currentReportingDate)
            {
                aggregated.Add(AggregateReportingDate(currentReportingDate!.Value, buffer));
                buffer.Clear();
            }

            currentReportingDate = snapshot.ReportingDate;
            buffer.Add(snapshot);
        }

        if (buffer.Count > 0)
        {
            aggregated.Add(AggregateReportingDate(currentReportingDate!.Value, buffer));
        }

        return aggregated;
    }

    private static ReportingDateSummary AggregateReportingDate(DateTime reportingDate, List<AccountSnapshot> snapshots)
    {
        return new ReportingDateSummary(
            reportingDate,
            snapshots.Count,
            snapshots.Sum(s => s.Balance),
            snapshots.Sum(s => s.InvestedAmount));
    }
}

/// <summary>
/// Measures the number BenchmarkDotNet's MemoryDiagnoser cannot show: peak retained
/// (live) managed heap during the call. Total allocations are near-identical for both
/// strategies, but the live set differs dramatically — the materialised list roots the
/// whole history so it cannot be collected, whereas streamed rows become collectable
/// once their reporting date is aggregated.
/// </summary>
internal static class PeakMemoryRunner
{
    // Sampling forces a full collection, so keep the interval large enough to limit perturbation.
    private const int SampleIntervalMilliseconds = 3;

    public static void Run(int rowsPerReportingDate, int reportingDates)
    {
        long totalRows = (long)rowsPerReportingDate * reportingDates;

        Console.WriteLine($"Rows/reporting date = {rowsPerReportingDate:N0}   Reporting dates = {reportingDates}   Total rows = {totalRows:N0}");
        Console.WriteLine("(peak = maximum live/retained managed heap during the call)");
        Console.WriteLine();

        Measure("Materialize (old)", () =>
            ReportingDateAggregators.MaterializeAsync(
                AccountSnapshotGenerator.GenerateAsync(rowsPerReportingDate, reportingDates),
                CancellationToken.None));

        Measure("Streaming (new)", () =>
            ReportingDateAggregators.StreamingAsync(
                AccountSnapshotGenerator.GenerateAsync(rowsPerReportingDate, reportingDates),
                CancellationToken.None));
    }

    private static void Measure(string label, Func<Task> workload)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long baseline = GC.GetTotalMemory(forceFullCollection: true);
        long peak = baseline;
        using CancellationTokenSource cts = new();

        Task sampler = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                // forceFullCollection: true returns the live set after a blocking collection, so dead
                // rows the streaming path already released are excluded from the measurement.
                long live = GC.GetTotalMemory(forceFullCollection: true);
                if (live > peak)
                {
                    peak = live;
                }

                Thread.Sleep(SampleIntervalMilliseconds);
            }
        }, cts.Token);

        workload().GetAwaiter().GetResult();

        cts.Cancel();
        sampler.GetAwaiter().GetResult();

        double peakMb = (peak - baseline) / (1024.0 * 1024.0);
        Console.WriteLine($"{label,-20} peak retained heap: +{peakMb,8:F1} MB");
    }
}
