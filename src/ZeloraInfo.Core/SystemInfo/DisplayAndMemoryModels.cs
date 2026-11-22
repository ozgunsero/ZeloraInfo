// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

namespace ZeloraInfo.Core.SystemInfo;

public sealed class DisplayInfo
{
    /// <summary>GDI device name, e.g. \\.\DISPLAY1</summary>
    public string DeviceName { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public string AdapterName { get; init; } = "";
    public bool IsPrimary { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int? PreferredWidth { get; init; }
    public int? PreferredHeight { get; init; }
    public double RefreshRateHz { get; init; }
    public double? PreferredRefreshRateHz { get; init; }
    public double? MaxRefreshRateHz { get; init; }
    public int BitsPerPixel { get; init; }
    public int? BitsPerColorChannel { get; init; }
    public bool? HdrSupported { get; init; }
    public bool? HdrEnabled { get; init; }
    public uint DpiX { get; init; }
    public int ScalePercent { get; init; }
    public double? PhysicalWidthCm { get; init; }
    public double? PhysicalHeightCm { get; init; }
    public double? DiagonalInches { get; init; }
    public string AspectRatio { get; init; } = "";
    public string Orientation { get; init; } = "";
    public string ConnectionType { get; init; } = "";
    /// <summary>Virtual desktop coordinates (physical pixels).</summary>
    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }
    public int WorkLeft { get; init; }
    public int WorkTop { get; init; }
    public int WorkRight { get; init; }
    public int WorkBottom { get; init; }
}

public sealed class DisplaySnapshot
{
    public IReadOnlyList<DisplayInfo> Displays { get; init; } = [];
    public int VirtualLeft { get; init; }
    public int VirtualTop { get; init; }
    public int VirtualWidth { get; init; }
    public int VirtualHeight { get; init; }
}

public sealed class MemoryStatus
{
    public ulong TotalPhysicalBytes { get; init; }
    public ulong AvailablePhysicalBytes { get; init; }
    public ulong UsedPhysicalBytes => TotalPhysicalBytes - AvailablePhysicalBytes;
    public uint LoadPercent { get; init; }
    public ulong CommitTotalBytes { get; init; }
    public ulong CommitLimitBytes { get; init; }
    public ulong CommitAvailableBytes { get; init; }
}

public sealed class MemoryModule
{
    public string Slot { get; init; } = "";
    public string Bank { get; init; } = "";
    public ulong CapacityBytes { get; init; }
    public int? SpeedMTs { get; init; }
    public int? ConfiguredSpeedMTs { get; init; }
    public string MemoryType { get; init; } = "";
    public string FormFactor { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string PartNumber { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public int? DataWidth { get; init; }
    public double? VoltageV { get; init; }
}

public sealed class PageFileInfo
{
    public string Path { get; init; } = "";
    public ulong AllocatedBytes { get; init; }
    public ulong CurrentUsageBytes { get; init; }
    public ulong PeakUsageBytes { get; init; }
}

public sealed class MemorySnapshot
{
    public MemoryStatus Status { get; init; } = new();
    public IReadOnlyList<MemoryModule> Modules { get; init; } = [];
    public int? TotalSlots { get; init; }
    public ulong? MaxCapacityBytes { get; init; }
    public IReadOnlyList<PageFileInfo> PageFiles { get; init; } = [];
    public bool PageFileAutomaticallyManaged { get; init; }
}
