// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Runtime.InteropServices;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.SystemInfo;
using ZeloraInfo.Infrastructure.Interop;

namespace ZeloraInfo.Infrastructure.SystemInfo;

/// <summary>GlobalMemoryStatusEx for live usage; SMBIOS (Win32_PhysicalMemory*) for DIMM details; Win32_PageFileUsage for virtual memory.</summary>
public sealed class MemoryInfoService : IMemoryInfoService
{
    private static readonly Dictionary<string, string> JedecVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["80CE"] = "Samsung", ["CE00"] = "Samsung", ["80AD"] = "SK hynix", ["AD00"] = "SK hynix",
        ["802C"] = "Micron", ["2C00"] = "Micron", ["859B"] = "Crucial", ["9801"] = "Kingston", ["0198"] = "Kingston",
        ["04CB"] = "ADATA", ["CB04"] = "ADATA", ["029E"] = "Corsair", ["9E02"] = "Corsair", ["04CD"] = "G.Skill", ["CD04"] = "G.Skill",
        ["8551"] = "Qimonda", ["80C1"] = "Infineon", ["0B83"] = "Patriot", ["8502"] = "Patriot", ["86F1"] = "TeamGroup", ["04EF"] = "TeamGroup",
    };

    public MemoryStatus GetStatus()
    {
        var s = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref s)) return new MemoryStatus();
        return new MemoryStatus
        {
            TotalPhysicalBytes = s.ullTotalPhys,
            AvailablePhysicalBytes = s.ullAvailPhys,
            LoadPercent = s.dwMemoryLoad,
            CommitLimitBytes = s.ullTotalPageFile,
            CommitAvailableBytes = s.ullAvailPageFile,
            CommitTotalBytes = s.ullTotalPageFile - s.ullAvailPageFile,
        };
    }

    public Task<MemorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var modules = Wmi.Query(Wmi.Cimv2, "SELECT * FROM Win32_PhysicalMemory")
            .Select(m => new MemoryModule
            {
                Slot = m.Str("DeviceLocator"),
                Bank = m.Str("BankLabel"),
                CapacityBytes = m.ULong("Capacity") ?? 0,
                SpeedMTs = m.Int("Speed") is > 0 and var sp ? sp : null,
                ConfiguredSpeedMTs = m.Int("ConfiguredClockSpeed") is > 0 and var cs ? cs : null,
                MemoryType = MapMemoryType(m.Int("SMBIOSMemoryType") ?? 0, m.Int("MemoryType") ?? 0),
                FormFactor = MapFormFactor(m.Int("FormFactor") ?? 0),
                Manufacturer = MapVendor(m.Str("Manufacturer")),
                PartNumber = m.Str("PartNumber"),
                SerialNumber = m.Str("SerialNumber"),
                DataWidth = m.Int("DataWidth"),
                VoltageV = m.Int("ConfiguredVoltage") is > 0 and var mv ? mv / 1000d : null,
            })
            .Where(m => m.CapacityBytes > 0)
            .ToList();

        cancellationToken.ThrowIfCancellationRequested();
        int? slots = null;
        ulong? maxCapacity = null;
        foreach (var a in Wmi.Query(Wmi.Cimv2, "SELECT MemoryDevices, MaxCapacity, MaxCapacityEx, Use FROM Win32_PhysicalMemoryArray"))
        {
            if (a.Int("Use") is { } use && use != 3) continue; // 3 = System memory
            slots = (slots ?? 0) + (a.Int("MemoryDevices") ?? 0);
            var kib = a.ULong("MaxCapacityEx") is > 0 and var ex ? ex : a.ULong("MaxCapacity") ?? 0;
            if (kib > 0) maxCapacity = (maxCapacity ?? 0) + kib * 1024;
        }

        var pageFiles = Wmi.Query(Wmi.Cimv2, "SELECT Name, AllocatedBaseSize, CurrentUsage, PeakUsage FROM Win32_PageFileUsage")
            .Select(p => new PageFileInfo
            {
                Path = p.Str("Name"),
                AllocatedBytes = (p.ULong("AllocatedBaseSize") ?? 0) * 1024 * 1024,
                CurrentUsageBytes = (p.ULong("CurrentUsage") ?? 0) * 1024 * 1024,
                PeakUsageBytes = (p.ULong("PeakUsage") ?? 0) * 1024 * 1024,
            }).ToList();
        var automatic = Wmi.Query(Wmi.Cimv2, "SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem").FirstOrDefault()?.Bool("AutomaticManagedPagefile") ?? false;

        return new MemorySnapshot
        {
            Status = GetStatus(),
            Modules = modules,
            TotalSlots = slots is > 0 ? slots : null,
            MaxCapacityBytes = maxCapacity,
            PageFiles = pageFiles,
            PageFileAutomaticallyManaged = automatic,
        };
    }, cancellationToken);

    internal static string MapMemoryType(int smbiosType, int legacyType) => smbiosType switch
    {
        18 => "DDR",
        19 => "DDR2",
        20 => "DDR",
        21 => "DDR2",
        22 => "DDR2 FB-DIMM",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => legacyType switch
        {
            20 => "DDR",
            21 => "DDR2",
            24 => "DDR3",
            _ => "—"
        }
    };

    internal static string MapFormFactor(int formFactor) => formFactor switch
    {
        8 => "DIMM",
        12 => "SO-DIMM",
        13 => "SRIMM",
        7 => "SIMM",
        11 => "RIMM",
        9 => "TSOP",
        2 => "SIP",
        3 => "DIP",
        0 => "—",
        _ => "Other"
    };

    internal static string MapVendor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "—";
        var key = raw.Trim();
        if (JedecVendors.TryGetValue(key, out var name)) return name;
        if (key.Length >= 4 && JedecVendors.TryGetValue(key[..4], out name)) return name;
        return key;
    }
}
