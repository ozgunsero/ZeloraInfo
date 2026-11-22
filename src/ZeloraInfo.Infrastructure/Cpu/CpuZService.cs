// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Diagnostics;
using Microsoft.Win32;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.Hardware;
using ZeloraInfo.Infrastructure.Interop;

namespace ZeloraInfo.Infrastructure.Cpu;

/// <summary>
/// Data sources (all user mode, no driver):
/// <list type="bullet">
/// <item>CPUID (X86Base.CpuId): vendor, brand, signature, instruction sets, base/max/bus clock (leaf 16h).</item>
/// <item>GetLogicalProcessorInformationEx: packages, cores, threads, hybrid P/E cores, cache hierarchy.</item>
/// <item>WMI Win32_Processor: socket designation, firmware virtualization flag.</item>
/// <item>Registry CentralProcessor\0 "Update Revision": microcode revision.</item>
/// <item>PDH "% Processor Performance" / "% Processor Utility": live per-core clocks and usage.</item>
/// </list>
/// Core voltage (VID), real BCLK on AMD and uncore clocks need MSR access and are reported as N/A.
/// </summary>
public sealed class CpuZService : ICpuZService, IDisposable
{
    private readonly object _gate = new();
    private PdhQuery? _pdh;
    private int _logical;
    private int _baseClockMHz;
    private double _busMHz = 100;

    public Task<CpuZSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var topology = ProcessorTopology.Read();
        var wmi = Wmi.Query(Wmi.Cimv2, "SELECT Name, SocketDesignation, MaxClockSpeed, VirtualizationFirmwareEnabled FROM Win32_Processor").FirstOrDefault();
        cancellationToken.ThrowIfCancellationRequested();

        var available = CpuIdDecoder.IsSupported;
        string vendor = "", brand = wmi?.Str("Name") ?? "";
        CpuSignature? sig = null;
        var instructions = new List<string>();
        int? baseMHz = wmi?.Int("MaxClockSpeed"), maxMHz = null;
        double? busMHz = null;
        var hypervisor = false;

        if (available)
        {
            vendor = CpuIdDecoder.ReadVendor();
            var cpuBrand = CpuIdDecoder.ReadBrand();
            if (!string.IsNullOrWhiteSpace(cpuBrand)) brand = cpuBrand;
            var maxLeaf = CpuIdDecoder.Query(0).Eax;
            var leaf1 = CpuIdDecoder.Query(1);
            var leaf7 = maxLeaf >= 7 ? CpuIdDecoder.Query(7) : default;
            var leaf7s1 = maxLeaf >= 7 && leaf7.Eax >= 1 ? CpuIdDecoder.Query(7, 1) : default;
            var ext1 = CpuIdDecoder.Query(0x80000000).Eax >= 0x80000001 ? CpuIdDecoder.Query(0x80000001) : default;
            sig = CpuIdDecoder.DecodeSignature(leaf1.Eax);
            instructions = CpuIdDecoder.DecodeInstructions(leaf1, leaf7, leaf7s1, ext1, vendor == "AuthenticAMD");
            hypervisor = CpuIdDecoder.HypervisorPresent(leaf1);

            if (maxLeaf >= 0x16)
            {
                var f = CpuIdDecoder.Query(0x16);
                if ((f.Eax & 0xFFFF) > 0) baseMHz = (int)(f.Eax & 0xFFFF);
                if ((f.Ebx & 0xFFFF) > 0) maxMHz = (int)(f.Ebx & 0xFFFF);
                if ((f.Ecx & 0xFFFF) > 0) busMHz = f.Ecx & 0xFFFF;
            }
        }

        // Under Hyper-V / VBS the VMX bit is hidden from the guest root partition; firmware flag tells the truth.
        var vtFirmware = wmi?.Bool("VirtualizationFirmwareEnabled");
        if (hypervisor && !instructions.Contains("VT-x") && !instructions.Contains("AMD-V"))
            instructions.Add(vendor == "AuthenticAMD" ? "AMD-V*" : "VT-x*");

        var (codeName, process) = sig is { } s ? CpuCodeNameCatalog.Lookup(vendor, s.Family, s.Model) : ("", "");

        lock (_gate)
        {
            _logical = topology.Threads;
            _baseClockMHz = baseMHz ?? 0;
            _busMHz = busMHz ?? 100;
        }

        return new CpuZSnapshot
        {
            CpuIdAvailable = available,
            Vendor = vendor switch { "GenuineIntel" => "Intel", "AuthenticAMD" => "AMD", "" => wmi?.Str("Name").Split(' ').FirstOrDefault() ?? "", _ => vendor },
            Name = ShortName(brand),
            Specification = brand,
            CodeName = codeName,
            ProcessNode = process,
            Package = wmi?.Str("SocketDesignation") ?? "",
            Family = sig?.Family,
            Model = sig?.Model,
            Stepping = sig?.Stepping,
            ExtFamily = sig?.ExtFamily,
            ExtModel = sig?.ExtModel,
            BaseFamily = sig?.BaseFamily,
            BaseModel = sig?.BaseModel,
            MicrocodeRevision = ReadMicrocode(),
            CoreVoltage = null,
            Instructions = instructions,
            Packages = topology.Packages,
            Cores = topology.Cores,
            Threads = topology.Threads,
            PerformanceCores = topology.PerformanceCores,
            EfficiencyCores = topology.EfficiencyCores,
            BaseClockMHz = baseMHz ?? 0,
            MaxClockMHz = maxMHz,
            BusClockMHz = busMHz ?? 100,
            BusClockEstimated = busMHz is null,
            HypervisorPresent = hypervisor,
            VirtualizationFirmwareEnabled = vtFirmware,
            Caches = topology.Caches,
        };
    }, cancellationToken);

    public CpuZLiveSample SampleLive()
    {
        lock (_gate)
        {
            if (_logical == 0) _logical = Environment.ProcessorCount;
            if (_pdh is null)
            {
                _pdh = new PdhQuery();
                _pdh.Add(@"\Processor Information(_Total)\% Processor Utility");
                _pdh.Add(@"\Processor Information(_Total)\% Processor Performance");
                for (var i = 0; i < _logical; i++)
                {
                    _pdh.Add(PerfPath(i));
                    _pdh.Add(UtilPath(i));
                }
                _pdh.Collect(); // rate counters need a first sample
                Thread.Sleep(100);
            }

            if (!_pdh.Collect()) return new CpuZLiveSample();

            var cores = new List<CoreSample>(_logical);
            for (var i = 0; i < _logical; i++)
            {
                var perf = _pdh.Read(PerfPath(i));
                var util = _pdh.Read(UtilPath(i));
                cores.Add(new CoreSample(i, perf is > 0 && _baseClockMHz > 0 ? _baseClockMHz * perf.Value / 100d : null, util is null ? null : Math.Clamp(util.Value, 0, 100)));
            }

            var totalPerf = _pdh.Read(@"\Processor Information(_Total)\% Processor Performance");
            var avg = totalPerf is > 0 && _baseClockMHz > 0 ? _baseClockMHz * totalPerf.Value / 100d : (double?)null;
            var freqs = cores.Where(c => c.FrequencyMHz is not null).Select(c => c.FrequencyMHz!.Value).ToList();
            return new CpuZLiveSample
            {
                UsagePercent = Math.Clamp(_pdh.Read(@"\Processor Information(_Total)\% Processor Utility") ?? 0, 0, 100),
                AverageMHz = avg,
                MaxMHz = freqs.Count > 0 ? freqs.Max() : avg,
                Multiplier = avg is null || _busMHz <= 0 ? null : Math.Round(avg.Value / _busMHz, 1),
                Cores = cores,
            };
        }
    }

    private static string PerfPath(int logical) => $@"\Processor Information({logical / 64},{logical % 64})\% Processor Performance";
    private static string UtilPath(int logical) => $@"\Processor Information({logical / 64},{logical % 64})\% Processor Utility";

    /// <summary>
    /// Light synthetic integer + floating-point workload. Score = millions of inner iterations per second × 10
    /// ("Zelora points"); only comparable between runs of Zelora Info.
    /// </summary>
    public async Task<CpuBenchmarkResult> RunBenchmarkAsync(TimeSpan durationPerPhase, IProgress<CpuBenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        var threads = Environment.ProcessorCount;
        var single = await RunPhaseAsync(1, durationPerPhase, p => progress?.Report(new CpuBenchmarkProgress(false, p * 50)), cancellationToken).ConfigureAwait(false);
        var multi = await RunPhaseAsync(threads, durationPerPhase, p => progress?.Report(new CpuBenchmarkProgress(true, 50 + p * 50)), cancellationToken).ConfigureAwait(false);
        progress?.Report(new CpuBenchmarkProgress(true, 100));
        return new CpuBenchmarkResult
        {
            SingleThreadScore = Math.Round(single, 0),
            MultiThreadScore = Math.Round(multi, 0),
            Threads = threads,
            DurationSeconds = durationPerPhase.TotalSeconds * 2,
        };
    }

    private static async Task<double> RunPhaseAsync(int threads, TimeSpan duration, Action<double> report, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var deadline = start + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        var workers = Enumerable.Range(0, threads)
            .Select(i => Task.Factory.StartNew(() => Workload(deadline, (ulong)i + 1, ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            .ToArray();
        var all = Task.WhenAll(workers);
        while (!all.IsCompleted)
        {
            await Task.WhenAny(all, Task.Delay(200, CancellationToken.None)).ConfigureAwait(false);
            report(Math.Clamp(Stopwatch.GetElapsedTime(start).TotalSeconds / duration.TotalSeconds, 0, 1));
        }
        var iterations = await all.ConfigureAwait(false);
        var seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        return iterations.Sum() / seconds / 1_000_000d * 10d;
    }

    private static long Workload(long deadline, ulong seed, CancellationToken ct)
    {
        var x = 0x9E3779B97F4A7C15UL ^ seed;
        var f = 1.0001;
        long iterations = 0;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            for (var i = 0; i < 8192; i++)
            {
                x ^= x << 13;
                x ^= x >> 7;
                x ^= x << 17;
                f = f * 1.0000001 + (x & 0xFF) * 1e-9;
                if (f > 1e6) f = 1.0001;
            }
            iterations += 8192;
            ct.ThrowIfCancellationRequested();
        }
        GC.KeepAlive(f);
        return iterations + (long)(x & 0); // keep x observable
    }

    private static string ShortName(string brand)
    {
        var s = brand;
        foreach (var noise in new[] { "(R)", "(TM)", "(tm)", " CPU", " Processor", " with Radeon Graphics" })
            s = s.Replace(noise, "", StringComparison.Ordinal);
        var at = s.IndexOf(" @", StringComparison.Ordinal);
        if (at > 0) s = s[..at];
        return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string? ReadMicrocode()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var v = k?.GetValue("Update Revision") ?? k?.GetValue("Update Signature");
            if (v is byte[] b && b.Length >= 8)
            {
                var rev = BitConverter.ToUInt32(b, 4);
                if (rev == 0) rev = BitConverter.ToUInt32(b, 0);
                return rev == 0 ? null : $"0x{rev:X}";
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _pdh?.Dispose();
            _pdh = null;
        }
    }
}
