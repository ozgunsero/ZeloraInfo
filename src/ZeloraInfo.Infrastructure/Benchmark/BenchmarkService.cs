// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.Benchmark;

namespace ZeloraInfo.Infrastructure.Benchmark;

/// <summary>
/// CrystalDiskMark-style benchmark engine.
/// <list type="bullet">
/// <item>Unbuffered + write-through overlapped I/O (FILE_FLAG_NO_BUFFERING) so the OS cache does not inflate results.</item>
/// <item>Queue depth × threads concurrent outstanding requests through the I/O completion port.</item>
/// <item>Each test is measured <c>Iterations</c> times for <c>DurationPerPass</c>; the best pass is reported.</item>
/// <item>The temporary file is always removed on completion, cancellation or failure.</item>
/// </list>
/// </summary>
public sealed class BenchmarkService : IBenchmarkService
{
    public const string WorkFolderName = "ZeloraInfoBench";
    public const string FilePrefix = "zelora_bench_";
    private const FileOptions NoBuffering = (FileOptions)0x20000000;
    private const int PrepareBlock = 8 * 1024 * 1024;
    private const double PreparationWeight = 5.0;

    public static string GetWorkFolder(string targetDirectory) => Path.Combine(targetDirectory, WorkFolderName);

    public async Task<BenchmarkReport> RunAsync(BenchmarkOptions options, IProgress<BenchmarkProgress>? progress,
        Action<BenchmarkResult>? onResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Tests.Count == 0) throw new ArgumentException("No tests configured.", nameof(options));

        var maxBlock = options.Tests.Max(t => t.BlockSizeBytes);
        var align = Math.Max(PrepareBlock, maxBlock);
        var fileSize = Math.Max(align, options.TestFileSizeBytes / align * align);

        var workFolder = GetWorkFolder(options.TargetDirectory);
        Directory.CreateDirectory(workFolder);
        EnsureFreeSpace(workFolder, fileSize);

        var filePath = Path.Combine(workFolder, $"{FilePrefix}{Guid.NewGuid():N}.tmp");
        var report = new BenchmarkReport
        {
            ApplicationVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "",
            TargetDirectory = options.TargetDirectory,
            OperatingSystem = RuntimeInformation.OSDescription,
            TestFileSizeBytes = fileSize,
            Iterations = options.Iterations,
            DurationPerPassSeconds = options.DurationPerPass.TotalSeconds,
            Mode = options.Mode,
        };

        var plan = BuildPlan(options);
        var completedPasses = 0;
        var totalPasses = plan.Count * options.Iterations;

        try
        {
            using (var handle = File.OpenHandle(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                       FileOptions.Asynchronous | FileOptions.WriteThrough | NoBuffering))
            {
                await PrepareFileAsync(handle, fileSize, progress, cancellationToken).ConfigureAwait(false);

                for (var p = 0; p < plan.Count; p++)
                {
                    var (testIndex, test, isRead) = plan[p];
                    if (p > 0 && options.IntervalBetweenTests > TimeSpan.Zero)
                    {
                        progress?.Report(new BenchmarkProgress(BenchmarkPhase.Interval, Overall(completedPasses, totalPasses, 0), testIndex, isRead, 0, null));
                        await Task.Delay(options.IntervalBetweenTests, cancellationToken).ConfigureAwait(false);
                    }

                    PassResult? best = null;
                    var aggregateLatencyMax = 0d;
                    for (var it = 1; it <= options.Iterations; it++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var passIndex = completedPasses;
                        var phase = isRead ? BenchmarkPhase.Reading : BenchmarkPhase.Writing;
                        var iteration = it;
                        var pass = await RunPassAsync(handle, test, isRead, fileSize, options.DurationPerPass, cancellationToken,
                            (fraction, mbps) => progress?.Report(new BenchmarkProgress(phase, Overall(passIndex, totalPasses, fraction), testIndex, isRead, iteration, mbps)))
                            .ConfigureAwait(false);
                        completedPasses++;
                        aggregateLatencyMax = Math.Max(aggregateLatencyMax, pass.MaxLatencyUs);
                        if (best is null || pass.MBps > best.MBps) best = pass;
                        progress?.Report(new BenchmarkProgress(phase, Overall(completedPasses, totalPasses, 0), testIndex, isRead, it, best.MBps));
                    }

                    var result = new BenchmarkResult
                    {
                        Test = test.Clone(),
                        IsRead = isRead,
                        MBps = best!.MBps,
                        Iops = best.Iops,
                        AverageLatencyMicroseconds = best.AvgLatencyUs,
                        MaxLatencyMicroseconds = aggregateLatencyMax,
                        TotalBytes = best.Bytes,
                        TotalOperations = best.Operations,
                        IterationsCompleted = options.Iterations,
                    };
                    report.Results.Add(result);
                    onResult?.Invoke(result);
                }
            }

            progress?.Report(new BenchmarkProgress(BenchmarkPhase.Completed, 100, -1, false, 0, null));
            return report;
        }
        finally
        {
            // Always clean up on cancel/failure; on success honour the user setting (default: delete).
            var succeeded = report.Results.Count == plan.Count;
            if (!succeeded || options.DeleteTestFileAfterRun)
            {
                progress?.Report(new BenchmarkProgress(BenchmarkPhase.CleaningUp, Overall(completedPasses, totalPasses, 0), -1, false, 0, null));
                TryDelete(filePath);
                TryDeleteEmptyFolder(workFolder);
            }
        }
    }

    public int CleanupOrphanedTestFiles(IEnumerable<string> roots)
    {
        var deleted = 0;
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var folder = GetWorkFolder(root);
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, FilePrefix + "*.tmp"))
                {
                    if (TryDelete(file)) deleted++;
                }
                TryDeleteEmptyFolder(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Inaccessible root: ignore.
            }
        }
        return deleted;
    }

    // ------------------------------------------------------------------

    private static List<(int TestIndex, BenchmarkTestDefinition Test, bool IsRead)> BuildPlan(BenchmarkOptions options)
    {
        var plan = new List<(int, BenchmarkTestDefinition, bool)>();
        if (options.Mode != BenchmarkMode.WriteOnly)
            for (var i = 0; i < options.Tests.Count; i++) plan.Add((i, options.Tests[i], true));
        if (options.Mode != BenchmarkMode.ReadOnly)
            for (var i = 0; i < options.Tests.Count; i++) plan.Add((i, options.Tests[i], false));
        return plan;
    }

    private static double Overall(int completedPasses, int totalPasses, double fraction)
        => PreparationWeight + (100 - PreparationWeight) * Math.Clamp((completedPasses + fraction) / Math.Max(1, totalPasses), 0, 1);

    private static void EnsureFreeSpace(string folder, long fileSize)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(folder));
        if (string.IsNullOrEmpty(root)) return;
        try
        {
            var drive = new DriveInfo(root);
            var required = fileSize + 64L * 1024 * 1024;
            if (drive.IsReady && drive.AvailableFreeSpace < required)
                throw new InsufficientSpaceException(required, drive.AvailableFreeSpace);
        }
        catch (ArgumentException)
        {
            // UNC path etc.: let the write fail naturally if there is not enough space.
        }
    }

    private static async Task PrepareFileAsync(SafeFileHandle handle, long fileSize, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        RandomAccess.SetLength(handle, fileSize);
        using var buffer = new AlignedBuffer(PrepareBlock);
        buffer.FillRandom(); // incompressible data so compressing controllers cannot cheat
        var memory = buffer.Memory;
        long written = 0;
        var lastReport = Stopwatch.StartNew();
        while (written < fileSize)
        {
            ct.ThrowIfCancellationRequested();
            await RandomAccess.WriteAsync(handle, (ReadOnlyMemory<byte>)memory, written, ct).ConfigureAwait(false);
            written += PrepareBlock;
            if (lastReport.ElapsedMilliseconds > 200)
            {
                lastReport.Restart();
                progress?.Report(new BenchmarkProgress(BenchmarkPhase.Preparing, PreparationWeight * written / fileSize, -1, false, 0, null));
            }
        }
        progress?.Report(new BenchmarkProgress(BenchmarkPhase.Preparing, PreparationWeight, -1, false, 0, null));
    }

    private sealed record PassResult(double MBps, double Iops, double AvgLatencyUs, double MaxLatencyUs, long Bytes, long Operations);

    private static async Task<PassResult> RunPassAsync(SafeFileHandle handle, BenchmarkTestDefinition test, bool isRead, long fileSize,
        TimeSpan duration, CancellationToken ct, Action<double, double> live)
    {
        var blockSize = test.BlockSizeBytes;
        var blocks = fileSize / blockSize;
        var workers = test.QueueDepth * test.Threads;
        long sequence = -1, bytes = 0, ops = 0, latencyTicks = 0, maxLatencyTicks = 0;
        var stopwatch = Stopwatch.StartNew();
        var durationTicks = (long)(duration.TotalSeconds * Stopwatch.Frequency);
        var startTs = Stopwatch.GetTimestamp();
        var sequential = test.Access == BenchmarkAccess.Sequential;

        async Task Worker()
        {
            using var buffer = new AlignedBuffer(blockSize);
            if (!isRead) buffer.FillRandom();
            var memory = buffer.Memory;
            var random = new Random(Environment.TickCount ^ Environment.CurrentManagedThreadId ^ Guid.NewGuid().GetHashCode());
            while (Stopwatch.GetTimestamp() - startTs < durationTicks)
            {
                ct.ThrowIfCancellationRequested();
                var block = sequential ? Interlocked.Increment(ref sequence) % blocks : random.NextInt64(blocks);
                var offset = block * blockSize;
                var t0 = Stopwatch.GetTimestamp();
                if (isRead)
                    await RandomAccess.ReadAsync(handle, memory, offset, ct).ConfigureAwait(false);
                else
                    await RandomAccess.WriteAsync(handle, (ReadOnlyMemory<byte>)memory, offset, ct).ConfigureAwait(false);
                var elapsed = Stopwatch.GetTimestamp() - t0;
                Interlocked.Add(ref bytes, blockSize);
                Interlocked.Increment(ref ops);
                Interlocked.Add(ref latencyTicks, elapsed);
                long current;
                while (elapsed > (current = Interlocked.Read(ref maxLatencyTicks)) &&
                       Interlocked.CompareExchange(ref maxLatencyTicks, elapsed, current) != current)
                {
                }
            }
        }

        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(Worker, ct)).ToArray();
        var all = Task.WhenAll(tasks);
        while (!all.IsCompleted)
        {
            await Task.WhenAny(all, Task.Delay(250, CancellationToken.None)).ConfigureAwait(false);
            var seconds = stopwatch.Elapsed.TotalSeconds;
            if (seconds > 0)
                live(Math.Min(1, seconds / duration.TotalSeconds), Interlocked.Read(ref bytes) / 1_000_000d / seconds);
        }
        await all.ConfigureAwait(false); // propagate cancellation / I/O errors
        stopwatch.Stop();

        var totalSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 1e-6);
        var tickToUs = 1_000_000d / Stopwatch.Frequency;
        return new PassResult(
            MBps: bytes / 1_000_000d / totalSeconds,
            Iops: ops / totalSeconds,
            AvgLatencyUs: ops == 0 ? 0 : latencyTicks * tickToUs / ops,
            MaxLatencyUs: maxLatencyTicks * tickToUs,
            Bytes: bytes,
            Operations: ops);
    }

    private static bool TryDelete(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
        return false;
    }

    private static void TryDeleteEmptyFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public sealed class InsufficientSpaceException(long required, long available)
    : IOException($"Insufficient free space: {required} bytes required, {available} available.")
{
    public long Required { get; } = required;
    public long Available { get; } = available;
}
