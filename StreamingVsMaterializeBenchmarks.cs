namespace BenchmarkingDemo;

// Result of aggregating one day's worth of orders
public record DailyOrderSummary(DateTime Date, int OrderCount, decimal TotalRevenue, decimal AverageOrderValue);

/// <summary>
/// Materialize vs Streaming: builds a per-day summary over the orders table (50,000 rows).
///
/// Old way (Materialize): the service returns Task&lt;List&lt;Order&gt;&gt; via ToListAsync() —
/// every row is held in memory at once before aggregation starts.
///
/// New way (Streaming): the service returns IAsyncEnumerable&lt;Order&gt; via AsAsyncEnumerable() —
/// rows flow one at a time. Because SQL guarantees ORDER BY order_date, all rows for a day
/// arrive together, so the moment the date changes the previous day is complete: aggregate
/// the small buffer, clear it, move on. Peak live memory is one day's rows instead of all 50,000.
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
    public async Task<List<DailyOrderSummary>> Materialize_ToListAsync()
    {
        var allOrders = await GetOrdersMaterializedAsync();

        return allOrders
            .GroupBy(o => o.OrderDate.Date)
            .Select(g => AggregateDay(g.Key, g.ToList()))
            .ToList();
    }

    [Benchmark]
    public async Task<List<DailyOrderSummary>> Streaming_AsAsyncEnumerable()
    {
        List<Order> buffer = [];                // holds the current day only
        DateTime? currentDate = null;
        List<DailyOrderSummary> aggregated = [];

        await foreach (var order in GetOrdersStreamAsync())
        {
            var orderDate = order.OrderDate.Date;

            // A new date appeared — the previous day is complete (rows arrive sorted),
            // so aggregate it and throw the old rows away.
            if (buffer.Count > 0 && orderDate != currentDate)
            {
                aggregated.Add(AggregateDay(currentDate!.Value, buffer));
                buffer.Clear();
            }

            currentDate = orderDate;
            buffer.Add(order);
        }

        // The stream ended, but the last day is still in the buffer.
        if (buffer.Count > 0)
        {
            aggregated.Add(AggregateDay(currentDate!.Value, buffer));
        }

        return aggregated;
    }

    private static DailyOrderSummary AggregateDay(DateTime date, List<Order> orders)
    {
        var total = orders.Sum(o => o.TotalAmount);
        return new DailyOrderSummary(date, orders.Count, total, total / orders.Count);
    }
}
