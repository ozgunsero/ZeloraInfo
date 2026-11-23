// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Globalization;
using System.Text.RegularExpressions;

namespace ZeloraInfo.Infrastructure.Gpu;

/// <summary>Pure decoding helpers for GPU data (unit-testable without hardware).</summary>
public static partial class GpuFormat
{
    public static string VendorName(uint vendorId) => vendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 or 0x1022 => "AMD",
        0x8086 => "Intel",
        0x1414 => "Microsoft",
        0x5143 => "Qualcomm",
        0x13B5 => "ARM",
        0x1AE0 => "Google",
        0x15AD => "VMware",
        0x80EE => "Oracle (VirtualBox)",
        0x1AF4 => "Red Hat (VirtIO)",
        0x1234 => "QEMU",
        _ => vendorId == 0 ? "" : $"0x{vendorId:X4}"
    };

    /// <summary>D3D_FEATURE_LEVEL (e.g. 0xC200) → "12_2".</summary>
    public static string FeatureLevel(int level) => level <= 0 ? "" : $"{(level >> 12) & 0xF}_{(level >> 8) & 0xF}";

    /// <summary>D3D_SHADER_MODEL (e.g. 0x66) → "6.6".</summary>
    public static string ShaderModel(int sm) => sm <= 0 ? "" : $"{(sm >> 4) & 0xF}.{sm & 0xF}";

    /// <summary>D3D12 "tier" enums use 0 = not supported, 10 = 1.0, 11 = 1.1 …</summary>
    public static string Tier(int tier, string notSupported) => tier <= 0 ? notSupported : $"Tier {tier / 10}.{tier % 10}";

    /// <summary>Plain 1-based tiers (resource binding, VRS: 1, 2, 3).</summary>
    public static string SimpleTier(int tier, string notSupported) => tier <= 0 ? notSupported : $"Tier {tier}";

    /// <summary>D3DKMT_DRIVERVERSION (e.g. 3100) → "WDDM 3.1".</summary>
    public static string Wddm(int version) => version < 1000 ? "" : $"WDDM {version / 1000}.{version % 1000 / 100}";

    /// <summary>NVIDIA user-facing driver version from the Windows one: 32.0.15.6094 → 560.94.</summary>
    public static string? NvidiaVersion(string windowsVersion)
    {
        var parts = windowsVersion.Split('.');
        if (parts.Length != 4) return null;
        var digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5 || !digits.All(char.IsDigit)) return null;
        var last5 = digits[^5..];
        return $"{int.Parse(last5[..3], CultureInfo.InvariantCulture)}.{last5[3..]}";
    }

    /// <summary>PCIe DEVPKEY link speed (1 = 2.5 GT/s … 6 = 64 GT/s) → generation text ("4.0").</summary>
    public static string? PcieGeneration(uint speed) => speed switch
    {
        1 => "1.1",
        2 => "2.0",
        3 => "3.0",
        4 => "4.0",
        5 => "5.0",
        6 => "6.0",
        _ => null
    };

    public static string PcieLink(uint speed, uint width)
    {
        var gen = PcieGeneration(speed);
        return gen is null || width is 0 or > 32 ? "" : $"PCIe {gen} x{width}";
    }

    /// <summary>LUID as it appears in PDH instance names: "0x00000000_0x0000F1A2".</summary>
    public static string LuidKey(long luid) => $"0x{(uint)(luid >> 32):X8}_0x{(uint)(luid & 0xFFFFFFFF):X8}";

    [GeneratedRegex(@"luid_(0x[0-9A-Fa-f]{8}_0x[0-9A-Fa-f]{8})_phys_(\d+)(?:_eng_(\d+)_engtype_(.*))?$")]
    private static partial Regex InstanceRegex();

    /// <summary>
    /// Parses a "GPU Engine" / "GPU Adapter Memory" PDH instance name:
    /// "pid_1234_luid_0x00000000_0x0000F1A2_phys_0_eng_3_engtype_VideoDecode" → (luid, engine index, engine type).
    /// </summary>
    public static (string LuidKey, int? Engine, string? EngineType)? ParseInstance(string instance)
    {
        var m = InstanceRegex().Match(instance);
        if (!m.Success) return null;
        int? engine = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : null;
        var type = m.Groups[4].Success ? m.Groups[4].Value.Trim() : null;
        return (m.Groups[1].Value.ToUpperInvariant().Replace("0X", "0x"), engine, string.IsNullOrEmpty(type) ? (engine is null ? null : "Engine " + engine) : type);
    }

    /// <summary>
    /// Task-Manager-style aggregation: sum each engine over all processes, report every engine type by its busiest
    /// engine, and the adapter utilization as the busiest engine overall.
    /// </summary>
    public static Dictionary<string, (double Overall, List<(string Type, double Percent)> Engines)> AggregateEngines(IEnumerable<(string Instance, double Value)> samples)
    {
        var perEngine = new Dictionary<(string Luid, int Engine), (string Type, double Sum)>();
        foreach (var (instance, value) in samples)
        {
            if (ParseInstance(instance) is not { Engine: { } engine } parsed) continue;
            var key = (parsed.LuidKey, engine);
            perEngine.TryGetValue(key, out var acc);
            perEngine[key] = (parsed.EngineType ?? acc.Type ?? "", acc.Sum + Math.Max(0, value));
        }

        var result = new Dictionary<string, (double, List<(string, double)>)>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in perEngine.GroupBy(e => e.Key.Luid))
        {
            var byType = group
                .GroupBy(e => e.Value.Type, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Type: g.Key, Percent: Math.Min(100, g.Max(x => x.Value.Sum))))
                .OrderByDescending(t => t.Type.Equals("3D", StringComparison.OrdinalIgnoreCase))
                .ThenBy(t => t.Type, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result[group.Key] = (byType.Count == 0 ? 0 : byType.Max(t => t.Percent), byType);
        }
        return result;
    }
}
