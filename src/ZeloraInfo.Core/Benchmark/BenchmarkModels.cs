// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

namespace ZeloraInfo.Core.Benchmark;

public enum BenchmarkAccess
{
    Sequential,
    Random
}

public enum BenchmarkMode
{
    ReadWrite,
    ReadOnly,
    WriteOnly
}

public enum BenchmarkPhase
{
    Idle,
    Preparing,
    Reading,
    Writing,
    Interval,
    CleaningUp,
    Completed,
    Cancelled,
    Failed
}

/// <summary>One row of the benchmark grid, e.g. "SEQ1M Q8T1".</summary>
public sealed class BenchmarkTestDefinition
{
    public static readonly int[] AllowedBlockSizesKiB = [4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192];
    public static readonly int[] AllowedQueueDepths = [1, 2, 4, 8, 16, 32, 64, 128];
    public static readonly int[] AllowedThreads = [1, 2, 4, 8, 16];

    public BenchmarkAccess Access { get; set; } = BenchmarkAccess.Sequential;
    public int BlockSizeKiB { get; set; } = 1024;
    public int QueueDepth { get; set; } = 8;
    public int Threads { get; set; } = 1;

    public int BlockSizeBytes => BlockSizeKiB * 1024;

    /// <summary>CrystalDiskMark-style label: SEQ1M Q8T1, RND4K Q32T1 …</summary>
    public string Label
    {
        get
        {
            var prefix = Access == BenchmarkAccess.Sequential ? "SEQ" : "RND";
            var size = BlockSizeKiB >= 1024 && BlockSizeKiB % 1024 == 0 ? $"{BlockSizeKiB / 1024}M" : $"{BlockSizeKiB}K";
            return $"{prefix}{size} Q{QueueDepth}T{Threads}";
        }
    }

    public BenchmarkTestDefinition Clone() => (BenchmarkTestDefinition)MemberwiseClone();

    public void Normalize()
    {
        if (!AllowedBlockSizesKiB.Contains(BlockSizeKiB)) BlockSizeKiB = Access == BenchmarkAccess.Sequential ? 1024 : 4;
        if (!AllowedQueueDepths.Contains(QueueDepth)) QueueDepth = 1;
        if (!AllowedThreads.Contains(Threads)) Threads = 1;
    }

    public static List<BenchmarkTestDefinition> DefaultProfile() =>
    [
        new() { Access = BenchmarkAccess.Sequential, BlockSizeKiB = 1024, QueueDepth = 8, Threads = 1 },
        new() { Access = BenchmarkAccess.Sequential, BlockSizeKiB = 1024, QueueDepth = 1, Threads = 1 },
        new() { Access = BenchmarkAccess.Random, BlockSizeKiB = 4, QueueDepth = 32, Threads = 1 },
        new() { Access = BenchmarkAccess.Random, BlockSizeKiB = 4, QueueDepth = 1, Threads = 1 },
    ];

    public override string ToString() => Label;
}

public sealed class BenchmarkOptions
{
    /// <summary>Directory (or drive root) where the temporary test file is created.</summary>
    public required string TargetDirectory { get; init; }
    public long TestFileSizeBytes { get; init; } = 1L << 30;
    public int Iterations { get; init; } = 5;
    public TimeSpan DurationPerPass { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan IntervalBetweenTests { get; init; } = TimeSpan.FromSeconds(1);
    public BenchmarkMode Mode { get; init; } = BenchmarkMode.ReadWrite;
    public IReadOnlyList<BenchmarkTestDefinition> Tests { get; init; } = BenchmarkTestDefinition.DefaultProfile();
    public bool DeleteTestFileAfterRun { get; init; } = true;
}

public sealed record BenchmarkProgress(
    BenchmarkPhase Phase,
    double OverallPercent,
    int TestIndex,
    bool IsRead,
    int Iteration,
    double? CurrentMBps,
    string? Detail = null);

public sealed class BenchmarkResult
{
    public required BenchmarkTestDefinition Test { get; init; }
    public required bool IsRead { get; init; }
    /// <summary>Decimal megabytes per second (1 MB = 1,000,000 bytes), as CrystalDiskMark reports.</summary>
    public double MBps { get; init; }
    public double Iops { get; init; }
    public double AverageLatencyMicroseconds { get; init; }
    public double MaxLatencyMicroseconds { get; init; }
    public long TotalBytes { get; init; }
    public long TotalOperations { get; init; }
    public int IterationsCompleted { get; init; }
}

public sealed class BenchmarkReport
{
    public string Application { get; init; } = "Zelora Info";
    public string ApplicationVersion { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string TargetDirectory { get; init; } = "";
    public string? DriveDescription { get; init; }
    public string OperatingSystem { get; init; } = "";
    public long TestFileSizeBytes { get; init; }
    public int Iterations { get; init; }
    public double DurationPerPassSeconds { get; init; }
    public BenchmarkMode Mode { get; init; }
    public List<BenchmarkResult> Results { get; init; } = [];
}
