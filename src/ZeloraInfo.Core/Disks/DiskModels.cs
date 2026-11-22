// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

namespace ZeloraInfo.Core.Disks;

public enum HealthStatus
{
    Unknown,
    Good,
    Caution,
    Bad
}

public enum DiskBusKind
{
    Unknown,
    Sata,
    Nvme,
    Usb,
    Sas,
    Scsi,
    Raid,
    Virtual,
    SdCard,
    Other
}

public enum DiskMediaKind
{
    Unknown,
    Hdd,
    Ssd,
    Scm
}

public enum SmartSource
{
    /// <summary>No SMART data could be read (restricted mode, unsupported bridge, virtual disk …).</summary>
    None,
    /// <summary>ATA SMART READ DATA / READ THRESHOLDS via SMART_RCV_DRIVE_DATA.</summary>
    Ata,
    /// <summary>NVMe SMART / Health Information log page (02h) via IOCTL_STORAGE_QUERY_PROPERTY.</summary>
    Nvme,
    /// <summary>Windows Storage reliability counters (MSFT_StorageReliabilityCounter).</summary>
    StorageReliability
}

public enum AttributeStatus
{
    Ok,
    Info,
    Caution,
    Bad
}

public sealed class SmartAttribute
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Normalized current value (ATA). Null for NVMe log fields.</summary>
    public int? Current { get; init; }
    public int? Worst { get; init; }
    public int? Threshold { get; init; }
    public required ulong RawValue { get; init; }
    /// <summary>Raw value as 12-digit hex (ATA, 6 bytes) or full decimal (NVMe).</summary>
    public string RawDisplay { get; init; } = "";
    public AttributeStatus Status { get; set; } = AttributeStatus.Ok;
    public string IdHex => Id.ToString("X2");
}

public sealed class DiskInfo
{
    public int Index { get; init; }
    public string Model { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string Firmware { get; init; } = "";
    public DiskBusKind Bus { get; init; }
    public DiskMediaKind Media { get; init; }
    public long SizeBytes { get; init; }
    public IReadOnlyList<string> DriveLetters { get; init; } = [];
    public bool IsSystemDisk { get; init; }
    public bool IsRemovable { get; init; }

    /// <summary>"SATA/600 | SATA/600", "PCIe / NVMe" …</summary>
    public string? TransferMode { get; set; }
    public int? RotationRateRpm { get; set; }

    public SmartSource SmartSource { get; set; }
    public HealthStatus Health { get; set; } = HealthStatus.Unknown;
    /// <summary>Remaining life estimate in percent (SSD/NVMe), when available.</summary>
    public int? LifeRemainingPercent { get; set; }
    public int? TemperatureC { get; set; }
    public long? PowerOnHours { get; set; }
    public long? PowerOnCount { get; set; }
    public long? HostReadsBytes { get; set; }
    public long? HostWritesBytes { get; set; }
    /// <summary>Health reported by Windows Storage Spaces (MSFT_PhysicalDisk.HealthStatus).</summary>
    public HealthStatus WindowsHealth { get; set; } = HealthStatus.Unknown;

    public List<SmartAttribute> Attributes { get; set; } = [];
    /// <summary>Localization keys of reasons that contributed to Caution/Bad.</summary>
    public List<string> HealthReasons { get; set; } = [];
    /// <summary>Localization key describing why SMART is unavailable (if it is).</summary>
    public string? SmartUnavailableReason { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? $"Disk {Index}" : Model;
}
