// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Text;
using ZeloraInfo.Infrastructure.Gpu;

namespace ZeloraInfo.Tests;

public class GpuTests
{
    [Theory]
    [InlineData(0x10DEu, "NVIDIA")]
    [InlineData(0x1002u, "AMD")]
    [InlineData(0x8086u, "Intel")]
    [InlineData(0x1414u, "Microsoft")]
    [InlineData(0xABCDu, "0xABCD")]
    public void VendorNames(uint id, string expected) => Assert.Equal(expected, GpuFormat.VendorName(id));

    [Fact]
    public void DirectXFormatting()
    {
        Assert.Equal("12_2", GpuFormat.FeatureLevel(0xC200));
        Assert.Equal("11_1", GpuFormat.FeatureLevel(0xB100));
        Assert.Equal("6.8", GpuFormat.ShaderModel(0x68));
        Assert.Equal("Tier 1.1", GpuFormat.Tier(11, "—"));
        Assert.Equal("—", GpuFormat.Tier(0, "—"));
        Assert.Equal("Tier 2", GpuFormat.SimpleTier(2, "—"));
        Assert.Equal("WDDM 3.2", GpuFormat.Wddm(3200));
        Assert.Equal("WDDM 2.7", GpuFormat.Wddm(2700));
        Assert.Equal("", GpuFormat.Wddm(0));
    }

    [Theory]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("32.0.16.2124", "621.24")]
    [InlineData("31.0.15.5222", "552.22")]
    [InlineData("not.a.version", null)]
    public void NvidiaVersions(string windows, string? expected) => Assert.Equal(expected, GpuFormat.NvidiaVersion(windows));

    [Fact]
    public void PcieLinks()
    {
        Assert.Equal("PCIe 4.0 x16", GpuFormat.PcieLink(4, 16));
        Assert.Equal("PCIe 3.0 x8", GpuFormat.PcieLink(3, 8));
        Assert.Equal("", GpuFormat.PcieLink(9, 16));
        Assert.Equal("", GpuFormat.PcieLink(4, 0));
    }

    [Fact]
    public void LuidKeyMatchesPdhInstanceFormat()
    {
        const long luid = 0x0000_0001_0000_E0DFL;
        Assert.Equal("0x00000001_0x0000E0DF", GpuFormat.LuidKey(luid));
        var parsed = GpuFormat.ParseInstance("pid_4242_luid_0x00000001_0x0000e0df_phys_0_eng_3_engtype_VideoDecode");
        Assert.NotNull(parsed);
        Assert.Equal(GpuFormat.LuidKey(luid), parsed!.Value.LuidKey);
        Assert.Equal(3, parsed.Value.Engine);
        Assert.Equal("VideoDecode", parsed.Value.EngineType);

        var memory = GpuFormat.ParseInstance("luid_0x00000000_0x0000E0DF_phys_0");
        Assert.NotNull(memory);
        Assert.Null(memory!.Value.Engine);
        Assert.Null(GpuFormat.ParseInstance("_Total"));
    }

    [Fact]
    public void EngineAggregationSumsProcessesAndTakesBusiestEngine()
    {
        var samples = new[]
        {
            ("pid_1_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 20.0),
            ("pid_2_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 15.0),   // same engine → summed = 35
            ("pid_2_luid_0x00000000_0x0000AAAA_phys_0_eng_1_engtype_Copy", 4.0),
            ("pid_3_luid_0x00000000_0x0000AAAA_phys_0_eng_5_engtype_VideoDecode", 50.0),
            ("pid_3_luid_0x00000000_0x0000BBBB_phys_0_eng_0_engtype_3D", 70.0),   // other adapter
            ("pid_4_luid_0x00000000_0x0000BBBB_phys_0_eng_0_engtype_3D", 60.0),   // capped at 100
        };
        var result = GpuFormat.AggregateEngines(samples);

        var a = result["0x00000000_0x0000AAAA"];
        Assert.Equal(50, a.Overall);
        Assert.Equal("3D", a.Engines[0].Type);
        Assert.Equal(35, a.Engines[0].Percent);
        Assert.Contains(a.Engines, e => e.Type == "Copy" && e.Percent == 4);

        Assert.Equal(100, result["0x00000000_0x0000BBBB"].Overall);
    }

    [Fact]
    public void RegistryTextHandlesBinaryAndStringValues()
    {
        Assert.Equal("NVIDIA GeForce RTX 3080 Ti", GpuService.RegistryText(Encoding.Unicode.GetBytes("NVIDIA GeForce RTX 3080 Ti\0")));
        Assert.Equal("Ampere", GpuService.RegistryText("Ampere "));
        Assert.Equal("", GpuService.RegistryText(null));
    }
}
