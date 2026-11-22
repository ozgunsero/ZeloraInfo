// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Text;
using ZeloraInfo.Core.Disks;
using ZeloraInfo.Core.Settings;
using ZeloraInfo.Infrastructure.Disks;

namespace ZeloraInfo.Tests;

public class SmartParserTests
{
    /// <summary>Builds a SMART READ DATA sector with the given (id, current, worst, raw) entries.</summary>
    internal static byte[] AtaData(params (byte Id, byte Cur, byte Worst, ulong Raw)[] attrs)
    {
        var data = new byte[512];
        data[0] = 0x10; // revision
        for (var i = 0; i < attrs.Length; i++)
        {
            var o = 2 + i * 12;
            data[o] = attrs[i].Id;
            data[o + 3] = attrs[i].Cur;
            data[o + 4] = attrs[i].Worst;
            for (var b = 0; b < 6; b++) data[o + 5 + b] = (byte)(attrs[i].Raw >> (8 * b));
        }
        return data;
    }

    internal static byte[] AtaThresholds(params (byte Id, byte Th)[] ths)
    {
        var data = new byte[512];
        for (var i = 0; i < ths.Length; i++)
        {
            data[2 + i * 12] = ths[i].Id;
            data[2 + i * 12 + 1] = ths[i].Th;
        }
        return data;
    }

    [Fact]
    public void ParsesAtaAttributesWithThresholds()
    {
        var data = AtaData((0x05, 100, 100, 0), (0x09, 98, 98, 12345), (0xC2, 66, 40, 0x0000_0028_0012_0022));
        var th = AtaThresholds((0x05, 10), (0x09, 0));
        var attrs = SmartParsers.ParseAtaAttributes(data, th);

        Assert.Equal(3, attrs.Count);
        var poh = attrs.Single(a => a.Id == 0x09);
        Assert.Equal("Power-On Hours", poh.Name);
        Assert.Equal(12345UL, poh.RawValue);
        Assert.Equal("000000003039", poh.RawDisplay);
        Assert.Equal(10, attrs.Single(a => a.Id == 0x05).Threshold);
        Assert.Null(attrs.Single(a => a.Id == 0xC2).Threshold);
    }

    [Fact]
    public void ParsesNvmeHealthLog()
    {
        var log = new byte[512];
        log[0] = 0;                                              // critical warning
        BinaryPrimitives.WriteUInt16LittleEndian(log.AsSpan(1), 318); // 318 K = 45 °C
        log[3] = 100; log[4] = 10; log[5] = 3;                   // spare, threshold, % used
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(32), 2_000_000); // data units read
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(48), 1_000_000); // data units written
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(112), 421);      // power cycles
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(128), 5_000);    // power-on hours

        var disk = new DiskInfo { Index = 0, Bus = DiskBusKind.Nvme, Media = DiskMediaKind.Ssd };
        disk.Attributes = SmartParsers.ParseNvmeHealthLog(log);
        disk.SmartSource = SmartSource.Nvme;
        SmartHealthEvaluator.Evaluate(disk, new SmartSettings());

        Assert.Equal(HealthStatus.Good, disk.Health);
        Assert.Equal(45, disk.TemperatureC);
        Assert.Equal(97, disk.LifeRemainingPercent);
        Assert.Equal(5_000, disk.PowerOnHours);
        Assert.Equal(421, disk.PowerOnCount);
        Assert.Equal(1_000_000L * 512_000, disk.HostWritesBytes);
        Assert.Equal(2_000_000L * 512_000, disk.HostReadsBytes);
    }

    [Fact]
    public void NvmeUint128OverflowSaturates()
    {
        var log = new byte[512];
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(48 + 8), 1); // high qword set
        var attrs = SmartParsers.ParseNvmeHealthLog(log);
        Assert.Equal(ulong.MaxValue, attrs.Single(a => a.Id == SmartAttributeCatalog.Nvme.DataUnitsWritten).RawValue);
    }

    [Fact]
    public void ParsesAtaIdentifyStringsAndSataSpeed()
    {
        var id = new byte[512];
        WriteSwapped(id, 10, 20, "S3Z9NB0K123456");
        WriteSwapped(id, 23, 8, "RVT04B6Q");
        WriteSwapped(id, 27, 40, "Samsung SSD 860 EVO 500GB");
        BinaryPrimitives.WriteUInt16LittleEndian(id.AsSpan(76 * 2), 0b1110); // Gen1-3 supported
        BinaryPrimitives.WriteUInt16LittleEndian(id.AsSpan(77 * 2), 3 << 1); // negotiated Gen3
        BinaryPrimitives.WriteUInt16LittleEndian(id.AsSpan(217 * 2), 1);     // non-rotating

        var info = SmartParsers.ParseAtaIdentify(id);
        Assert.Equal("Samsung SSD 860 EVO 500GB", info.Model);
        Assert.Equal("S3Z9NB0K123456", info.Serial);
        Assert.Equal("RVT04B6Q", info.Firmware);
        Assert.Equal("SATA/600 | SATA/600", info.TransferMode);
        Assert.Equal(0, info.RotationRate);
    }

    private static void WriteSwapped(byte[] buffer, int word, int length, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text.PadRight(length));
        for (var i = 0; i < length; i += 2)
        {
            buffer[word * 2 + i] = bytes[i + 1];
            buffer[word * 2 + i + 1] = bytes[i];
        }
    }
}

public class SmartHealthEvaluatorTests
{
    private static DiskInfo Ata(params (byte Id, byte Cur, byte Worst, ulong Raw)[] attrs)
    {
        var disk = new DiskInfo { Index = 1, Bus = DiskBusKind.Sata, Media = DiskMediaKind.Hdd, SmartSource = SmartSource.Ata };
        disk.Attributes = SmartParsers.ParseAtaAttributes(SmartParserTests.AtaData(attrs), SmartParserTests.AtaThresholds((0x05, 36), (0x01, 6)));
        return disk;
    }

    [Fact]
    public void HealthyAtaDiskIsGoodAndSummaryValuesAreDerived()
    {
        var disk = Ata((0x05, 100, 100, 0), (0x09, 99, 99, 8760), (0x0C, 99, 99, 512), (0xC2, 64, 50, 36));
        SmartHealthEvaluator.Evaluate(disk, new SmartSettings());
        Assert.Equal(HealthStatus.Good, disk.Health);
        Assert.Equal(8760, disk.PowerOnHours);
        Assert.Equal(512, disk.PowerOnCount);
        Assert.Equal(36, disk.TemperatureC);
    }

    [Fact]
    public void ReallocatedSectorsGiveCaution()
    {
        var disk = Ata((0x05, 100, 100, 8));
        SmartHealthEvaluator.Evaluate(disk, new SmartSettings());
        Assert.Equal(HealthStatus.Caution, disk.Health);
        Assert.Contains("Health.Reason.Reallocated", disk.HealthReasons);
        Assert.Equal(AttributeStatus.Caution, disk.Attributes.Single(a => a.Id == 0x05).Status);
    }

    [Fact]
    public void ReallocatedCautionCanBeDisabled()
    {
        var disk = Ata((0x05, 100, 100, 8));
        SmartHealthEvaluator.Evaluate(disk, new SmartSettings { TreatReallocatedAsCaution = false });
        Assert.Equal(HealthStatus.Good, disk.Health);
    }

    [Fact]
    public void ThresholdExceededIsBad()
    {
        var disk = Ata((0x05, 30, 30, 2000));
        SmartHealthEvaluator.Evaluate(disk, new SmartSettings());
        Assert.Equal(HealthStatus.Bad, disk.Health);
        Assert.Equal(AttributeStatus.Bad, disk.Attributes.Single(a => a.Id == 0x05).Status);
    }

    [Fact]
    public void NvmeCriticalWarningIsBad()
    {
        var log = new byte[512];
        log[0] = 0x04; // reliability degraded
        var disk = new DiskInfo { Bus = DiskBusKind.Nvme, Media = DiskMediaKind.Ssd, SmartSource = SmartSource.Nvme, Attributes = SmartParsers.ParseNvmeHealthLog(log) };
        SmartHealthEvaluator.Evaluate(disk, new SmartSettings());
        Assert.Equal(HealthStatus.Bad, disk.Health);
    }

    [Fact]
    public void WindowsHealthIsUsedWithoutSmart()
    {
        var good = new DiskInfo { WindowsHealth = HealthStatus.Good };
        SmartHealthEvaluator.Evaluate(good, new SmartSettings());
        Assert.Equal(HealthStatus.Good, good.Health);

        var unknown = new DiskInfo();
        SmartHealthEvaluator.Evaluate(unknown, new SmartSettings());
        Assert.Equal(HealthStatus.Unknown, unknown.Health);

        var bad = new DiskInfo { SmartSource = SmartSource.StorageReliability, WindowsHealth = HealthStatus.Bad };
        SmartHealthEvaluator.Evaluate(bad, new SmartSettings());
        Assert.Equal(HealthStatus.Bad, bad.Health);
    }

    [Theory]
    [InlineData(null, TemperatureLevel.Unknown)]
    [InlineData(40, TemperatureLevel.Normal)]
    [InlineData(55, TemperatureLevel.Caution)]
    [InlineData(70, TemperatureLevel.Critical)]
    public void TemperatureLevels(int? celsius, TemperatureLevel expected)
        => Assert.Equal(expected, SmartHealthEvaluator.GetTemperatureLevel(celsius, new SmartSettings { CautionTemperatureC = 55, CriticalTemperatureC = 65 }));
}
