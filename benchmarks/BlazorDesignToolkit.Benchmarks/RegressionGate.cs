using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

/// <summary>
/// Compares a benchmark run with a committed baseline (<c>benchmarks/baseline.json</c>). Allocations are deterministic
/// across machines, so they are gated tightly; time depends on the machine (CI runners are slower than developer machines),
/// so it is gated with a generous factor that still catches algorithmic regressions (for example quadratic behavior).
/// <para>Usage: <c>-- --gate benchmarks/baseline.json</c> (exit code 1 on regression) or <c>-- --update-baseline benchmarks/baseline.json</c>.</para>
/// </summary>
public static class RegressionGate
{
    public static int Run(string mode, string path)
    {
        var summaries = BenchmarkRunner.Run(typeof(RegressionGate).Assembly);
        var current = Measure(summaries);
        if (mode == "--update-baseline")
        {
            var json = new JsonObject
            {
                ["$comment"] = "Benchmark baseline for the CI regression gate. Regenerate with: dotnet run -c Release --project benchmarks/BlazorDesignToolkit.Benchmarks -- --update-baseline benchmarks/baseline.json",
                ["allocationTolerance"] = 0.10,
                ["timeFactor"] = 4.0,
                ["benchmarks"] = new JsonObject(current.OrderBy(static c => c.Key, StringComparer.Ordinal).Select(c =>
                    KeyValuePair.Create(c.Key, (JsonNode?)new JsonObject { ["allocatedBytes"] = c.Value.Allocated, ["meanNanoseconds"] = Math.Round(c.Value.MeanNs) }))),
            };
            File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");
            Console.WriteLine($"Baseline written to {path} ({current.Count} benchmarks).");
            return 0;
        }

        var baseline = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var allocationTolerance = baseline["allocationTolerance"]!.GetValue<double>();
        var timeFactor = baseline["timeFactor"]!.GetValue<double>();
        var failures = new List<string>();
        Console.WriteLine($"{"Benchmark",-90} {"Alloc (KB)",12} {"Base (KB)",12} {"Mean (µs)",12} {"Base (µs)",12}");
        foreach (var (name, expected) in baseline["benchmarks"]!.AsObject())
        {
            if (!current.TryGetValue(name, out var actual))
            {
                failures.Add($"{name}: missing from this run (renamed or removed? update the baseline).");
                continue;
            }
            var baseAlloc = expected!["allocatedBytes"]!.GetValue<long>();
            var baseMean = expected["meanNanoseconds"]!.GetValue<double>();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{name,-90} {actual.Allocated / 1024.0,12:0.0} {baseAlloc / 1024.0,12:0.0} {actual.MeanNs / 1000,12:0.0} {baseMean / 1000,12:0.0}"));
            if (actual.Allocated > baseAlloc * (1 + allocationTolerance) + 1024)
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: allocates {actual.Allocated:N0} B, baseline {baseAlloc:N0} B (+{allocationTolerance:P0} allowed)."));
            if (actual.MeanNs > baseMean * timeFactor)
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: takes {actual.MeanNs / 1000:0.0} µs, baseline {baseMean / 1000:0.0} µs (×{timeFactor} allowed)."));
        }
        foreach (var name in current.Keys.Where(k => baseline["benchmarks"]![k] is null))
            Console.WriteLine($"New benchmark without a baseline (not gated): {name}");
        if (failures.Count == 0)
        {
            Console.WriteLine("Benchmark gate PASSED.");
            return 0;
        }
        Console.WriteLine("Benchmark gate FAILED:");
        foreach (var f in failures) Console.WriteLine("  " + f);
        return 1;
    }

    private static Dictionary<string, (long Allocated, double MeanNs)> Measure(IEnumerable<Summary> summaries)
    {
        var result = new Dictionary<string, (long, double)>(StringComparer.Ordinal);
        foreach (var summary in summaries)
        {
            foreach (var report in summary.Reports)
            {
                if (report.ResultStatistics is null) continue;
                var name = report.BenchmarkCase.Descriptor.Type.Name + "." + report.BenchmarkCase.Descriptor.WorkloadMethod.Name
                    + (report.BenchmarkCase.HasParameters ? "(" + report.BenchmarkCase.Parameters.PrintInfo + ")" : "");
                result[name] = (report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0, report.ResultStatistics.Mean);
            }
        }
        return result;
    }
}
