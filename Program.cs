using System.Globalization;

// Usage:
//   dotnet run -c Release                    -> BenchmarkDotNet (time + GC + allocations)
//   dotnet run -c Release -- peak            -> peak retained-memory comparison (default 5000 x 40)
//   dotnet run -c Release -- peak 20000 40   -> peak comparison with custom rows/date and dates
// Note: the peak sampler forces a full GC per sample, so very large datasets take longer to run.
if (args.Length > 0 && string.Equals(args[0], "peak", StringComparison.OrdinalIgnoreCase))
{
    int rowsPerReportingDate = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5_000;
    int reportingDates = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 40;
    PeakMemoryRunner.Run(rowsPerReportingDate, reportingDates);
    return;
}

//BenchmarkRunner.Run<DapperVsEfPgBenchmarks>();
//BenchmarkRunner.Run<StringConcatBenchmarks>();
//BenchmarkRunner.Run<StringSpanBenchmarks>();
//BenchmarkRunner.Run<ComplexDapperVsEfBenchmarks>();
BenchmarkRunner.Run<StreamingVsMaterializeBenchmarks>();
