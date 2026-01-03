namespace BenchmarkingDemo;

[MemoryDiagnoser]
public class StringSpanBenchmarks
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
    public string StringCreateWithSpan()
    {
        // compute the total length once, then allocate the target string and fill via Span<char>
        int total = parts.Sum(p => p.Length);
        return string.Create(total, parts, (span, state) =>
        {
            int pos = 0;
            foreach (var p in state)
            {
                p.AsSpan().CopyTo(span.Slice(pos));
                pos += p.Length;
            }
        });
    }
}