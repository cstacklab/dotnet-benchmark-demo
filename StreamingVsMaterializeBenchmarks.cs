namespace BenchmarkingDemo;

// Result of aggregating one reporting date's worth of orders
public record ReportingDateSummary(DateTime ReportingDate, int OrderCount, decimal TotalRevenue, decimal AverageOrderValue);

public static class ReportingDates
{
    /// <summary>
    /// Maps any timestamp to its quarter-end reporting date (four per year).
    /// Monotonic in time, so rows sorted by order_date are also sorted by reporting date —
    /// each reporting date arrives as one contiguous run.
    /// </summary>
    public static DateTime ToReportingDate(this DateTime date)
    {
        var quarterEndMonth = ((date.Month - 1) / 3) * 3 + 3;
        return new DateTime(date.Year, quarterEndMonth, DateTime.DaysInMonth(date.Year, quarterEndMonth));
    }
}

/// <summary>
/// Materialize vs Streaming: builds a per-reporting-date (quarter-end) summary over the
/// orders table (50,000 rows).
///
/// Old way (Materialize): the service returns Task&lt;List&lt;Order&gt;&gt; via ToListAsync() —
/// every row is held in memory at once before aggregation starts.
///
/// New way (Streaming): the service returns IAsyncEnumerable&lt;Order&gt; via AsAsyncEnumerable() —
/// rows flow one at a time. Because SQL guarantees ORDER BY order_date, all rows for a
/// reporting date arrive together, so the moment the reporting date changes the previous
/// period is complete: aggregate the small buffer, clear it, move on. Peak live memory is
/// one quarter's rows instead of all 50,000.
///
/// Total allocated is similar for both (every row is touched once either way); the difference
/// shows up in peak retained memory and GC generation counts — streamed rows die in Gen0.
/// </summary>
[MemoryDiagnoser]
public class StreamingVsMaterializeBenchmarks
{
    private NpgsqlConnection _conn = null!;
    private ComplexAppDbContext _db = null!;

    private const string ConnectionString = "Host=localhost;Port=5432;Database=benchmarks;Username=postgres;Password=postgres";

    [GlobalSetup]
    public void Setup()
    {
        _conn = new NpgsqlConnection(ConnectionString);
        _conn.Open();

        var options = new DbContextOptionsBuilder<ComplexAppDbContext>()
            .UseNpgsql(_conn)
            .EnableSensitiveDataLogging(false)
            .Options;

        _db = new ComplexAppDbContext(options);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    // OLD: "here is the whole basket, already filled"
    private async Task<List<Order>> GetOrdersMaterializedAsync()
    {
        return await _db.Orders
            .AsNoTracking()
            .OrderBy(o => o.OrderDate)
            .ToListAsync();
    }

    // NEW: "here is a belt; ask me for the next row when you're ready"
    private IAsyncEnumerable<Order> GetOrdersStreamAsync()
    {
        return _db.Orders
            .AsNoTracking()
            .OrderBy(o => o.OrderDate)
            .AsAsyncEnumerable();
    }

    [Benchmark(Baseline = true)]
    public async Task<List<ReportingDateSummary>> Materialize_ToListAsync()
    {
        var allOrders = await GetOrdersMaterializedAsync();

        return allOrders
            .GroupBy(o => o.OrderDate.ToReportingDate())
            .Select(g => AggregateReportingDate(g.Key, g.ToList()))
            .ToList();
    }

    [Benchmark]
    public async Task<List<ReportingDateSummary>> Streaming_AsAsyncEnumerable()
    {
        List<Order> buffer = [];                    // holds the current reporting date only
        DateTime? currentReportingDate = null;
        List<ReportingDateSummary> aggregated = [];

        await foreach (var order in GetOrdersStreamAsync())
        {
            var reportingDate = order.OrderDate.ToReportingDate();

            // A new reporting date appeared — the previous period is complete (rows arrive
            // sorted), so aggregate it and throw the old rows away.
            if (buffer.Count > 0 && reportingDate != currentReportingDate)
            {
                aggregated.Add(AggregateReportingDate(currentReportingDate!.Value, buffer));
                buffer.Clear();
            }

            currentReportingDate = reportingDate;
            buffer.Add(order);
        }

        // The stream ended, but the last reporting date is still in the buffer.
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
