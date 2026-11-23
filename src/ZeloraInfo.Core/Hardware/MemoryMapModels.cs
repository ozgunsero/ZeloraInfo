// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

namespace ZeloraInfo.Core.Hardware;

/// <summary>Where the numbers of a <see cref="MemoryMapSnapshot"/> come from.</summary>
public enum MemoryListSource
{
    /// <summary>Performance counters (no elevation): standby is grouped into Reserve / Normal / Core.</summary>
    PerformanceCounters,
    /// <summary>NtQuerySystemInformation(SystemMemoryListInformation) (administrator): exact lists and 8 standby priorities.</summary>
    Kernel
}

/// <summary>Physical memory page lists (bytes).</summary>
public sealed class MemoryLists
{
    public ulong Zeroed { get; init; }
    public ulong Free { get; init; }
    /// <summary>Free + zeroed when the two cannot be told apart (performance counters).</summary>
    public ulong FreeAndZeroed => Zeroed + Free;
    public ulong Modified { get; init; }
    public ulong ModifiedNoWrite { get; init; }
    public ulong Bad { get; init; }
    public ulong Standby => StandbyByPriority.Aggregate(0UL, (sum, p) => sum + p.Bytes);
    public IReadOnlyList<StandbyPriority> StandbyByPriority { get; init; } = [];
}

/// <summary>Standby list for one priority (0–7) or one counter group ("0–1", "2–5", "6–7").</summary>
public sealed class StandbyPriority
{
    public required string Priority { get; init; }
    public ulong Bytes { get; init; }
    /// <summary>Pages repurposed from this priority since boot (kernel source only).</summary>
    public ulong? Repurposed { get; init; }
}

/// <summary>A memory consumer shown in the "active memory" breakdown.</summary>
public sealed class MemoryConsumer
{
    public required string Key { get; init; }
    public ulong Bytes { get; init; }
}

public sealed class MemoryProcessInfo
{
    public int ProcessId { get; init; }
    public int ParentProcessId { get; init; }
    public string Name { get; init; } = "";
    public int SessionId { get; init; }
    public ulong WorkingSet { get; init; }
    public ulong PrivateWorkingSet { get; init; }
    public ulong SharedWorkingSet => WorkingSet > PrivateWorkingSet ? WorkingSet - PrivateWorkingSet : 0;
    public ulong PeakWorkingSet { get; init; }
    /// <summary>Private commit (PagefileUsage).</summary>
    public ulong PrivateBytes { get; init; }
    public ulong PagedPool { get; init; }
    public ulong NonPagedPool { get; init; }
    public ulong VirtualSize { get; init; }
    public uint PageFaults { get; init; }
    public uint Threads { get; init; }
    public uint Handles { get; init; }
}

public sealed record PhysicalRange(ulong Start, ulong Length)
{
    public ulong End => Start + Length;
}

public sealed class MemoryMapSnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public MemoryListSource Source { get; init; }
    public ulong PageSize { get; init; } = 4096;
    public ulong InstalledBytes { get; init; }
    public ulong TotalPhysicalBytes { get; init; }
    public ulong AvailableBytes { get; init; }
    public ulong HardwareReservedBytes => InstalledBytes > TotalPhysicalBytes ? InstalledBytes - TotalPhysicalBytes : 0;
    /// <summary>Resource-Monitor definition: total − (standby + free + zeroed + modified).</summary>
    public ulong InUseBytes
    {
        get
        {
            var notInUse = Lists.Standby + Lists.FreeAndZeroed + Lists.Modified;
            return TotalPhysicalBytes > notInUse ? TotalPhysicalBytes - notInUse : 0;
        }
    }
    public MemoryLists Lists { get; init; } = new();
    public IReadOnlyList<MemoryConsumer> Consumers { get; init; } = [];
    public ulong CommitTotal { get; init; }
    public ulong CommitLimit { get; init; }
    public ulong CommitPeak { get; init; }
    public uint ProcessCount { get; init; }
    public uint ThreadCount { get; init; }
    public uint HandleCount { get; init; }
}

public enum MemoryEmptyAction
{
    /// <summary>Trim the working sets of every process (MemoryEmptyWorkingSets).</summary>
    WorkingSets,
    /// <summary>Trim the system / file-cache working set (SetSystemFileCacheSize(-1, -1)).</summary>
    SystemWorkingSet,
    /// <summary>Write the modified page list to disk (MemoryFlushModifiedList).</summary>
    ModifiedPageList,
    /// <summary>Purge the whole standby list (MemoryPurgeStandbyList).</summary>
    StandbyList,
    /// <summary>Purge priority-0 standby pages only (MemoryPurgeLowPriorityStandbyList).</summary>
    PriorityZeroStandbyList
}

public sealed record MemoryEmptyResult(bool Success, ulong FreedBytes, string? Error);
