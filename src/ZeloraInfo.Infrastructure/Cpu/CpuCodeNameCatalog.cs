// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

namespace ZeloraInfo.Infrastructure.Cpu;

/// <summary>
/// Best-effort mapping of (vendor, family, model) to microarchitecture code name and manufacturing process.
/// Unknown combinations return empty strings (shown as N/A) instead of a guess.
/// </summary>
public static class CpuCodeNameCatalog
{
    private static readonly Dictionary<(int Family, int Model), (string Name, string Process)> Intel = new()
    {
        [(6, 0x2A)] = ("Sandy Bridge", "32 nm"),
        [(6, 0x2D)] = ("Sandy Bridge-E", "32 nm"),
        [(6, 0x3A)] = ("Ivy Bridge", "22 nm"),
        [(6, 0x3E)] = ("Ivy Bridge-E", "22 nm"),
        [(6, 0x3C)] = ("Haswell", "22 nm"),
        [(6, 0x3F)] = ("Haswell-E", "22 nm"),
        [(6, 0x45)] = ("Haswell-ULT", "22 nm"),
        [(6, 0x46)] = ("Crystal Well", "22 nm"),
        [(6, 0x3D)] = ("Broadwell", "14 nm"),
        [(6, 0x47)] = ("Broadwell-H", "14 nm"),
        [(6, 0x4F)] = ("Broadwell-E", "14 nm"),
        [(6, 0x4E)] = ("Skylake-U/Y", "14 nm"),
        [(6, 0x5E)] = ("Skylake-S/H", "14 nm"),
        [(6, 0x55)] = ("Skylake-SP / Cascade Lake", "14 nm"),
        [(6, 0x8E)] = ("Kaby / Whiskey / Amber / Comet Lake-U", "14 nm"),
        [(6, 0x9E)] = ("Kaby Lake / Coffee Lake", "14 nm"),
        [(6, 0xA5)] = ("Comet Lake-S/H", "14 nm"),
        [(6, 0xA6)] = ("Comet Lake-U", "14 nm"),
        [(6, 0xA7)] = ("Rocket Lake", "14 nm"),
        [(6, 0x66)] = ("Cannon Lake", "10 nm"),
        [(6, 0x7D)] = ("Ice Lake", "10 nm"),
        [(6, 0x7E)] = ("Ice Lake", "10 nm"),
        [(6, 0x6A)] = ("Ice Lake-SP", "10 nm"),
        [(6, 0x6C)] = ("Ice Lake-SP", "10 nm"),
        [(6, 0x8C)] = ("Tiger Lake-UP3/UP4", "10 nm SuperFin"),
        [(6, 0x8D)] = ("Tiger Lake-H", "10 nm SuperFin"),
        [(6, 0x8F)] = ("Sapphire Rapids", "Intel 7"),
        [(6, 0xCF)] = ("Emerald Rapids", "Intel 7"),
        [(6, 0x97)] = ("Alder Lake-S", "Intel 7"),
        [(6, 0x9A)] = ("Alder Lake-P/H/U", "Intel 7"),
        [(6, 0xBE)] = ("Alder Lake-N", "Intel 7"),
        [(6, 0xB7)] = ("Raptor Lake-S", "Intel 7"),
        [(6, 0xBA)] = ("Raptor Lake-P", "Intel 7"),
        [(6, 0xBF)] = ("Raptor Lake-S", "Intel 7"),
        [(6, 0xAA)] = ("Meteor Lake", "Intel 4"),
        [(6, 0xAC)] = ("Meteor Lake", "Intel 4"),
        [(6, 0xBD)] = ("Lunar Lake", "TSMC N3B"),
        [(6, 0xC5)] = ("Arrow Lake-H", "TSMC N3B"),
        [(6, 0xC6)] = ("Arrow Lake-S", "TSMC N3B"),
        [(6, 0xB5)] = ("Arrow Lake-U", "Intel 3"),
        [(6, 0xCC)] = ("Panther Lake", "Intel 18A"),
        [(6, 0xAD)] = ("Granite Rapids", "Intel 3"),
        [(6, 0xAF)] = ("Sierra Forest", "Intel 3"),
        [(6, 0x9C)] = ("Jasper Lake", "10 nm"),
        [(6, 0x96)] = ("Elkhart Lake", "10 nm"),
        [(6, 0x7A)] = ("Gemini Lake", "14 nm"),
        [(6, 0x5C)] = ("Apollo Lake", "14 nm"),
    };

    private static readonly Dictionary<(int Family, int Model), (string Name, string Process)> Amd = new()
    {
        [(0x17, 0x01)] = ("Summit Ridge / Naples (Zen)", "14 nm"),
        [(0x17, 0x08)] = ("Pinnacle Ridge (Zen+)", "12 nm"),
        [(0x17, 0x11)] = ("Raven Ridge (Zen)", "14 nm"),
        [(0x17, 0x18)] = ("Picasso (Zen+)", "12 nm"),
        [(0x17, 0x20)] = ("Dalí (Zen)", "14 nm"),
        [(0x17, 0x31)] = ("Rome / Castle Peak (Zen 2)", "7 nm"),
        [(0x17, 0x47)] = ("Zen 2", "7 nm"),
        [(0x17, 0x60)] = ("Renoir (Zen 2)", "7 nm"),
        [(0x17, 0x68)] = ("Lucienne (Zen 2)", "7 nm"),
        [(0x17, 0x71)] = ("Matisse (Zen 2)", "7 nm"),
        [(0x17, 0x90)] = ("Van Gogh (Zen 2)", "7 nm"),
        [(0x17, 0xA0)] = ("Mendocino (Zen 2)", "6 nm"),
        [(0x19, 0x01)] = ("Milan (Zen 3)", "7 nm"),
        [(0x19, 0x08)] = ("Chagall (Zen 3)", "7 nm"),
        [(0x19, 0x11)] = ("Genoa (Zen 4)", "5 nm"),
        [(0x19, 0x18)] = ("Storm Peak (Zen 4)", "5 nm"),
        [(0x19, 0x21)] = ("Vermeer (Zen 3)", "7 nm"),
        [(0x19, 0x40)] = ("Rembrandt (Zen 3+)", "6 nm"),
        [(0x19, 0x44)] = ("Rembrandt-R (Zen 3+)", "6 nm"),
        [(0x19, 0x50)] = ("Cezanne / Barcelo (Zen 3)", "7 nm"),
        [(0x19, 0x61)] = ("Raphael (Zen 4)", "5 nm"),
        [(0x19, 0x74)] = ("Phoenix (Zen 4)", "4 nm"),
        [(0x19, 0x75)] = ("Phoenix / Hawk Point (Zen 4)", "4 nm"),
        [(0x19, 0x78)] = ("Phoenix 2 (Zen 4/4c)", "4 nm"),
        [(0x19, 0xA0)] = ("Bergamo (Zen 4c)", "5 nm"),
        [(0x1A, 0x02)] = ("Turin (Zen 5)", "4 nm"),
        [(0x1A, 0x11)] = ("Turin Dense (Zen 5c)", "3 nm"),
        [(0x1A, 0x24)] = ("Strix Point (Zen 5)", "4 nm"),
        [(0x1A, 0x44)] = ("Granite Ridge (Zen 5)", "4 nm"),
        [(0x1A, 0x60)] = ("Krackan Point (Zen 5)", "4 nm"),
        [(0x1A, 0x70)] = ("Strix Halo (Zen 5)", "4 nm"),
    };

    public static (string CodeName, string Process) Lookup(string vendor, int family, int model)
    {
        var table = vendor switch
        {
            "GenuineIntel" => Intel,
            "AuthenticAMD" => Amd,
            _ => null
        };
        return table is not null && table.TryGetValue((family, model), out var v) ? v : ("", "");
    }
}
