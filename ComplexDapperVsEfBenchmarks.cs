namespace BenchmarkingDemo;

// Domain models for complex scenario
public class UserOrder
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int Age { get; init; }
    public List<Order> Orders { get; set; } = new();
}

public class Order
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public DateTime OrderDate { get; init; }
    public decimal TotalAmount { get; init; }
    public required string Status { get; init; }
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public int ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public Product? Product { get; set; }
}

public class Product
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public decimal Price { get; init; }
    public required string Category { get; init; }
    public int Stock { get; init; }
}

public class ComplexAppDbContext(DbContextOptions<ComplexAppDbContext> options) : DbContext(options)
{
    public DbSet<UserOrder> UserOrders => Set<UserOrder>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // UserOrder configuration
        modelBuilder.Entity<UserOrder>(entity =>
        {
            entity.ToTable("users");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Age).HasColumnName("age");
        });

        // Order configuration
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.OrderDate).HasColumnName("order_date");
            entity.Property(e => e.TotalAmount).HasColumnName("total_amount");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);

            entity.HasOne<UserOrder>()
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .HasPrincipalKey(u => u.Id);
        });

        // OrderItem configuration
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.UnitPrice).HasColumnName("unit_price");

            entity.HasOne<Order>()
                .WithMany(o => o.Items)
                .HasForeignKey(oi => oi.OrderId)
                .HasPrincipalKey(o => o.Id);

            entity.HasOne(oi => oi.Product)
                .WithMany()
                .HasForeignKey(oi => oi.ProductId);
        });

        // Product configuration
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Price).HasColumnName("price");
            entity.Property(e => e.Category).HasColumnName("category").HasMaxLength(100);
            entity.Property(e => e.Stock).HasColumnName("stock");
        });
    }
}

[MemoryDiagnoser]
public class ComplexDapperVsEfBenchmarks
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

    /// <summary>
    /// Dapper: Raw SQL with multiple joins to fetch users with orders and order items
    /// </summary>
    [Benchmark(Baseline = true)]
    public List<UserOrder> Dapper_MultipleJoins()
    {
        const string sql = """
                               SELECT u.id, u.name, u.age, 
                                      o.id, o.user_id, o.order_date, o.total_amount, o.status,
                                      oi.id, oi.order_id, oi.product_id, oi.quantity, oi.unit_price,
                                      p.id, p.name, p.price, p.category, p.stock
                               FROM users u
                               LEFT JOIN orders o ON u.id = o.user_id
                               LEFT JOIN order_items oi ON o.id = oi.order_id
                               LEFT JOIN products p ON oi.product_id = p.id
                               WHERE u.age > @minAge
                               ORDER BY u.id, o.id, oi.id
                           """;

        var userDict = new Dictionary<int, UserOrder>();

        _conn.Query<UserOrder, Order?, OrderItem?, Product?, UserOrder>(
            sql,
            (user, order, item, product) =>
            {
                if (!userDict.TryGetValue(user.Id, out var userEntry))
                {
                    userEntry = user;
                    userDict[user.Id] = userEntry;
                }

                if (order is not null)
                {
                    var existingOrder = userEntry.Orders.FirstOrDefault(o => o.Id == order.Id);
                    if (existingOrder is null)
                    {
                        order.Items = new List<OrderItem>();
                        userEntry.Orders.Add(order);
                        existingOrder = order;
                    }

                    if (item is not null)
                    {
                        var existingItem = existingOrder.Items.FirstOrDefault(i => i.Id == item.Id);
                        if (existingItem is null)
                        {
                            item.Product = product;
                            existingOrder.Items.Add(item);
                        }
                    }
                }

                return userEntry;
            },
            new { minAge = 30 },
            splitOn: "id,id,id,id"
        ).Distinct();

        return userDict.Values.ToList();
    }

    /// <summary>
    /// EF Core: Traditional Include/ThenInclude (single query with cartesian explosion)
    /// </summary>
    [Benchmark]
    public List<UserOrder> EfCore_TraditionalInclude()
    {
        return _db.UserOrders
            .AsNoTracking()
            .Where(u => u.Age > 30)
            .Include(u => u.Orders)
            .ThenInclude(o => o.Items)
            .ThenInclude(oi => oi.Product)
            .ToList();
    }

    /// <summary>
    /// EF Core: AsSplitQuery (multiple separate queries instead of one big join)
    /// This avoids cartesian explosion by running separate queries for related entities
    /// </summary>
    [Benchmark]
    public List<UserOrder> EfCore_AsSplitQuery()
    {
        return _db.UserOrders
            .AsNoTracking()
            .Where(u => u.Age > 30)
            .Include(u => u.Orders)
            .ThenInclude(o => o.Items)
            .ThenInclude(oi => oi.Product)
            .AsSplitQuery()
            .ToList();
    }

    /// <summary>
    /// EF Core: Only fetch users with their orders (no order items/products)
    /// </summary>
    [Benchmark]
    public List<UserOrder> EfCore_TwoLevels()
    {
        return _db.UserOrders
            .AsNoTracking()
            .Where(u => u.Age > 30)
            .Include(u => u.Orders)
            .ToList();
    }

    /// <summary>
    /// EF Core: Only fetch users (no navigation properties)
    /// </summary>
    [Benchmark]
    public List<UserOrder> EfCore_NoIncludes()
    {
        return _db.UserOrders
            .AsNoTracking()
            .Where(u => u.Age > 30)
            .ToList();
    }
}

