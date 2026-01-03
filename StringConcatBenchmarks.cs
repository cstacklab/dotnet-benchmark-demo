namespace BenchmarkingDemo;

[MemoryDiagnoser]
public class StringConcatBenchmarks
{
    private string[] parts;

    [GlobalSetup]
    public void Setup()
    {
        parts = Enumerable.Range(0, 100).Select(i => "item" + i).ToArray();
    }

    [Benchmark(Baseline = true)]
    public string PlusOperator()
    {
        var s = "";
        for (int i = 0; i < parts.Length; i++)
        {
            s += parts[i];
        }
        return s;
    }

    [Benchmark]
    public string StringBuilderConcat()
    {
        var sb = new StringBuilder();
        foreach (var p in parts) sb.Append(p);
        return sb.ToString();
    }

    [Benchmark]
    public string StringJoin()
    {
        return string.Join("", parts);
    }

    [Benchmark]
    public string LinqAggregate()
    {
        return parts.Aggregate((a, b) => a + b);
    }
}