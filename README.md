# BenchmarkingDemo

A comprehensive benchmarking project demonstrating performance comparisons across different data access patterns and string manipulation techniques using **BenchmarkDotNet**.

## 📋 Project Overview

This project includes multiple benchmarking scenarios:

1. **String Concatenation Benchmarks** - Comparing different string concatenation approaches
2. **String Span Benchmarks** - Demonstrating the performance benefits of using `Span<T>` and `string.Create`
3. **Dapper vs EF Core Benchmarks** - Simple scenario with a single users table
4. **Complex Dapper vs EF Core Benchmarks** - Advanced scenario with multiple tables, joins, and `AsSplitQuery`

## 🚀 Quick Start

### Prerequisites

- .NET 10.0 or higher
- Docker & Docker Compose (for PostgreSQL database)

### Setup

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd BenchmarkingDemo
   ```

2. **Start PostgreSQL with Docker Compose**
   ```bash
   docker-compose up -d
   ```

   This will:
   - Start a PostgreSQL 16 container
   - Create a `benchmarks` database
   - Automatically initialize the schema from `schema_complex.sql`
   - Expose PostgreSQL on `localhost:5432`

3. **Verify PostgreSQL is ready**
   ```bash
   docker-compose ps
   ```

4. **Build the project**
   ```bash
   dotnet build -c Release
   ```

5. **Run benchmarks**
   ```bash
   dotnet run -c Release
   ```

## 📊 Benchmark Scenarios

### 1. String Concatenation Benchmarks (`StringConcatBenchmarks.cs`)

Compares 4 approaches to concatenating 100 strings:

| Method | Mean | StdDev | Ratio | Allocated | Alloc Ratio |
|--------|------|--------|-------|-----------|------------|
| **PlusOperator** | 2,831.4 ns | 15.77 ns | 1.00 | 59.81 KB | 1.00 |
| **StringBuilderConcat** | 376.5 ns | 1.72 ns | 0.13 | 3.67 KB | 0.06 |
| **StringJoin** | 251.4 ns | 0.54 ns | 0.09 | 1.18 KB | 0.02 |
| **LinqAggregate** | 2,833.4 ns | 17.99 ns | 1.00 | 59.81 KB | 1.00 |

**Key Learning**: `StringBuilder` is **11.3x faster** than `+=` operator. `StringJoin()` is even faster at **0.09x** of the baseline.

### 2. String Span Benchmarks (`StringSpanBenchmarks.cs`)

Compares 4 approaches including a modern span-based approach:

| Method | Mean | StdDev | Ratio | Allocated | Alloc Ratio |
|--------|------|--------|-------|-----------|------------|
| **PlusOperator** | 2,836.8 ns | 25.04 ns | 1.00 | 59.81 KB | 1.00 |
| **StringBuilderConcat** | 389.3 ns | 5.35 ns | 0.14 | 3.67 KB | 0.06 |
| **StringJoin** | 251.4 ns | 1.56 ns | 0.09 | 1.18 KB | 0.02 |
| **StringCreateWithSpan** | 226.1 ns | 2.89 ns | 0.08 | 1.18 KB | 0.02 |

**Key Learning**: `string.Create()` with span is **0.08x** the baseline—12.5x faster than `+=`. It achieves the same allocation as `StringJoin` with slightly lower execution time.

### 3. Simple Dapper vs EF Core Benchmarks (`DapperVsEfPgBenchmarks.cs`)

Compares data access patterns with a single users table (10,000 rows):

| Method | Mean | StdDev | Allocated |
|--------|------|--------|-----------|
| **Dapper** | 300.7 μs | 5.94 μs | 17.78 KB |
| **EfCore_NoTracking** | 264.7 μs | 6.81 μs | 40.3 KB |
| **EfCore_Compiled** | 266.0 μs | 6.80 μs | 36.35 KB |

**Key Learning**: EF Core compiled queries match Dapper performance on simple queries. While Dapper uses slightly less memory, the difference is negligible for most applications.

### 4. Complex Dapper vs EF Core Benchmarks (`ComplexDapperVsEfBenchmarks.cs`)

Advanced scenario with 4 related tables and deep navigation properties:

**Schema:**
- `users` (10,000 rows) → `orders` (50,000 rows) → `order_items` (150,000 rows) → `products` (500 rows)

| Method | Mean | StdDev | Ratio | Gen0 | Gen1 | Gen2 | Allocated | Alloc Ratio |
|--------|------|--------|-------|------|------|------|-----------|------------|
| **Dapper_MultipleJoins** | 156.224 ms | 3.44 ms | 1.003 | 9000 | 3000 | 1000 | 77.93 MB | 1.00 |
| **EfCore_TraditionalInclude** | 358.649 ms | 6.76 ms | 2.304 | 19000 | 5000 | 1000 | 149.65 MB | 1.92 |
| **EfCore_AsSplitQuery** | 291.557 ms | 2.38 ms | 1.873 | 22000 | 8000 | 3000 | 177.68 MB | 2.28 |
| **EfCore_TwoLevels** | 42.435 ms | 0.18 ms | 0.273 | 2833 | 833 | 250 | 20.78 MB | 0.27 |
| **EfCore_NoIncludes** | 1.478 ms | 0.02 ms | 0.009 | 238 | 76 | - | 1.91 MB | 0.02 |

**Key Insights:**
- 🏆 **Dapper_MultipleJoins** is the fastest baseline (156 ms, 77.93 MB)
- ⭐ **EfCore_AsSplitQuery** is **1.87x slower** than Dapper but significantly better than TraditionalInclude
- ⚠️ **EfCore_TraditionalInclude** suffers from cartesian explosion: **2.3x slower** than Dapper, allocates **149.65 MB**
- 📊 **Memory allocation is critical**: TraditionalInclude allocates **1.92x** more memory than Dapper
- 🎯 **AsSplitQuery provides a good balance** between convenience and performance, using separate queries to avoid data duplication

**Recommendation**: Use `AsSplitQuery()` when loading multiple levels of navigation properties with EF Core to avoid cartesian explosion overhead.

### 5. Streaming vs Materialize Benchmarks (`StreamingVsMaterializeBenchmarks.cs`)

Builds a per-**reporting-date** summary over the `orders` table (50,000 rows spread across 40 quarter-end reporting dates) and compares two ways for a service to hand data to its consumer:

- **Materialize (`ToListAsync`)** — the service returns `Task<List<Order>>`. Every row is loaded into one big list before aggregation starts, so peak live memory is the entire result set.
- **Streaming (`AsAsyncEnumerable`)** — the service returns `IAsyncEnumerable<Order>` and the consumer walks it with `await foreach`. The grouping key is the quarter-end reporting date (four per year), which is a monotonic function of the timestamp — so `ORDER BY order_date` guarantees all rows for a reporting date arrive together: when the reporting date changes, the previous period is complete — aggregate the small buffer, clear it, move on.

| Method | Mean | StdDev | Ratio | Gen0 | Gen1 | Gen2 | Allocated | Alloc Ratio |
|--------|------|--------|-------|------|------|------|-----------|------------|
| **Materialize_ToListAsync** | 33.91 ms | 0.59 ms | 1.00 | 2583 | 1083 | 417 | 18.3 MB | 1.00 |
| **Streaming_AsAsyncEnumerable** | 22.01 ms | 0.30 ms | 0.65 | 1938 | 656 | **0** | 15.69 MB | 0.86 |

**Key Learning**: total *allocated* memory is similar for both (every row is touched once either way). The win is in **object lifetime** — the streaming version only ever holds one reporting date's rows, so streamed entities die in Gen0 and never reach Gen2 (417 Gen2 collections → **zero**). Avoiding the giant list and the `GroupBy` dictionary also makes it **35% faster**. This is the pattern that turns an out-of-memory crash on large result sets into a flat, small memory profile, no matter how many rows the query returns.

```csharp
// OLD: "here is the whole basket, already filled"
Task<List<Order>> GetOrdersMaterializedAsync() => query.ToListAsync();

// NEW: "here is a belt; ask me for the next row when you're ready"
IAsyncEnumerable<Order> GetOrdersStreamAsync() => query.AsAsyncEnumerable();
```

#### Peak retained-memory runner (`PeakMemoryRunner.cs`)

`MemoryDiagnoser` reports *total allocated*, which is near-identical for both strategies. The number that actually differs — **peak retained (live) heap** — needs a different measurement: a background sampler polls `GC.GetTotalMemory(forceFullCollection: true)` every few milliseconds during the workload and records the maximum. Forcing a full blocking collection per sample means dead rows the streaming path already released are excluded, so only truly *rooted* memory is counted. Data comes from an in-memory sorted generator so the row count can be scaled arbitrarily (no database needed).

```bash
dotnet run -c Release                    # -> BenchmarkDotNet (time + GC + allocations)
dotnet run -c Release -- peak            # -> peak retained-memory comparison (default 5000 x 40)
dotnet run -c Release -- peak 20000 40   # -> peak comparison with custom rows/reporting date and dates
```

Measured results:

| Total rows | Materialize (old) | Streaming (new) |
|-----------|------------------:|----------------:|
| 200,000 | +47.7 MB | +1.0 MB |
| 800,000 | +170.1 MB | +3.9 MB |

**Key Learning**: the materialised list roots every row until the end, so peak retained memory grows **linearly with total rows** (4x rows → ~3.5x memory). The streaming path's peak only grows with the *largest single group* (one reporting date), so it stays flat no matter how long the history gets — this is the difference between an eventual out-of-memory crash and a bounded memory profile.

## 🗄️ Database Schema

The `schema_complex.sql` initializes the following structure:

```sql
users (id, name, email, age, created_at)
  ├── orders (id, user_id, order_date, total_amount, status)
  │   └── order_items (id, order_id, product_id, quantity, unit_price)
  │       └── products (id, name, price, category, stock)
```

**Data Population:**
- 10,000 users (IDs: 1-10,000)
- 500 products
- 50,000 orders
- 150,000 order items

## 🐳 Docker Compose Commands

### Start PostgreSQL
```bash
docker-compose up -d
```

### Check status
```bash
docker-compose ps
```

### View logs
```bash
docker-compose logs -f postgres
```

### Stop PostgreSQL
```bash
docker-compose stop
```

### Remove containers and volumes (clean slate)
```bash
docker-compose down -v
```

### Restart PostgreSQL
```bash
docker-compose restart
```

## 🔧 Connection String

The default connection string is configured for the Docker container:

```
Host=localhost;Port=5432;Database=benchmarks;Username=postgres;Password=postgres
```

To use a different database, update the `ConnectionString` in the benchmark class.

## 📈 Understanding the Results

BenchmarkDotNet generates detailed reports including:

- **Mean**: Average execution time
- **StdDev**: Standard deviation (consistency)
- **Gen0/Gen1/Gen2**: Garbage collection collections
- **Allocated**: Memory allocated per operation

Reports are saved in:
```
BenchmarkDotNet.Artifacts/results/
```

## 💡 Key Takeaways

1. **String Building**: Use `StringBuilder` or `string.Create()` for multiple concatenations
2. **Span Usage**: `Span<T>` and `string.Create()` eliminate intermediate allocations
3. **Data Access**: 
   - Dapper is fastest for simple queries
   - EF Core compiled queries are competitive
   - `AsSplitQuery()` is crucial for queries with multiple levels of navigation
4. **Memory**: Memory allocations often correlate more with perceived performance than raw execution time

## 🎯 Running Specific Benchmarks

To run a specific benchmark class:

```bash
dotnet run -c Release --filter BenchmarkingDemo.StringConcatBenchmarks
```

Or for complex scenario:

```bash
dotnet run -c Release --filter BenchmarkingDemo.ComplexDapperVsEfBenchmarks
```

## 📦 Dependencies

- **BenchmarkDotNet**: Performance benchmarking framework
- **Dapper**: Lightweight ORM
- **EntityFrameworkCore**: Microsoft's ORM
- **Npgsql**: PostgreSQL data provider

## 🛠️ Development

To add a new benchmark:

1. Create a new class inheriting from benchmark class with `[MemoryDiagnoser]` attribute
2. Mark methods with `[Benchmark]` attribute
3. Use `[GlobalSetup]` for initialization
4. Add the class to `Program.cs` or run it directly

Example:
```csharp
[MemoryDiagnoser]
public class MyBenchmark
{
    [GlobalSetup]
    public void Setup() { }

    [Benchmark(Baseline = true)]
    public void Method1() { }

    [Benchmark]
    public void Method2() { }
}
```

## 📝 Notes

- Always run benchmarks in **Release** mode for accurate results
- Ensure PostgreSQL is running before executing data access benchmarks
- The first run may take longer due to JIT compilation
- Multiple iterations ensure statistical significance

## 📚 Resources

- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- [Entity Framework Core Performance](https://docs.microsoft.com/en-us/ef/core/performance/)
- [Dapper GitHub](https://github.com/DapperLib/Dapper)

## 📄 License

MIT

