namespace BenchmarkingDemo;

public class User{
    public int Id { get; init; }
    public required string Name { get; init; } 
    public int Age { get; init; }
    
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Age).HasColumnName("age");
        });
    }
}

[MemoryDiagnoser]
public class DapperVsEfPgBenchmarks
{
    private NpgsqlConnection _conn = null!;
    private AppDbContext _db = null!;

    private static readonly Func<AppDbContext, int, IEnumerable<User>> EfCompiledQuery =
        EF.CompileQuery((AppDbContext ctx, int age) =>
            ctx.Users.AsNoTracking().Where(u => u.Age == age));

    [GlobalSetup]
    public void Setup()
    {
        _conn = new NpgsqlConnection(ConnectionString);
        _conn.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_conn)
            .EnableSensitiveDataLogging(false)
            .Options;

        _db = new AppDbContext(options);
    }

    private static string ConnectionString => "Host=localhost;Port=5432;Database=benchmarks;Username=postgres;Password=postgres";

    [Benchmark]
    public List<User> Dapper()
    {
        return _conn
            .Query<User>("SELECT * FROM users WHERE age = @age", new { age = 30 })
            .AsList();
    }

    [Benchmark]
    public List<User> EfCore_NoTracking()
    {
        return _db.Users
            .AsNoTracking()
            .Where(u => u.Age == 30)
            .ToList();
    }

    [Benchmark]
    public List<User> EfCore_Compiled()
    {
        return EfCompiledQuery(_db, 30).ToList();
    }
}