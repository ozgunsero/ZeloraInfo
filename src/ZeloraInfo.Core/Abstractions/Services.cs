// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using ZeloraInfo.Core.Benchmark;
using ZeloraInfo.Core.Disks;
using ZeloraInfo.Core.Settings;
using ZeloraInfo.Core.SystemInfo;

namespace ZeloraInfo.Core.Abstractions;

public interface ISettingsService
{
    AppSettings Current { get; }
    string SettingsFilePath { get; }
    event EventHandler<AppSettings>? SettingsChanged;
    AppSettings Load();
    void Save();
    AppSettings ResetToDefaults();
}

public interface IPrivilegeService
{
    bool IsAdministrator { get; }
    /// <summary>Starts an elevated copy of the current executable. Returns false if the user declined UAC.</summary>
    bool TryRestartElevated(string? arguments = null);
}

public interface IStartupRegistrationService
{
    bool IsRegistered();
    void SetRegistered(bool enabled, string arguments = "--minimized");
}

public interface IDiskInfoService
{
    /// <summary>Enumerates physical disks and reads SMART where possible. Never throws for a single bad disk.</summary>
    Task<IReadOnlyList<DiskInfo>> GetDisksAsync(SmartSettings settings, CancellationToken cancellationToken = default);
}

public interface IBenchmarkService
{
    Task<BenchmarkReport> RunAsync(BenchmarkOptions options, IProgress<BenchmarkProgress>? progress, Action<BenchmarkResult>? onResult, CancellationToken cancellationToken);
    /// <summary>Removes orphaned test files under the given roots. Returns how many were deleted.</summary>
    int CleanupOrphanedTestFiles(IEnumerable<string> roots);
}

public interface ISystemInfoService
{
    Task<SystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    SystemLiveSample SampleLive();
}

public interface IDisplayInfoService
{
    DisplaySnapshot GetSnapshot();
}

public interface IMemoryInfoService
{
    Task<MemorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    MemoryStatus GetStatus();
}

/// <summary>Processor identification, topology, caches, live clocks and a light synthetic benchmark.</summary>
public interface ICpuZService
{
    Task<Hardware.CpuZSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Hardware.CpuZLiveSample SampleLive();
    Task<Hardware.CpuBenchmarkResult> RunBenchmarkAsync(TimeSpan durationPerPhase, IProgress<Hardware.CpuBenchmarkProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>RAMMap-style view: page lists, standby priorities, consumers, processes, physical ranges and "empty" actions.</summary>
public interface IMemoryMapService
{
    Hardware.MemoryMapSnapshot GetSnapshot();
    IReadOnlyList<Hardware.MemoryProcessInfo> GetProcesses();
    IReadOnlyList<Hardware.PhysicalRange> GetPhysicalRanges();
    /// <summary>Requires administrator rights; enables the needed privileges itself.</summary>
    Hardware.MemoryEmptyResult Empty(Hardware.MemoryEmptyAction action);
}

/// <summary>Graphics adapters: identity, memory, driver, DirectX 12 capabilities and live telemetry.</summary>
public interface IGpuService
{
    Task<IReadOnlyList<Hardware.GpuAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default);
    /// <summary>One sample per adapter LUID (utilization, engines, memory, sensors where the driver reports them).</summary>
    IReadOnlyList<Hardware.GpuLiveSample> SampleLive();
}

/// <summary>Memory controller summary, timings (where readable) and SMBIOS slot / module details.</summary>
public interface IRamZService
{
    Task<Hardware.RamZSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    MemoryStatus GetStatus();
}
