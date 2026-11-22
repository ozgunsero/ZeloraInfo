// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

namespace ZeloraInfo.Core.SystemInfo;

public sealed class OperatingSystemInfo
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    /// <summary>e.g. "24H2"</summary>
    public string DisplayVersion { get; init; } = "";
    public string Build { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string ComputerName { get; init; } = "";
    public string UserName { get; init; } = "";
    public DateTime? InstallDate { get; init; }
    public DateTime? LastBootTime { get; init; }
    public TimeSpan Uptime { get; init; }
    public string FirmwareType { get; init; } = "";
    public bool? SecureBootEnabled { get; init; }
    public bool IsAdministrator { get; init; }
}

public sealed class CpuInfo
{
    public string Name { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public int Sockets { get; init; }
    public int Cores { get; init; }
    public int LogicalProcessors { get; init; }
    public int BaseClockMHz { get; init; }
    public int? CurrentClockMHz { get; set; }
    public double? UsagePercent { get; set; }
    public int? L2CacheKiB { get; init; }
    public int? L3CacheKiB { get; init; }
    public string Socket { get; init; } = "";
    public string Identifier { get; init; } = "";
    public bool? VirtualizationEnabled { get; init; }
}

public sealed class BoardInfo
{
    public string SystemManufacturer { get; init; } = "";
    public string SystemModel { get; init; } = "";
    public string BoardManufacturer { get; init; } = "";
    public string BoardProduct { get; init; } = "";
    public string BoardVersion { get; init; } = "";
    public string BiosVendor { get; init; } = "";
    public string BiosVersion { get; init; } = "";
    public DateTime? BiosReleaseDate { get; init; }
}

public sealed class GpuInfo
{
    public string Name { get; init; } = "";
    public string DriverVersion { get; init; } = "";
    public DateTime? DriverDate { get; init; }
    public long? DedicatedMemoryBytes { get; init; }
    public string VideoProcessor { get; init; } = "";
    public string CurrentMode { get; init; } = "";
}

public sealed class SystemSnapshot
{
    public OperatingSystemInfo Os { get; init; } = new();
    public CpuInfo Cpu { get; init; } = new();
    public BoardInfo Board { get; init; } = new();
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
}

/// <summary>Lightweight values refreshed every few seconds.</summary>
public sealed record SystemLiveSample(double CpuUsagePercent, int? CurrentClockMHz, TimeSpan Uptime);
