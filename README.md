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

| Method | Description |
|--------|-------------|
| **PlusOperator** | Using `+=` operator (baseline) |
| **StringBuilderConcat** | Using `StringBuilder` |
| **StringJoin** | Using `string.Join()` |
| **LinqAggregate** | Using LINQ `Aggregate()` |

**Key Learning**: `StringBuilder` is significantly faster for multiple concatenations.

### 2. String Span Benchmarks (`StringSpanBenchmarks.cs`)

Compares 4 approaches including a modern span-based approach:

| Method | Description |
|--------|-------------|
| **PlusOperator** | Using `+=` operator (baseline) |
| **StringBuilderConcat** | Using `StringBuilder` |
| **StringJoin** | Using `string.Join()` |
| **StringCreateWithSpan** | Using `string.Create()` with `Span<char>` |

**Key Learning**: `string.Create()` with span avoids intermediate allocations, often providing the best performance.

### 3. Simple Dapper vs EF Core Benchmarks (`DapperVsEfPgBenchmarks.cs`)

Compares data access patterns with a single users table:

| Method | Description |
|--------|-------------|
| **Dapper** | Raw SQL query using Dapper |
| **EfCore_NoTracking** | EF Core with `AsNoTracking()` |
| **EfCore_Compiled** | EF Core with compiled queries |

**Key Learning**: Compiled queries in EF Core can match Dapper performance.

### 4. Complex Dapper vs EF Core Benchmarks (`ComplexDapperVsEfBenchmarks.cs`)

Advanced scenario with 4 related tables and deep navigation properties:

**Schema:**
- `users` (10,000 rows) → `orders` (50,000 rows) → `order_items` (150,000 rows) → `products` (500 rows)

| Method | Description |
|--------|-------------|
| **Dapper_MultipleJoins** | Raw SQL with LEFT JOINs (baseline) |
| **EfCore_TraditionalInclude** | Standard Include/ThenInclude |
| **EfCore_AsSplitQuery** | ⭐ **Recommended** - Uses separate queries to avoid cartesian explosion |
| **EfCore_TwoLevels** | Only users + orders (no items/products) |
| **EfCore_NoIncludes** | Just users (no navigation properties) |

**Key Learning**: `AsSplitQuery()` significantly reduces memory allocations and improves performance by running multiple targeted queries instead of one massive join.

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

