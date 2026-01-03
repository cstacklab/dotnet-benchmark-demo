using BenchmarkDotNet.Running;
using BenchmarkingDemo;

//BenchmarkRunner.Run<DapperVsEfPgBenchmarks>();
//BenchmarkRunner.Run<StringConcatBenchmarks>();
//BenchmarkRunner.Run<StringSpanBenchmarks>();
BenchmarkRunner.Run<ComplexDapperVsEfBenchmarks>();