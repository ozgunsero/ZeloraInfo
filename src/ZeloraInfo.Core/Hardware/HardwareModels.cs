// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using ZeloraInfo.Core.SystemInfo;

namespace ZeloraInfo.Core.Hardware;

// =====================================================================================
//  CPU details ("CPU-Z style")
// =====================================================================================

public enum CacheKind
{
    Unified,
    Instruction,
    Data,
    Trace
}

public sealed class CacheLevelInfo
{
    public int Level { get; init; }
    public CacheKind Kind { get; init; }
    public long SizeBytes { get; init; }
    /// <summary>0xFF = fully associative.</summary>
    public int Associativity { get; init; }
    public int LineSizeBytes { get; init; }
    /// <summary>Number of identical cache instances (e.g. 8 for "8 × 48 KB").</summary>
    public int Count { get; init; }
    /// <summary>Logical processors sharing one instance.</summary>
    public int SharedByThreads { get; init; }

    public string Label => Level == 1 ? Kind switch { CacheKind.Data => "L1d", CacheKind.Instruction => "L1i", _ => "L1" } : $"L{Level}";
}

public sealed class CpuZSnapshot
{
    public bool CpuIdAvailable { get; init; }
    public string Vendor { get; init; } = "";
    public string Name { get; init; } = "";
    public string Specification { get; init; } = "";
    public string CodeName { get; init; } = "";
    public string ProcessNode { get; init; } = "";
    public string Package { get; init; } = "";
    public int? Family { get; init; }
    public int? Model { get; init; }
    public int? Stepping { get; init; }
    public int? ExtFamily { get; init; }
    public int? ExtModel { get; init; }
    public int? BaseFamily { get; init; }
    public int? BaseModel { get; init; }
    public string? MicrocodeRevision { get; init; }
    /// <summary>Core voltage / VID needs MSR access (ring-0 driver) — always null in Zelora Info.</summary>
    public double? CoreVoltage { get; init; }
    public IReadOnlyList<string> Instructions { get; init; } = [];
    public int Packages { get; init; }
    public int Cores { get; init; }
    public int Threads { get; init; }
    public int? PerformanceCores { get; init; }
    public int? EfficiencyCores { get; init; }
    public int BaseClockMHz { get; init; }
    public int? MaxClockMHz { get; init; }
    public double BusClockMHz { get; init; }
    /// <summary>True when BCLK could not be read (CPUID 16h) and 100 MHz is assumed.</summary>
    public bool BusClockEstimated { get; init; }
    public bool HypervisorPresent { get; init; }
    public bool? VirtualizationFirmwareEnabled { get; init; }
    public IReadOnlyList<CacheLevelInfo> Caches { get; init; } = [];
}

public sealed record CoreSample(int Index, double? FrequencyMHz, double? UsagePercent);

public sealed class CpuZLiveSample
{
    public double UsagePercent { get; init; }
    public double? AverageMHz { get; init; }
    public double? MaxMHz { get; init; }
    public double? Multiplier { get; init; }
    public IReadOnlyList<CoreSample> Cores { get; init; } = [];
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

public sealed class CpuBenchmarkResult
{
    public double SingleThreadScore { get; init; }
    public double MultiThreadScore { get; init; }
    public int Threads { get; init; }
    public double MultiThreadRatio => SingleThreadScore <= 0 ? 0 : MultiThreadScore / SingleThreadScore;
    public double DurationSeconds { get; init; }
}

public sealed record CpuBenchmarkProgress(bool MultiThread, double Percent);

// =====================================================================================
//  Memory details ("RAM-Z style")
// =====================================================================================

public sealed class RamSlot
{
    public string Locator { get; init; } = "";
    public string Bank { get; init; } = "";
    public bool IsPopulated { get; init; }
    public ulong CapacityBytes { get; init; }
    public string MemoryType { get; init; } = "";
    /// <summary>"Unbuffered", "Registered (Buffered)", "LRDIMM" …</summary>
    public string TypeDetail { get; init; } = "";
    public string FormFactor { get; init; } = "";
    public string Technology { get; init; } = "";
    /// <summary>Maximum rated speed reported by SMBIOS (MT/s).</summary>
    public int? RatedSpeedMTs { get; init; }
    public int? ConfiguredSpeedMTs { get; init; }
    public string Manufacturer { get; init; } = "";
    public string PartNumber { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public int? Rank { get; init; }
    public int? DataWidth { get; init; }
    public int? TotalWidth { get; init; }
    public double? ConfiguredVoltage { get; init; }
    public double? MinVoltage { get; init; }
    public double? MaxVoltage { get; init; }
    public string? Channel { get; init; }
}

/// <summary>
/// DRAM timings. Real values live in the SPD EEPROM (SMBus) and memory-controller registers, both only reachable
/// through a kernel driver. Zelora Info deliberately ships no ring-0 driver, so these stay null unless a future
/// provider can read them; voltage comes from SMBIOS (configured voltage = VDD).
/// </summary>
public sealed class MemoryTimings
{
    public int? Cl { get; init; }
    public int? Rcd { get; init; }
    public int? Rp { get; init; }
    public int? Ras { get; init; }
    public string? CommandRate { get; init; }
    public int? Rfc { get; init; }
    public double? Vdd { get; init; }
    public double? Vddq { get; init; }
    public string Source { get; init; } = "";
}

public sealed class RamZSnapshot
{
    public string Source { get; init; } = "";
    public string MemoryType { get; init; } = "";
    public ulong TotalInstalledBytes { get; init; }
    public string ChannelMode { get; init; } = "";
    public int? ChannelCount { get; init; }
    public bool ChannelModeInferred { get; init; }
    public int? ConfiguredSpeedMTs { get; init; }
    public double? DramFrequencyMHz { get; init; }
    public int? RatedSpeedMTs { get; init; }
    public bool? Ecc { get; init; }
    /// <summary>NB / uncore clock — requires MSR access, always null.</summary>
    public double? UncoreFrequencyMHz { get; init; }
    public int? TotalSlots { get; init; }
    public int PopulatedSlots { get; init; }
    public ulong? MaxCapacityBytes { get; init; }
    public IReadOnlyList<RamSlot> Slots { get; init; } = [];
    public MemoryTimings Timings { get; init; } = new();
    public MemoryStatus Status { get; init; } = new();
    public IReadOnlyList<PageFileInfo> PageFiles { get; init; } = [];
}

public static class SerialMasker
{
    /// <summary>Keeps the last 4 characters: "12AB34CD" → "••••34CD".</summary>
    public static string Mask(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial)) return "—";
        var s = serial.Trim();
        return s.Length <= 4 ? new string('•', s.Length) : new string('•', s.Length - 4) + s[^4..];
    }
}
