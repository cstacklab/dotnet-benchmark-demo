namespace BenchmarkingDemo;

// One account's state as of a quarter-end reporting date
public class AccountSnapshot
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public DateTime ReportingDate { get; init; }
    public decimal Balance { get; init; }
    public decimal InvestedAmount { get; init; }
}

// Result of aggregating one reporting date's worth of snapshots
public record ReportingDateSummary(DateTime ReportingDate, int AccountCount, decimal TotalBalance, decimal TotalInvested);

public class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public DbSet<AccountSnapshot> AccountSnapshots => Set<AccountSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountSnapshot>(entity =>
        {
            entity.ToTable("account_snapshots");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ReportingDate).HasColumnName("reporting_date");
            entity.Property(e => e.Balance).HasColumnName("balance");
            entity.Property(e => e.InvestedAmount).HasColumnName("invested_amount");
        });
    }
}

/// <summary>
/// Materialize vs Streaming: builds a per-reporting-date (quarter-end) summary over the
/// account_snapshots table (5,000 accounts x 40 reporting dates = 200,000 rows).
///
/// Old way (Materialize): the service returns Task&lt;List&lt;AccountSnapshot&gt;&gt; via ToListAsync() —
/// every row is held in memory at once before aggregation starts.
///
/// New way (Streaming): the service returns IAsyncEnumerable&lt;AccountSnapshot&gt; via
/// AsAsyncEnumerable() — rows flow one at a time. Because SQL guarantees
/// ORDER BY reporting_date, all rows for a reporting date arrive together, so the moment
/// the reporting date changes the previous period is complete: aggregate the small buffer,
/// clear it, move on. Peak live memory is one quarter's rows instead of all 200,000.
///
/// Total allocated is similar for both (every row is touched once either way); the difference
/// shows up in peak retained memory and GC generation counts — streamed rows die in Gen0.
/// </summary>
[MemoryDiagnoser]
public class StreamingVsMaterializeBenchmarks
{
    private NpgsqlConnection _conn = null!;
    private ReportingDbContext _db = null!;

    private const string ConnectionString = "Host=localhost;Port=5432;Database=benchmarks;Username=postgres;Password=postgres";

    [GlobalSetup]
    public void Setup()
    {
        _conn = new NpgsqlConnection(ConnectionString);
        _conn.Open();

        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql(_conn)
            .EnableSensitiveDataLogging(false)
            .Options;

        _db = new ReportingDbContext(options);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    // OLD: "here is the whole basket, already filled"
    private async Task<List<AccountSnapshot>> GetSnapshotsMaterializedAsync()
    {
        return await _db.AccountSnapshots
            .AsNoTracking()
            .OrderBy(s => s.ReportingDate)
            .ToListAsync();
    }

    // NEW: "here is a belt; ask me for the next row when you're ready"
    private IAsyncEnumerable<AccountSnapshot> GetSnapshotsStreamAsync()
    {
        return _db.AccountSnapshots
            .AsNoTracking()
            .OrderBy(s => s.ReportingDate)
            .AsAsyncEnumerable();
    }

    [Benchmark(Baseline = true)]
    public async Task<List<ReportingDateSummary>> Materialize_ToListAsync()
    {
        var allSnapshots = await GetSnapshotsMaterializedAsync();

        return allSnapshots
            .GroupBy(s => s.ReportingDate)
            .Select(g => AggregateReportingDate(g.Key, g.ToList()))
            .ToList();
    }

    [Benchmark]
    public async Task<List<ReportingDateSummary>> Streaming_AsAsyncEnumerable()
    {
        List<AccountSnapshot> buffer = [];          // holds the current reporting date only
        DateTime? currentReportingDate = null;
        List<ReportingDateSummary> aggregated = [];

        await foreach (var snapshot in GetSnapshotsStreamAsync())
        {
            // A new reporting date appeared — the previous period is complete (rows arrive
            // sorted), so aggregate it and throw the old rows away.
            if (buffer.Count > 0 && snapshot.ReportingDate != currentReportingDate)
            {
                aggregated.Add(AggregateReportingDate(currentReportingDate!.Value, buffer));
                buffer.Clear();
            }

            currentReportingDate = snapshot.ReportingDate;
            buffer.Add(snapshot);
        }

        // The stream ended, but the last reporting date is still in the buffer.
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
