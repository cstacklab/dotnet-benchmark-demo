namespace BenchmarkingDemo;

/// <summary>
/// Generates orders in reporting-date order, one at a time — an in-memory stand-in for
/// EF Core's AsAsyncEnumerable() over a query with ORDER BY order_date. Each order is
/// stamped with its quarter-end reporting date (four per year), and rows for the same
/// reporting date arrive together. Being a generator, it lets the peak runner scale to
/// arbitrary row counts without touching the database.
/// </summary>
public static class OrderDataGenerator
{
    public static async IAsyncEnumerable<Order> GenerateAsync(int rowsPerReportingDate, int reportingDates)
    {
        var firstPeriod = new DateTime(2016, 1, 1);
        var id = 0;

        for (var q = 0; q < reportingDates; q++)
        {
            var reportingDate = firstPeriod.AddMonths(3 * q).ToReportingDate();
            for (var r = 0; r < rowsPerReportingDate; r++)
            {
                id++;
                yield return new Order
                {
                    Id = id,
                    UserId = r % 10_000,
                    OrderDate = reportingDate,
                    TotalAmount = 10m + id % 500,
                    Status = "completed",
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
        IAsyncEnumerable<Order> source, CancellationToken ct)
    {
        List<Order> allOrders = [];
        await foreach (var order in source.WithCancellation(ct))
        {
            allOrders.Add(order);
        }

        return allOrders
            .GroupBy(o => o.OrderDate.ToReportingDate())
            .Select(g => AggregateReportingDate(g.Key, g.ToList()))
            .ToList();
    }

    // NEW: hold one reporting date's rows at a time; aggregate and clear at each boundary.
    public static async Task<List<ReportingDateSummary>> StreamingAsync(
        IAsyncEnumerable<Order> source, CancellationToken ct)
    {
        List<Order> buffer = [];
        DateTime? currentReportingDate = null;
        List<ReportingDateSummary> aggregated = [];

        await foreach (var order in source.WithCancellation(ct))
        {
            var reportingDate = order.OrderDate.ToReportingDate();

            if (buffer.Count > 0 && reportingDate != currentReportingDate)
            {
                aggregated.Add(AggregateReportingDate(currentReportingDate!.Value, buffer));
                buffer.Clear();
            }

            currentReportingDate = reportingDate;
            buffer.Add(order);
        }

        if (buffer.Count > 0)
        {
            aggregated.Add(AggregateReportingDate(currentReportingDate!.Value, buffer));
        }

        return aggregated;
    }

    private static ReportingDateSummary AggregateReportingDate(DateTime reportingDate, List<Order> orders)
    {
        var total = orders.Sum(o => o.TotalAmount);
        return new ReportingDateSummary(reportingDate, orders.Count, total, total / orders.Count);
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
                OrderDataGenerator.GenerateAsync(rowsPerReportingDate, reportingDates),
                CancellationToken.None));

        Measure("Streaming (new)", () =>
            ReportingDateAggregators.StreamingAsync(
                OrderDataGenerator.GenerateAsync(rowsPerReportingDate, reportingDates),
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
