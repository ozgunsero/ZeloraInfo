// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Runtime.InteropServices;
using Microsoft.Win32;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.Hardware;
using ZeloraInfo.Infrastructure.Interop;

namespace ZeloraInfo.Infrastructure.Memory;

/// <summary>
/// RAMMap-style memory analysis using documented / stable sources:
/// <list type="bullet">
/// <item>Administrator: NtQuerySystemInformation(SystemMemoryListInformation) — exact zeroed / free / modified / bad lists and
/// standby + repurposed counts for all 8 priorities.</item>
/// <item>No elevation: \Memory\* performance counters (standby grouped as Reserve 0–1 / Normal 2–5 / Core 6–7).</item>
/// <item>GetPerformanceInfo, GetPhysicallyInstalledSystemMemory, SystemProcessInformation (per-process private working set),
/// HKLM\HARDWARE\RESOURCEMAP physical ranges.</item>
/// <item>"Empty" actions: NtSetSystemInformation(SystemMemoryListInformation) commands and SetSystemFileCacheSize(-1, -1).</item>
/// </list>
/// Per-page attribution (RAMMap "Use Counts", "File Summary", "Physical Pages") needs the undocumented Superfetch PFN query
/// and is intentionally not implemented.
/// </summary>
public sealed partial class MemoryMapService(Core.Abstractions.IPrivilegeService privileges) : IMemoryMapService, IDisposable
{
    private const int SystemProcessInformation = 5;
    private const int SystemMemoryListInformation = 80;

    private static readonly string[] Counters =
    [
        @"\Memory\Free & Zero Page List Bytes",
        @"\Memory\Modified Page List Bytes",
        @"\Memory\Standby Cache Reserve Bytes",
        @"\Memory\Standby Cache Normal Priority Bytes",
        @"\Memory\Standby Cache Core Bytes",
        @"\Memory\Cache Bytes",
        @"\Memory\Pool Paged Resident Bytes",
        @"\Memory\Pool Nonpaged Bytes",
        @"\Memory\System Driver Resident Bytes",
        @"\Memory\System Code Resident Bytes",
    ];

    private readonly object _gate = new();
    private PdhQuery? _pdh;
    private bool _privilegeEnabled;

    public MemoryMapSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var perf = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
            var hasPerf = GetPerformanceInfo(ref perf, perf.cb);
            var pageSize = hasPerf && perf.PageSize > 0 ? (ulong)perf.PageSize : 4096UL;

            var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
            NativeMethods.GlobalMemoryStatusEx(ref status);
            ulong installed = GetPhysicallyInstalledSystemMemory(out var kb) ? kb * 1024 : status.ullTotalPhys;

            EnsurePdh();
            _pdh!.Collect();
            double C(int i) => _pdh.Read(Counters[i]) ?? 0;

            var source = MemoryListSource.PerformanceCounters;
            MemoryLists lists;
            var kernel = privileges.IsAdministrator ? QueryKernelLists(pageSize) : null;
            if (kernel is not null)
            {
                lists = kernel;
                source = MemoryListSource.Kernel;
            }
            else
            {
                lists = new MemoryLists
                {
                    Zeroed = 0,
                    Free = (ulong)C(0),
                    Modified = (ulong)C(1),
                    StandbyByPriority =
                    [
                        new StandbyPriority { Priority = "0–1", Bytes = (ulong)C(2) },
                        new StandbyPriority { Priority = "2–5", Bytes = (ulong)C(3) },
                        new StandbyPriority { Priority = "6–7", Bytes = (ulong)C(4) },
                    ],
                };
            }

            ulong privateWs = 0;
            foreach (var p in GetProcessesCore()) privateWs += p.PrivateWorkingSet;

            ulong Pages(UIntPtr v) => (ulong)v * pageSize;
            var consumers = new List<MemoryConsumer>
            {
                new() { Key = "ProcessPrivate", Bytes = privateWs },
                new() { Key = "SystemCache", Bytes = C(5) > 0 ? (ulong)C(5) : hasPerf ? Pages(perf.SystemCache) : 0 },
                new() { Key = "PagedPool", Bytes = C(6) > 0 ? (ulong)C(6) : hasPerf ? Pages(perf.KernelPaged) : 0 },
                new() { Key = "NonPagedPool", Bytes = C(7) > 0 ? (ulong)C(7) : hasPerf ? Pages(perf.KernelNonpaged) : 0 },
                new() { Key = "Drivers", Bytes = (ulong)C(8) },
                new() { Key = "KernelCode", Bytes = (ulong)C(9) },
            };

            return new MemoryMapSnapshot
            {
                Source = source,
                PageSize = pageSize,
                InstalledBytes = Math.Max(installed, status.ullTotalPhys),
                TotalPhysicalBytes = status.ullTotalPhys,
                AvailableBytes = status.ullAvailPhys,
                Lists = lists,
                Consumers = consumers,
                CommitTotal = hasPerf ? Pages(perf.CommitTotal) : 0,
                CommitLimit = hasPerf ? Pages(perf.CommitLimit) : 0,
                CommitPeak = hasPerf ? Pages(perf.CommitPeak) : 0,
                ProcessCount = hasPerf ? perf.ProcessCount : 0,
                ThreadCount = hasPerf ? perf.ThreadCount : 0,
                HandleCount = hasPerf ? perf.HandleCount : 0,
            };
        }
    }

    public IReadOnlyList<MemoryProcessInfo> GetProcesses() => GetProcessesCore();

    private static List<MemoryProcessInfo> GetProcessesCore()
    {
        var size = 1 << 20;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQuerySystemInformation(SystemProcessInformation, buffer, size, out var needed);
                if (status == 0) return MemoryMapParsers.ParseProcesses(buffer, size);
                if (status != unchecked((int)0xC0000004)) return []; // STATUS_INFO_LENGTH_MISMATCH
                size = Math.Max(size * 2, needed + (1 << 16));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return [];
    }

    public IReadOnlyList<PhysicalRange> GetPhysicalRanges()
    {
        // .NET returns null for REG_RESOURCE_LIST values, so read the raw bytes with RegQueryValueEx.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\RESOURCEMAP\System Resources\Physical Memory");
            if (key is null) return [];
            var handle = key.Handle.DangerousGetHandle();
            uint size = 0;
            if (RegQueryValueEx(handle, ".Translated", IntPtr.Zero, out _, null, ref size) != 0 || size == 0) return [];
            var data = new byte[size];
            if (RegQueryValueEx(handle, ".Translated", IntPtr.Zero, out _, data, ref size) != 0) return [];
            return MemoryMapParsers.ParseResourceList(data.AsSpan(0, (int)size));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ObjectDisposedException)
        {
        }
        return [];
    }

    // ------------------------------------------------------------------ empty actions

    public MemoryEmptyResult Empty(MemoryEmptyAction action)
    {
        if (!privileges.IsAdministrator) return new MemoryEmptyResult(false, 0, "administrator");
        var before = GetSnapshot();
        try
        {
            if (action == MemoryEmptyAction.SystemWorkingSet)
            {
                if (!EnablePrivilege("SeIncreaseQuotaPrivilege")) return new MemoryEmptyResult(false, 0, "SeIncreaseQuotaPrivilege");
                if (!SetSystemFileCacheSize(new IntPtr(-1), new IntPtr(-1), 0))
                    return new MemoryEmptyResult(false, 0, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
            else
            {
                if (!EnablePrivilege("SeProfileSingleProcessPrivilege")) return new MemoryEmptyResult(false, 0, "SeProfileSingleProcessPrivilege");
                var command = action switch
                {
                    MemoryEmptyAction.WorkingSets => 2,              // MemoryEmptyWorkingSets
                    MemoryEmptyAction.ModifiedPageList => 3,         // MemoryFlushModifiedList
                    MemoryEmptyAction.StandbyList => 4,              // MemoryPurgeStandbyList
                    MemoryEmptyAction.PriorityZeroStandbyList => 5,  // MemoryPurgeLowPriorityStandbyList
                    _ => throw new ArgumentOutOfRangeException(nameof(action))
                };
                var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
                if (status != 0) return new MemoryEmptyResult(false, 0, $"NTSTATUS 0x{status:X8}");
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return new MemoryEmptyResult(false, 0, ex.Message);
        }

        Thread.Sleep(300);
        var after = GetSnapshot();
        ulong Gain(ulong a, ulong b) => a > b ? a - b : 0;
        var moved = action switch
        {
            MemoryEmptyAction.WorkingSets or MemoryEmptyAction.SystemWorkingSet => Gain(before.InUseBytes, after.InUseBytes),
            MemoryEmptyAction.ModifiedPageList => Gain(before.Lists.Modified, after.Lists.Modified),
            _ => Gain(after.Lists.FreeAndZeroed, before.Lists.FreeAndZeroed),
        };
        return new MemoryEmptyResult(true, moved, null);
    }

    // ------------------------------------------------------------------ helpers

    private MemoryLists? QueryKernelLists(ulong pageSize)
    {
        if (!_privilegeEnabled) _privilegeEnabled = EnablePrivilege("SeProfileSingleProcessPrivilege");
        var buffer = Marshal.AllocHGlobal(MemoryMapParsers.MemoryListInformationSize);
        try
        {
            if (NtQuerySystemInformation(SystemMemoryListInformation, buffer, MemoryMapParsers.MemoryListInformationSize, out _) != 0) return null;
            var bytes = new byte[MemoryMapParsers.MemoryListInformationSize];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return MemoryMapParsers.ParseMemoryListInformation(bytes, pageSize);
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void EnsurePdh()
    {
        if (_pdh is not null) return;
        _pdh = new PdhQuery();
        foreach (var c in Counters) _pdh.Add(c);
    }

    private static bool EnablePrivilege(string name)
    {
        const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8, SE_PRIVILEGE_ENABLED = 2;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token)) return false;
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) return false;
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;
            return Marshal.GetLastWin32Error() == 0; // ERROR_NOT_ALL_ASSIGNED (1300) when the token lacks it
        }
        finally
        {
            CloseHandle(token);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _pdh?.Dispose();
            _pdh = null;
        }
    }

    // ------------------------------------------------------------------ interop

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetPerformanceInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetSystemFileCacheSize(IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize, uint flags);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(int systemInformationClass, IntPtr systemInformation, int length, out int returnLength);

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int length);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegQueryValueEx(IntPtr key, string valueName, IntPtr reserved, out uint type, [Out] byte[]? data, ref uint dataSize);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? systemName, string name, out LUID luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TOKEN_PRIVILEGES newState,
        uint bufferLength, IntPtr previousState, IntPtr returnLength);
}
