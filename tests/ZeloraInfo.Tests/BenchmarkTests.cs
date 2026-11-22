// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Text.Json;
using ZeloraInfo.Core.Benchmark;
using ZeloraInfo.Infrastructure.Benchmark;

namespace ZeloraInfo.Tests;

public sealed class BenchmarkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZeloraInfoTests", "bench-" + Guid.NewGuid().ToString("N"));

    public BenchmarkTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void DefaultProfileMatchesCrystalDiskMarkLabels()
    {
        var labels = BenchmarkTestDefinition.DefaultProfile().Select(t => t.Label).ToArray();
        Assert.Equal(["SEQ1M Q8T1", "SEQ1M Q1T1", "RND4K Q32T1", "RND4K Q1T1"], labels);
    }

    [Fact]
    public void NormalizeFixesInvalidDefinitions()
    {
        var t = new BenchmarkTestDefinition { Access = BenchmarkAccess.Random, BlockSizeKiB = 3, QueueDepth = 7, Threads = 99 };
        t.Normalize();
        Assert.Equal("RND4K Q1T1", t.Label);
    }

    [Fact]
    public async Task RunsEndToEndAndRemovesTheTestFile()
    {
        var service = new BenchmarkService();
        var progress = new List<BenchmarkProgress>();
        var results = new List<BenchmarkResult>();
        var options = new BenchmarkOptions
        {
            TargetDirectory = _dir,
            TestFileSizeBytes = 16L * 1024 * 1024,
            Iterations = 1,
            DurationPerPass = TimeSpan.FromMilliseconds(400),
            IntervalBetweenTests = TimeSpan.Zero,
            Tests =
            [
                new() { Access = BenchmarkAccess.Sequential, BlockSizeKiB = 1024, QueueDepth = 2, Threads = 1 },
                new() { Access = BenchmarkAccess.Random, BlockSizeKiB = 4, QueueDepth = 4, Threads = 2 },
            ],
        };

        var report = await service.RunAsync(options, new SyncProgress(progress), results.Add, CancellationToken.None);

        Assert.Equal(4, report.Results.Count); // 2 tests × read + write
        Assert.Equal(4, results.Count);
        Assert.All(report.Results, r =>
        {
            Assert.True(r.MBps > 0, $"{r.Test.Label} {(r.IsRead ? "R" : "W")} has no throughput");
            Assert.True(r.Iops > 0);
            Assert.True(r.AverageLatencyMicroseconds > 0);
        });
        Assert.Contains(progress, p => p.Phase == BenchmarkPhase.Completed);
        Assert.False(Directory.Exists(BenchmarkService.GetWorkFolder(_dir)), "Work folder must be removed after the run");
    }

    [Fact]
    public async Task CancellationStopsAndCleansUp()
    {
        var service = new BenchmarkService();
        using var cts = new CancellationTokenSource();
        var options = new BenchmarkOptions
        {
            TargetDirectory = _dir,
            TestFileSizeBytes = 16L * 1024 * 1024,
            Iterations = 3,
            DurationPerPass = TimeSpan.FromSeconds(10),
            Tests = BenchmarkTestDefinition.DefaultProfile(),
        };
        var progress = new SyncProgress([], p =>
        {
            if (p.Phase is BenchmarkPhase.Reading) cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(options, progress, null, cts.Token));
        Assert.False(Directory.Exists(BenchmarkService.GetWorkFolder(_dir)));
    }

    [Fact]
    public void CleanupRemovesOrphanedFilesOnly()
    {
        var work = BenchmarkService.GetWorkFolder(_dir);
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, BenchmarkService.FilePrefix + "old.tmp"), "x");
        File.WriteAllText(Path.Combine(work, "keep.txt"), "user file");

        var removed = new BenchmarkService().CleanupOrphanedTestFiles([_dir]);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(Path.Combine(work, "keep.txt")));
    }

    [Fact]
    public void ReportFormattersProduceTxtCsvAndJson()
    {
        var report = new BenchmarkReport
        {
            ApplicationVersion = "1.0.0",
            TargetDirectory = @"D:\",
            OperatingSystem = "Windows",
            TestFileSizeBytes = 1L << 30,
            Iterations = 5,
            DurationPerPassSeconds = 5,
            Results =
            [
                new BenchmarkResult { Test = BenchmarkTestDefinition.DefaultProfile()[0], IsRead = true, MBps = 7012.345, Iops = 6687.5, AverageLatencyMicroseconds = 1195.2 },
                new BenchmarkResult { Test = BenchmarkTestDefinition.DefaultProfile()[0], IsRead = false, MBps = 5100.1, Iops = 4863.8, AverageLatencyMicroseconds = 1640.0 },
            ],
        };

        var txt = BenchmarkReportFormatter.ToText(report);
        Assert.Contains("SEQ1M Q8T1", txt);
        Assert.Contains("7012.35 MB/s", txt);
        Assert.Contains("Şero Teknoloji", txt);

        var csv = BenchmarkReportFormatter.ToCsv(report).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, csv.Length);
        Assert.StartsWith("SEQ1M Q8T1,Read,1024,8,1,7012.345", csv[1]);

        using var json = JsonDocument.Parse(BenchmarkReportFormatter.ToJson(report));
        Assert.Equal(2, json.RootElement.GetProperty("results").GetArrayLength());
        Assert.Equal("ReadWrite", json.RootElement.GetProperty("mode").GetString());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>IProgress that reports synchronously (Progress&lt;T&gt; would post to the thread pool).</summary>
    private sealed class SyncProgress(List<BenchmarkProgress> sink, Action<BenchmarkProgress>? onReport = null) : IProgress<BenchmarkProgress>
    {
        public void Report(BenchmarkProgress value)
        {
            lock (sink) sink.Add(value);
            onReport?.Invoke(value);
        }
    }
}
