// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Microsoft.Win32;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.Hardware;
using ZeloraInfo.Infrastructure.Interop;

namespace ZeloraInfo.Infrastructure.Gpu;

/// <summary>
/// GPU-Z style adapter information without a kernel driver:
/// <list type="bullet">
/// <item>DXGI: name, PCI IDs, memory sizes, LUID, user-mode driver version.</item>
/// <item>D3D12: feature level, shader model, DXR / mesh shader / VRS / resource binding tiers, UMA (integrated).</item>
/// <item>D3DKMT (gdi32 WDDM thunks, the API Task Manager uses): WDDM version, HW scheduling, BIOS / architecture,
/// temperature, fan, power, core / memory clock — when the driver reports them.</item>
/// <item>PDH "GPU Engine" / "GPU Adapter Memory": per-engine utilization and VRAM usage.</item>
/// <item>Registry + PnP: driver provider / date / chip type, PCIe link speed and width.</item>
/// </list>
/// </summary>
public sealed class GpuService : IGpuService, IDisposable
{
    private const string EngineCounter = @"\GPU Engine(*)\Utilization Percentage";
    private const string DedicatedCounter = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const string SharedCounter = @"\GPU Adapter Memory(*)\Shared Usage";

    private readonly object _gate = new();
    private PdhQuery? _pdh;
    private List<long> _luids = [];

    public Task<IReadOnlyList<GpuAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => (IReadOnlyList<GpuAdapterInfo>)Scan(cancellationToken), cancellationToken);

    private List<GpuAdapterInfo> Scan(CancellationToken ct)
    {
        var dxgi = GpuInterop.EnumerateAdapters();
        var controllers = Wmi.Query(Wmi.Cimv2, "SELECT Name, PNPDeviceID, DriverVersion, DriverDate FROM Win32_VideoController");
        var registry = ReadDriverKeys();
        var result = new List<GpuAdapterInfo>();
        try
        {
            var hardware = dxgi.Where(a => !a.IsSoftware).ToList();
            var candidates = hardware.Count > 0 ? hardware : dxgi;
            var index = 0;
            foreach (var a in candidates.DistinctBy(a => a.Luid))
            {
                ct.ThrowIfCancellationRequested();
                var pciKey = $"VEN_{a.VendorId:X4}&DEV_{a.DeviceId:X4}";
                var subsysKey = $"SUBSYS_{a.SubSysId:X8}";
                var wmi = controllers.FirstOrDefault(c => c.Str("PNPDeviceID").Contains(pciKey, StringComparison.OrdinalIgnoreCase) && c.Str("PNPDeviceID").Contains(subsysKey, StringComparison.OrdinalIgnoreCase))
                          ?? controllers.FirstOrDefault(c => c.Str("PNPDeviceID").Contains(pciKey, StringComparison.OrdinalIgnoreCase))
                          ?? controllers.FirstOrDefault(c => c.Str("Name").Equals(a.Description, StringComparison.OrdinalIgnoreCase));
                var reg = registry.FirstOrDefault(r => r.MatchingDeviceId.Contains(pciKey, StringComparison.OrdinalIgnoreCase))
                          ?? registry.FirstOrDefault(r => r.Description.Equals(a.Description, StringComparison.OrdinalIgnoreCase));

                var caps = GpuInterop.QueryD3D12(a.Pointer);
                var kmt = QueryKmtStatic(a.Luid);
                var pnp = wmi?.Str("PNPDeviceID") ?? "";
                var link = GpuInterop.ReadPcieLink(pnp);

                var driverVersion = FirstNonEmpty(reg?.DriverVersion, wmi?.Str("DriverVersion"), a.UmdVersion);
                string? vendorVersion = a.VendorId switch
                {
                    0x10DE => GpuFormat.NvidiaVersion(driverVersion),
                    0x1002 => reg?.VendorVersion,
                    _ => null
                };
                var ultimate = caps is null ? (bool?)null
                    : caps.MaxFeatureLevel >= 0xC200 || (caps.RaytracingTier >= 11 && caps.MeshShaderTier >= 10 && caps.VrsTier >= 2 && caps.ShaderModel >= 0x65);

                result.Add(new GpuAdapterInfo
                {
                    Index = index++,
                    Name = a.Description,
                    Vendor = GpuFormat.VendorName(a.VendorId),
                    VendorId = a.VendorId,
                    DeviceId = a.DeviceId,
                    SubsystemId = a.SubSysId,
                    Revision = a.Revision,
                    Luid = a.Luid,
                    IsSoftware = a.IsSoftware,
                    IsIntegrated = caps?.Uma ?? kmt.Integrated,
                    DedicatedVideoMemory = reg?.MemorySize is > 0 and var mem && mem > a.DedicatedVideoMemory ? mem : a.DedicatedVideoMemory,
                    DedicatedSystemMemory = a.DedicatedSystemMemory,
                    SharedSystemMemory = a.SharedSystemMemory,
                    DriverVersion = driverVersion,
                    DriverVendorVersion = vendorVersion,
                    DriverDate = reg?.DriverDate ?? wmi?.Date("DriverDate"),
                    DriverProvider = reg?.Provider ?? "",
                    WddmVersion = kmt.Wddm,
                    HardwareSchedulingSupported = kmt.HwSchSupported,
                    HardwareSchedulingEnabled = kmt.HwSchEnabled,
                    ChipType = reg?.ChipType ?? "",
                    Architecture = kmt.Architecture,
                    BiosVersion = FirstNonEmpty(kmt.Bios, reg?.BiosString),
                    PnpDeviceId = pnp,
                    BusInterface = link is { } l ? GpuFormat.PcieLink(l.MaxSpeed, l.MaxWidth) : "",
                    CurrentLink = link is { } c ? GpuFormat.PcieLink(c.CurSpeed, c.CurWidth) : "",
                    MaxFeatureLevel = caps is null ? "" : GpuFormat.FeatureLevel(caps.MaxFeatureLevel),
                    ShaderModel = caps is null ? "" : GpuFormat.ShaderModel(caps.ShaderModel),
                    RaytracingTier = caps is null ? "" : GpuFormat.Tier(caps.RaytracingTier, "—"),
                    MeshShaderTier = caps is null ? "" : GpuFormat.Tier(caps.MeshShaderTier, "—"),
                    VariableShadingRateTier = caps is null ? "" : GpuFormat.SimpleTier(caps.VrsTier, "—"),
                    ResourceBindingTier = caps is null ? "" : GpuFormat.SimpleTier(caps.ResourceBindingTier, "—"),
                    DirectX12Ultimate = ultimate,
                    NodeCount = caps?.NodeCount,
                    MaxFanRpm = kmt.MaxFan,
                    TemperatureMaxC = kmt.TempMax,
                    TemperatureWarningC = kmt.TempWarn,
                    MaxMemoryBandwidth = kmt.MaxBandwidth,
                });
            }
        }
        finally
        {
            foreach (var a in dxgi) GpuInterop.Release(a.Pointer);
        }

        lock (_gate) _luids = result.Select(r => r.Luid).ToList();
        return result;
    }

    // ------------------------------------------------------------------ live

    public IReadOnlyList<GpuLiveSample> SampleLive()
    {
        lock (_gate)
        {
            if (_pdh is null)
            {
                _pdh = new PdhQuery();
                _pdh.Add(EngineCounter);
                _pdh.Add(DedicatedCounter);
                _pdh.Add(SharedCounter);
                _pdh.Collect(); // rate counters need two samples
                Thread.Sleep(120);
            }
            _pdh.Collect();

            var engines = GpuFormat.AggregateEngines(_pdh.ReadArray(EngineCounter));
            var dedicated = SumPerLuid(_pdh.ReadArray(DedicatedCounter));
            var shared = SumPerLuid(_pdh.ReadArray(SharedCounter));

            var samples = new List<GpuLiveSample>();
            foreach (var luid in _luids)
            {
                var key = GpuFormat.LuidKey(luid);
                engines.TryGetValue(key, out var e);
                var sensors = QueryKmtLive(luid);
                samples.Add(new GpuLiveSample
                {
                    Luid = luid,
                    UtilizationPercent = e.Overall,
                    Engines = (e.Engines ?? []).Select(x => new GpuEngineLoad { Name = x.Type, Percent = x.Percent }).ToList(),
                    DedicatedUsedBytes = dedicated.TryGetValue(key, out var d) ? (ulong)d : null,
                    SharedUsedBytes = shared.TryGetValue(key, out var s) ? (ulong)s : null,
                    TemperatureC = sensors.Temperature,
                    FanRpm = sensors.Fan,
                    PowerPercent = sensors.Power,
                    CoreClockMHz = sensors.Core,
                    MaxCoreClockMHz = sensors.MaxCore,
                    MemoryClockMHz = sensors.Mem,
                    MaxMemoryClockMHz = sensors.MaxMem,
                    VoltageV = sensors.Voltage,
                });
            }
            return samples;
        }
    }

    private static Dictionary<string, double> SumPerLuid(IEnumerable<(string Instance, double Value)> values)
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, value) in values)
        {
            if (GpuFormat.ParseInstance(instance) is not { } p) continue;
            map[p.LuidKey] = map.GetValueOrDefault(p.LuidKey) + Math.Max(0, value);
        }
        return map;
    }

    // ------------------------------------------------------------------ D3DKMT

    private sealed record KmtStatic(string Wddm, bool? HwSchSupported, bool? HwSchEnabled, bool? Integrated, string Architecture, string Bios,
        int? MaxFan, double? TempMax, double? TempWarn, ulong? MaxBandwidth);

    private static KmtStatic QueryKmtStatic(long luid)
    {
        var h = GpuInterop.OpenAdapter(luid);
        if (h == 0) return new KmtStatic("", null, null, null, "", "", null, null, null, null);
        try
        {
            Span<byte> b4 = stackalloc byte[4];
            var wddm = GpuInterop.Query(h, GpuInterop.KMTQAITYPE_DRIVERVERSION, b4) ? GpuFormat.Wddm(BinaryPrimitives.ReadInt32LittleEndian(b4)) : "";

            bool? hwsSupported = null, hwsEnabled = null;
            b4.Clear();
            if (GpuInterop.Query(h, GpuInterop.KMTQAITYPE_WDDM_2_7_CAPS, b4))
            {
                var v = BinaryPrimitives.ReadUInt32LittleEndian(b4);
                hwsSupported = (v & 1) != 0;
                hwsEnabled = (v & 2) != 0;
            }

            bool? integrated = null;
            b4.Clear();
            if (GpuInterop.Query(h, GpuInterop.KMTQAITYPE_ADAPTERTYPE, b4))
            {
                var v = BinaryPrimitives.ReadUInt32LittleEndian(b4);
                if ((v & (1 << 5)) != 0) integrated = true;      // HybridIntegrated
                else if ((v & (1 << 4)) != 0) integrated = false; // HybridDiscrete
            }

            // D3DKMT_GPUVERSION { UINT PhysicalAdapterIndex; WCHAR BiosVersion[32]; WCHAR GpuArchitecture[32]; }
            Span<byte> ver = stackalloc byte[4 + 64 + 64];
            string bios = "", arch = "";
            if (GpuInterop.Query(h, GpuInterop.KMTQAITYPE_GPUVERSION, ver))
            {
                bios = Encoding.Unicode.GetString(ver.Slice(4, 64)).TrimEnd('\0').Trim();
                arch = Encoding.Unicode.GetString(ver.Slice(68, 64)).TrimEnd('\0').Trim();
            }

            // D3DKMT_ADAPTERPERFDATACAPS
            Span<byte> caps = stackalloc byte[40];
            int? maxFan = null;
            double? tMax = null, tWarn = null;
            ulong? bw = null;
            if (GpuInterop.Query(h, GpuInterop.KMTQAITYPE_ADAPTERPERFDATA_CAPS, caps))
            {
                var mb = BinaryPrimitives.ReadUInt64LittleEndian(caps[8..]);
                var fan = BinaryPrimitives.ReadUInt32LittleEndian(caps[24..]);
                var tm = BinaryPrimitives.ReadUInt32LittleEndian(caps[28..]);
                var tw = BinaryPrimitives.ReadUInt32LittleEndian(caps[32..]);
                if (mb > 0) bw = mb;
                if (fan is > 0 and < 20000) maxFan = (int)fan;
                if (tm is > 0 and < 2000) tMax = tm / 10d;
                if (tw is > 0 and < 2000) tWarn = tw / 10d;
            }
            return new KmtStatic(wddm, hwsSupported, hwsEnabled, integrated, arch, bios, maxFan, tMax, tWarn, bw);
        }
        finally
        {
            GpuInterop.CloseAdapter(h);
        }
    }

    private sealed record KmtLive(double? Temperature, int? Fan, double? Power, double? Core, double? MaxCore, double? Mem, double? MaxMem, double? Voltage);

    private static KmtLive QueryKmtLive(long luid)
    {
        var h = GpuInterop.OpenAdapter(luid);
        if (h == 0) return new KmtLive(null, null, null, null, null, null, null, null);
        try
        {
            double? temp = null, power = null, mem = null, maxMem = null, core = null, maxCore = null, volt = null;
            int? fan = null;

            // D3DKMT_ADAPTERPERFDATA (physical adapter 0)
            Span<byte> perf = stackalloc byte[64];
            if (GpuInterop.Query(h, GpuInterop.KMTQAITYPE_ADAPTERPERFDATA, perf))
            {
                var memHz = BinaryPrimitives.ReadUInt64LittleEndian(perf[8..]);
                var maxMemHz = BinaryPrimitives.ReadUInt64LittleEndian(perf[16..]);
                var fanRpm = BinaryPrimitives.ReadUInt32LittleEndian(perf[48..]);
                var pwr = BinaryPrimitives.ReadUInt32LittleEndian(perf[52..]);
                var t = BinaryPrimitives.ReadUInt32LittleEndian(perf[56..]);
                if (memHz > 0) mem = memHz / 1_000_000d;
                if (maxMemHz > 0) maxMem = maxMemHz / 1_000_000d;
                if (fanRpm < 20000) fan = (int)fanRpm;
                if (pwr is > 0 and < 5000) power = pwr / 10d;
                if (t is > 0 and < 2000) temp = t / 10d;
            }

            // D3DKMT_NODE_PERFDATA for node 0 (the 3D / graphics engine on all major drivers)
            Span<byte> node = stackalloc byte[56];
            if (GpuInterop.Query(h, GpuInterop.KMTQAITYPE_NODEPERFDATA, node))
            {
                var hz = BinaryPrimitives.ReadUInt64LittleEndian(node[8..]);
                var maxHz = BinaryPrimitives.ReadUInt64LittleEndian(node[16..]);
                var mv = BinaryPrimitives.ReadUInt32LittleEndian(node[32..]);
                if (hz > 0) core = hz / 1_000_000d;
                if (maxHz > 0) maxCore = maxHz / 1_000_000d;
                if (mv is > 100 and < 3000) volt = mv / 1000d;
            }

            // A driver that reports no temperature also reports no fan: treat 0 RPM as "unknown" then.
            if (temp is null && fan == 0) fan = null;
            return new KmtLive(temp, fan, power, core, maxCore, mem, maxMem, volt);
        }
        finally
        {
            GpuInterop.CloseAdapter(h);
        }
    }

    // ------------------------------------------------------------------ registry

    private sealed record DriverKey(string MatchingDeviceId, string Description, string DriverVersion, DateTime? DriverDate, string Provider,
        string ChipType, string BiosString, ulong MemorySize, string? VendorVersion);

    private static List<DriverKey> ReadDriverKeys()
    {
        var list = new List<DriverKey>();
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return list;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k is null) continue;
                    DateTime? date = null;
                    if (k.GetValue("DriverDate") is string ds && DateTime.TryParseExact(ds, ["M-d-yyyy", "MM-dd-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        date = dt;
                    ulong memory = k.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long l when l > 0 => (ulong)l,
                        byte[] b when b.Length >= 8 => BitConverter.ToUInt64(b, 0),
                        _ => 0
                    };
                    list.Add(new DriverKey(
                        k.GetValue("MatchingDeviceId") as string ?? "",
                        k.GetValue("DriverDesc") as string ?? "",
                        k.GetValue("DriverVersion") as string ?? "",
                        date,
                        k.GetValue("ProviderName") as string ?? "",
                        RegistryText(k.GetValue("HardwareInformation.ChipType")),
                        RegistryText(k.GetValue("HardwareInformation.BiosString")),
                        memory,
                        k.GetValue("RadeonSoftwareVersion") as string));
                }
                catch (System.Security.SecurityException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
        }
        return list;
    }

    /// <summary>HardwareInformation.* values are REG_SZ or REG_BINARY holding UTF-16 text.</summary>
    internal static string RegistryText(object? value) => value switch
    {
        string s => s.Trim('\0', ' '),
        byte[] b => Encoding.Unicode.GetString(b).Trim('\0', ' '),
        string[] arr => string.Join(" ", arr).Trim(),
        _ => ""
    };

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    public void Dispose()
    {
        lock (_gate)
        {
            _pdh?.Dispose();
            _pdh = null;
        }
    }
}
