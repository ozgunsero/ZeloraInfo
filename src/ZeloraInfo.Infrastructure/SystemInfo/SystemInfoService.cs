// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Runtime.InteropServices;
using Microsoft.Win32;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.SystemInfo;
using ZeloraInfo.Infrastructure.Interop;

namespace ZeloraInfo.Infrastructure.SystemInfo;

/// <summary>
/// OS / CPU / board / GPU information from WMI and the registry. Live CPU usage and clock speed are read
/// through PDH ("% Processor Utility" / "% Processor Performance", the same counters Task Manager uses),
/// falling back to GetSystemTimes and CallNtPowerInformation.
/// </summary>
public sealed partial class SystemInfoService(Core.Abstractions.IPrivilegeService privileges) : ISystemInfoService, IDisposable
{
    private readonly object _gate = new();
    private int _baseClockMHz;
    private IntPtr _pdhQuery;
    private IntPtr _utilityCounter;
    private IntPtr _performanceCounter;
    private bool _pdhInitialized;
    private ulong _prevIdle, _prevKernel, _prevUser;

    public Task<SystemSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var os = ReadOs();
        cancellationToken.ThrowIfCancellationRequested();
        var cpu = ReadCpu();
        _baseClockMHz = cpu.BaseClockMHz;
        cancellationToken.ThrowIfCancellationRequested();
        var board = ReadBoard();
        var gpus = ReadGpus();
        return new SystemSnapshot { Os = os, Cpu = cpu, Board = board, Gpus = gpus };
    }, cancellationToken);

    public SystemLiveSample SampleLive()
    {
        lock (_gate)
        {
            EnsurePdh();
            double usage;
            int? clock = null;
            if (_pdhQuery != IntPtr.Zero && PdhCollectQueryData(_pdhQuery) == 0)
            {
                usage = ReadCounter(_utilityCounter) ?? ReadSystemTimesUsage();
                var perf = ReadCounter(_performanceCounter);
                if (perf is > 0 && _baseClockMHz > 0) clock = (int)Math.Round(_baseClockMHz * perf.Value / 100d);
            }
            else
            {
                usage = ReadSystemTimesUsage();
            }
            clock ??= ReadPowerInformationMHz();
            return new SystemLiveSample(Math.Clamp(usage, 0, 100), clock, TimeSpan.FromMilliseconds(Environment.TickCount64));
        }
    }

    // ------------------------------------------------------------------ OS

    private OperatingSystemInfo ReadOs()
    {
        var o = Wmi.Query(Wmi.Cimv2, "SELECT Caption, Version, BuildNumber, OSArchitecture, LastBootUpTime, InstallDate FROM Win32_OperatingSystem").FirstOrDefault();
        using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var displayVersion = cv?.GetValue("DisplayVersion") as string ?? cv?.GetValue("ReleaseId") as string ?? "";
        var currentBuild = cv?.GetValue("CurrentBuild") as string ?? o?.Str("BuildNumber") ?? Environment.OSVersion.Version.Build.ToString();
        var ubr = cv?.GetValue("UBR") is int u ? u : (int?)null;

        var caption = o?.Str("Caption") ?? RuntimeInformation.OSDescription;
        // Win32_OperatingSystem still reports "Windows 10" on some Windows 11 SKUs; build >= 22000 is Windows 11.
        if (int.TryParse(currentBuild, out var buildNo) && buildNo >= 22000 && caption.Contains("Windows 10", StringComparison.Ordinal))
            caption = caption.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);

        string firmware;
        try
        {
            firmware = NativeMethods.GetFirmwareType(out var ft) ? ft switch { 1 => "BIOS (Legacy)", 2 => "UEFI", _ => "?" } : "?";
        }
        catch (EntryPointNotFoundException)
        {
            firmware = "?";
        }

        bool? secureBoot = null;
        using (var sb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
        {
            if (sb?.GetValue("UEFISecureBootEnabled") is int v) secureBoot = v == 1;
        }

        var boot = o?.Date("LastBootUpTime");
        return new OperatingSystemInfo
        {
            Name = caption,
            Version = o?.Str("Version") ?? Environment.OSVersion.Version.ToString(),
            DisplayVersion = displayVersion,
            Build = ubr is null ? currentBuild : $"{currentBuild}.{ubr}",
            Architecture = string.IsNullOrEmpty(o?.Str("OSArchitecture")) ? RuntimeInformation.OSArchitecture.ToString() : o!.Str("OSArchitecture"),
            ComputerName = Environment.MachineName,
            UserName = Environment.UserDomainName is { Length: > 0 } d && !d.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                ? $@"{d}\{Environment.UserName}" : Environment.UserName,
            InstallDate = o?.Date("InstallDate"),
            LastBootTime = boot ?? DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64),
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            FirmwareType = firmware,
            SecureBootEnabled = secureBoot,
            IsAdministrator = privileges.IsAdministrator,
        };
    }

    // ------------------------------------------------------------------ CPU

    private static CpuInfo ReadCpu()
    {
        var cpus = Wmi.Query(Wmi.Cimv2, "SELECT * FROM Win32_Processor");
        var first = cpus.FirstOrDefault();
        if (first is null)
        {
            return new CpuInfo
            {
                Name = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "",
                Cores = Environment.ProcessorCount,
                LogicalProcessors = Environment.ProcessorCount,
                Sockets = 1,
            };
        }

        int? l2 = first.Int("L2CacheSize");
        int? l3 = first.Int("L3CacheSize");
        return new CpuInfo
        {
            Name = System.Text.RegularExpressions.Regex.Replace(first.Str("Name"), @"\s+", " "),
            Manufacturer = first.Str("Manufacturer"),
            Sockets = cpus.Count,
            Cores = cpus.Sum(c => c.Int("NumberOfCores") ?? 0),
            LogicalProcessors = cpus.Sum(c => c.Int("NumberOfLogicalProcessors") ?? 0) is > 0 and var lp ? lp : Environment.ProcessorCount,
            BaseClockMHz = first.Int("MaxClockSpeed") ?? 0,
            CurrentClockMHz = first.Int("CurrentClockSpeed"),
            L2CacheKiB = l2 is > 0 ? l2 * cpus.Count : null,
            L3CacheKiB = l3 is > 0 ? l3 * cpus.Count : null,
            Socket = first.Str("SocketDesignation"),
            Identifier = first.Str("Caption"),
            VirtualizationEnabled = first.Bool("VirtualizationFirmwareEnabled"),
        };
    }

    // ------------------------------------------------------------------ Board / BIOS

    private static BoardInfo ReadBoard()
    {
        var cs = Wmi.Query(Wmi.Cimv2, "SELECT Manufacturer, Model FROM Win32_ComputerSystem").FirstOrDefault();
        var bb = Wmi.Query(Wmi.Cimv2, "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard").FirstOrDefault();
        var bios = Wmi.Query(Wmi.Cimv2, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        return new BoardInfo
        {
            SystemManufacturer = cs?.Str("Manufacturer") ?? "",
            SystemModel = cs?.Str("Model") ?? "",
            BoardManufacturer = bb?.Str("Manufacturer") ?? "",
            BoardProduct = bb?.Str("Product") ?? "",
            BoardVersion = bb?.Str("Version") ?? "",
            BiosVendor = bios?.Str("Manufacturer") ?? "",
            BiosVersion = bios?.Str("SMBIOSBIOSVersion") ?? "",
            BiosReleaseDate = bios?.Date("ReleaseDate"),
        };
    }

    // ------------------------------------------------------------------ GPU

    private static List<GpuInfo> ReadGpus()
    {
        var registryMemory = ReadGpuMemoryFromRegistry();
        var list = new List<GpuInfo>();
        foreach (var g in Wmi.Query(Wmi.Cimv2, "SELECT Name, DriverVersion, DriverDate, AdapterRAM, VideoProcessor, VideoModeDescription FROM Win32_VideoController"))
        {
            var name = g.Str("Name");
            long? memory = registryMemory.TryGetValue(name, out var m) ? m : null;
            if (memory is null && g.Long("AdapterRAM") is > 0 and var ram) memory = ram;
            list.Add(new GpuInfo
            {
                Name = name,
                DriverVersion = g.Str("DriverVersion"),
                DriverDate = g.Date("DriverDate"),
                DedicatedMemoryBytes = memory,
                VideoProcessor = g.Str("VideoProcessor"),
                CurrentMode = g.Str("VideoModeDescription"),
            });
        }
        return list;
    }

    /// <summary>Win32_VideoController.AdapterRAM is a UInt32 (caps at 4 GiB); the driver key has the full 64-bit value.</summary>
    private static Dictionary<string, long> ReadGpuMemoryFromRegistry()
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return map;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k?.GetValue("DriverDesc") is not string desc) continue;
                    var value = k.GetValue("HardwareInformation.qwMemorySize");
                    long bytes = value switch
                    {
                        long l => l,
                        byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        int i => (uint)i,
                        _ => 0
                    };
                    if (bytes <= 0 && k.GetValue("HardwareInformation.MemorySize") is { } v2)
                        bytes = v2 switch { int i => (uint)i, byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0), _ => 0 };
                    if (bytes > 0) map[desc] = bytes;
                }
                catch (System.Security.SecurityException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
        }
        return map;
    }

    // ------------------------------------------------------------------ live sampling

    private void EnsurePdh()
    {
        if (_pdhInitialized) return;
        _pdhInitialized = true;
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _pdhQuery) != 0) { _pdhQuery = IntPtr.Zero; return; }
            if (PdhAddEnglishCounter(_pdhQuery, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out _utilityCounter) != 0)
            {
                PdhAddEnglishCounter(_pdhQuery, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out _utilityCounter);
            }
            PdhAddEnglishCounter(_pdhQuery, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out _performanceCounter);
            PdhCollectQueryData(_pdhQuery); // prime: rate counters need two samples
        }
        catch (DllNotFoundException)
        {
            _pdhQuery = IntPtr.Zero;
        }
    }

    private static double? ReadCounter(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var value) != 0) return null;
        return value.CStatus is 0 or 1 ? value.DoubleValue : null;
    }

    private double ReadSystemTimesUsage()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var (i, k, u) = (idle.Value, kernel.Value, user.Value);
        var idleDelta = i - _prevIdle;
        var total = (k - _prevKernel) + (u - _prevUser);
        (_prevIdle, _prevKernel, _prevUser) = (i, k, u);
        return total == 0 ? 0 : (total - idleDelta) * 100d / total;
    }

    private static int? ReadPowerInformationMHz()
    {
        try
        {
            var count = Environment.ProcessorCount;
            var info = new NativeMethods.PROCESSOR_POWER_INFORMATION[count];
            var size = (uint)(Marshal.SizeOf<NativeMethods.PROCESSOR_POWER_INFORMATION>() * count);
            if (NativeMethods.CallNtPowerInformation(NativeMethods.ProcessorInformation, IntPtr.Zero, 0, info, size) != 0) return null;
            return (int)info.Average(p => p.CurrentMhz);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_pdhQuery != IntPtr.Zero)
            {
                PdhCloseQuery(_pdhQuery);
                _pdhQuery = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(IntPtr query);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(IntPtr query);
}
