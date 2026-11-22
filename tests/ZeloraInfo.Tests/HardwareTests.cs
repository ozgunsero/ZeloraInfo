// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Text;
using ZeloraInfo.Core.Formatting;
using ZeloraInfo.Core.Hardware;
using ZeloraInfo.Core.Licensing;
using ZeloraInfo.Infrastructure.Cpu;
using ZeloraInfo.Infrastructure.Memory;
using ZeloraInfo.Infrastructure.SystemInfo;

namespace ZeloraInfo.Tests;

public class CpuTests
{
    [Fact]
    public void DecodesAmdZen3Signature()
    {
        // Ryzen 7 5700X3D: CPUID.01h:EAX = 0x00A20F12 → family 19h, model 21h, stepping 2
        var s = CpuIdDecoder.DecodeSignature(0x00A20F12);
        Assert.Equal(0x19, s.Family);
        Assert.Equal(0x21, s.Model);
        Assert.Equal(2, s.Stepping);
        Assert.Equal(0xA, s.ExtFamily);
        Assert.Equal(("Vermeer (Zen 3)", "7 nm"), CpuCodeNameCatalog.Lookup("AuthenticAMD", s.Family, s.Model));
    }

    [Fact]
    public void DecodesIntelSignature()
    {
        // Core i7-8700K: 0x000906EA → family 6, model 9Eh, stepping 10
        var s = CpuIdDecoder.DecodeSignature(0x000906EA);
        Assert.Equal(6, s.Family);
        Assert.Equal(0x9E, s.Model);
        Assert.Equal(10, s.Stepping);
        Assert.Equal("14 nm", CpuCodeNameCatalog.Lookup("GenuineIntel", 6, 0x9E).Process);
        Assert.Equal(("", ""), CpuCodeNameCatalog.Lookup("GenuineIntel", 6, 0x01));
    }

    [Fact]
    public void DecodesInstructionSets()
    {
        var leaf1 = new CpuIdRegs(0, 0, (1u << 0) | (1u << 9) | (1u << 19) | (1u << 20) | (1u << 25) | (1u << 28) | (1u << 12), (1u << 23) | (1u << 25) | (1u << 26));
        var leaf7 = new CpuIdRegs(0, (1u << 5) | (1u << 3) | (1u << 8) | (1u << 29), 0, 0);
        var ext = new CpuIdRegs(0, 0, 1u << 2, 1u << 29);
        var list = CpuIdDecoder.DecodeInstructions(leaf1, leaf7, default, ext, isAmd: true);
        foreach (var expected in new[] { "MMX", "SSE", "SSE2", "SSE3", "SSSE3", "SSE4.1", "SSE4.2", "AES", "AVX", "AVX2", "FMA3", "BMI1", "BMI2", "SHA", "x86-64", "AMD-V" })
            Assert.Contains(expected, list);
        Assert.DoesNotContain("AVX-512F", list);
    }

    [Fact]
    public void ParsesProcessorTopologyBuffer()
    {
        var records = new List<byte[]>
        {
            Core(efficiency: 1, mask: 0b11),      // P-core with SMT
            Core(efficiency: 1, mask: 0b1100),    // P-core with SMT
            Core(efficiency: 0, mask: 0b10000),   // E-core
            Cache(level: 1, type: 2, size: 48 * 1024, assoc: 12, mask: 0b11),
            Cache(level: 1, type: 2, size: 48 * 1024, assoc: 12, mask: 0b1100),
            Cache(level: 3, type: 0, size: 30 * 1024 * 1024, assoc: 12, mask: 0b11111),
            Package(),
        };
        var info = ProcessorTopology.Parse(records.SelectMany(r => r).ToArray());

        Assert.Equal(1, info.Packages);
        Assert.Equal(3, info.Cores);
        Assert.Equal(5, info.Threads);
        Assert.Equal(2, info.PerformanceCores);
        Assert.Equal(1, info.EfficiencyCores);
        var l1d = info.Caches.Single(c => c.Level == 1);
        Assert.Equal("L1d", l1d.Label);
        Assert.Equal(2, l1d.Count);
        Assert.Equal(2, l1d.SharedByThreads);
        Assert.Equal(5, info.Caches.Single(c => c.Level == 3).SharedByThreads);
    }

    private static byte[] Core(byte efficiency, ulong mask)
    {
        var r = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(r, 0);
        BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(4), r.Length);
        r[9] = efficiency;
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(30), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(32), mask);
        return r;
    }

    private static byte[] Cache(byte level, int type, uint size, byte assoc, ulong mask)
    {
        var r = new byte[56];
        BinaryPrimitives.WriteInt32LittleEndian(r, 2);
        BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(4), r.Length);
        r[8] = level;
        r[9] = assoc;
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(10), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(12), size);
        BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(16), type);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(38), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(r.AsSpan(40), mask);
        return r;
    }

    private static byte[] Package()
    {
        var r = Core(0, 0b11111);
        BinaryPrimitives.WriteInt32LittleEndian(r, 3);
        return r;
    }
}

public class SmbiosTests
{
    /// <summary>Builds a type-17 Memory Device structure (SMBIOS 3.3 layout, 0x5C bytes) plus its strings.</summary>
    private static byte[] Type17(ushort sizeMb, byte type, ushort speed, ushort configured, byte rank, ushort mv, params string[] strings)
    {
        var f = new byte[0x5C];
        f[0] = 17;
        f[1] = (byte)f.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0x08), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0x0A), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0x0C), sizeMb);
        f[0x0E] = 0x09; // DIMM
        f[0x10] = 1;    // device locator string #1
        f[0x11] = 2;    // bank locator string #2
        f[0x12] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0x13), 1 << 14); // unbuffered
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0x15), speed);
        f[0x17] = 3; f[0x18] = 4; f[0x1A] = 5; // manufacturer, serial, part number
        f[0x1B] = rank;
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0x20), configured);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0x26), mv);
        f[0x28] = 3; // DRAM
        var tail = strings.Length == 0 ? new byte[] { 0, 0 } : Encoding.ASCII.GetBytes(string.Join('\0', strings) + "\0\0");
        return [.. f, .. tail];
    }

    [Fact]
    public void ParsesPopulatedAndEmptySlots()
    {
        var table = Type17(16384, 0x1A, 3600, 3600, 2, 1200, "DIMM_A2", "BANK 1", "Kingston", "ABCD1234", "KF436C18BB/16")
            .Concat(Type17(0, 0, 0, 0, 0, 0, "DIMM_A1", "BANK 0"))
            .Concat(new byte[] { 127, 4, 0xFF, 0xFF, 0, 0 })
            .ToArray();
        var raw = new byte[8 + table.Length];
        raw[1] = 3; raw[2] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), (uint)table.Length);
        table.CopyTo(raw, 8);

        var parsed = SmbiosParser.ParseRaw(raw);
        Assert.Equal("3.3", parsed.Version);
        var slots = parsed.Structures.Where(s => s.Type == 17).Select(RamZService.MapType17).ToList();
        Assert.Equal(2, slots.Count);

        var a2 = slots[0];
        Assert.True(a2.IsPopulated);
        Assert.Equal(16UL * 1024 * 1024 * 1024, a2.CapacityBytes);
        Assert.Equal("DDR4", a2.MemoryType);
        Assert.Equal("DIMM", a2.FormFactor);
        Assert.Equal("Unbuffered", a2.TypeDetail);
        Assert.Equal(3600, a2.RatedSpeedMTs);
        Assert.Equal(3600, a2.ConfiguredSpeedMTs);
        Assert.Equal(2, a2.Rank);
        Assert.Equal(1.2, a2.ConfiguredVoltage);
        Assert.Equal("Kingston", a2.Manufacturer);
        Assert.Equal("KF436C18BB/16", a2.PartNumber);
        Assert.Equal("DIMM_A2", a2.Locator);

        Assert.False(slots[1].IsPopulated);
        Assert.Equal("DIMM_A1", slots[1].Locator);
    }

    [Theory]
    [InlineData("ChannelA-DIMM0", "BANK 0", "A")]
    [InlineData("DIMM_B2", "", "B")]
    [InlineData("DIMM 1", "P0 CHANNEL B", "0-B")]                 // typical AMD AM4/AM5 labels
    [InlineData("Controller1-ChannelB-DIMM1", "BANK 0", "1-B")]  // typical Intel labels
    public void InfersChannelFromLabels(string locator, string bank, string expected)
    {
        var slot = RamZService.WithChannel(new RamSlot { Locator = locator, Bank = bank, IsPopulated = true });
        Assert.Equal(expected, slot.Channel);
    }

    [Fact]
    public void ChannelModeFromDistinctChannels()
    {
        var slots = new[] { new RamSlot { IsPopulated = true, Channel = "A" }, new RamSlot { IsPopulated = true, Channel = "B" } };
        Assert.Equal(("Dual", 2, false), RamZService.InferChannelMode(slots));
        Assert.Equal(("Single", 1, true), RamZService.InferChannelMode([new RamSlot { IsPopulated = true }]));
    }
}

public class FormattingTests
{
    [Theory]
    [InlineData(1920, 1080, "16:9")]
    [InlineData(2560, 1440, "16:9")]
    [InlineData(1366, 768, "16:9")]
    [InlineData(3440, 1440, "21:9")]
    [InlineData(1920, 1200, "16:10")]
    [InlineData(1024, 768, "4:3")]
    [InlineData(2560, 1080, "21:9")]
    [InlineData(5120, 1440, "32:9")]
    [InlineData(1280, 1024, "5:4")]
    public void AspectRatios(int w, int h, string expected) => Assert.Equal(expected, AspectRatioHelper.Describe(w, h));

    [Fact]
    public void BytesAndTemperatures()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal("1.0 TB", ByteFormatter.FormatDecimal(1_000_204_886_016, 1, inv));
        Assert.Equal("16.0 GiB", ByteFormatter.FormatBinary(16L * 1024 * 1024 * 1024, 1, inv));
        Assert.Equal("512 B", ByteFormatter.FormatDecimal(512, 1, inv));
        Assert.Equal(212, TemperatureFormatter.CelsiusToFahrenheit(100));
        Assert.Equal("45 °C", TemperatureFormatter.Format(45, Core.Settings.TemperatureUnit.Celsius));
        Assert.Equal("113 °F", TemperatureFormatter.Format(45, Core.Settings.TemperatureUnit.Fahrenheit));
        Assert.Equal("—", TemperatureFormatter.Format(null, Core.Settings.TemperatureUnit.Celsius));
    }

    [Fact]
    public void SerialMasking()
    {
        Assert.Equal("••••34CD", SerialMasker.Mask("12AB34CD"));
        Assert.Equal("•••", SerialMasker.Mask("ABC"));
        Assert.Equal("—", SerialMasker.Mask(" "));
    }

    [Fact]
    public void MonitorInstanceNormalization()
    {
        Assert.Equal(@"DISPLAY\DEL40B5\5&1A2B&0&UID4352",
            DisplayInfoService.NormalizeInstance(@"\\?\DISPLAY#DEL40B5#5&1a2b&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"));
        Assert.Equal(@"DISPLAY\DEL40B5\5&1A2B&0&UID4352", DisplayInfoService.NormalizeInstance(@"DISPLAY\DEL40B5\5&1a2b&0&UID4352_0"));
    }
}

public class ThirdPartyManifestTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ZeloraInfo.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
        }
    }

    [Fact]
    public void ManifestParsesAndEveryComponentIsComplete()
    {
        var manifest = ThirdPartyManifest.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "third-party-licenses.json")));
        Assert.NotEmpty(manifest.Components);
        foreach (var c in manifest.Components)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Version), c.Name);
            Assert.False(string.IsNullOrWhiteSpace(c.License), c.Name);
            Assert.StartsWith("https://", c.ProjectUrl);
            Assert.True(c.LicenseText.Length > 500, $"{c.Name} needs the full license text");
            foreach (var lang in Core.Localization.LanguageCatalog.SupportedLanguages)
                Assert.True(c.Purpose.ContainsKey(lang), $"{c.Name} purpose missing [{lang}]");
        }
    }

    /// <summary>Every PackageReference in the solution must be listed with the same version.</summary>
    [Fact]
    public void ManifestCoversEveryPackageReference()
    {
        var manifest = ThirdPartyManifest.Parse(File.ReadAllText(Path.Combine(RepoRoot, "third-party-licenses.json")));
        var projects = Directory.GetFiles(RepoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        var refs = projects.SelectMany(p => System.Xml.Linq.XDocument.Load(p).Descendants("PackageReference"))
            .Select(e => (Name: (string)e.Attribute("Include")!, Version: (string?)e.Attribute("Version")))
            .Distinct();
        foreach (var (name, version) in refs)
        {
            var c = manifest.Components.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Assert.True(c is not null, $"Package '{name}' is missing from third-party-licenses.json");
            Assert.Equal(version, c!.Version);
        }
    }

    [Fact]
    public void FilterMatchesNameLicenseAndVersion()
    {
        var c = new ThirdPartyComponent { Name = "CommunityToolkit.Mvvm", License = "MIT", Version = "8.4.0" };
        Assert.True(c.Matches("toolkit"));
        Assert.True(c.Matches("mit"));
        Assert.True(c.Matches("8.4"));
        Assert.False(c.Matches("apache"));
    }
}
