// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Text;
using ZeloraInfo.Core.Disks;

namespace ZeloraInfo.Infrastructure.Disks;

public sealed record AtaIdentifyInfo(string Model, string Serial, string Firmware, string? TransferMode, int? RotationRate);

/// <summary>Pure parsers for raw ATA / NVMe buffers (unit-testable without hardware).</summary>
public static class SmartParsers
{
    public const int SectorSize = 512;
    private const int AtaAttributeCount = 30;
    private const int AtaEntrySize = 12;

    /// <summary>Parses SMART READ DATA (+ optional SMART READ THRESHOLDS) 512-byte sectors.</summary>
    public static List<SmartAttribute> ParseAtaAttributes(ReadOnlySpan<byte> data, ReadOnlySpan<byte> thresholds)
    {
        var list = new List<SmartAttribute>();
        if (data.Length < 2 + AtaAttributeCount * AtaEntrySize) return list;

        var thresholdMap = new Dictionary<int, int>();
        if (thresholds.Length >= 2 + AtaAttributeCount * AtaEntrySize)
        {
            for (var i = 0; i < AtaAttributeCount; i++)
            {
                var e = thresholds.Slice(2 + i * AtaEntrySize, AtaEntrySize);
                if (e[0] != 0) thresholdMap[e[0]] = e[1];
            }
        }

        for (var i = 0; i < AtaAttributeCount; i++)
        {
            var e = data.Slice(2 + i * AtaEntrySize, AtaEntrySize);
            int id = e[0];
            if (id == 0) continue;
            // e[1..2] flags, e[3] current, e[4] worst, e[5..10] raw (little endian, 48-bit), e[11] reserved
            ulong raw = 0;
            for (var b = 5; b >= 0; b--) raw = (raw << 8) | e[5 + b];
            list.Add(new SmartAttribute
            {
                Id = id,
                Name = SmartAttributeCatalog.GetAtaName(id),
                Current = e[3],
                Worst = e[4],
                Threshold = thresholdMap.TryGetValue(id, out var th) ? th : null,
                RawValue = raw,
                RawDisplay = raw.ToString("X12"),
            });
        }
        return list;
    }

    /// <summary>Parses the NVMe SMART / Health Information log page (Log Identifier 02h).</summary>
    public static List<SmartAttribute> ParseNvmeHealthLog(ReadOnlySpan<byte> log)
    {
        var list = new List<SmartAttribute>();
        if (log.Length < SectorSize) return list;

        void Add(int id, ulong value) => list.Add(new SmartAttribute
        {
            Id = id,
            Name = SmartAttributeCatalog.Nvme.Names[id],
            RawValue = value,
            RawDisplay = value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
        });

        Add(SmartAttributeCatalog.Nvme.CriticalWarning, log[0]);
        Add(SmartAttributeCatalog.Nvme.CompositeTemperature, BinaryPrimitives.ReadUInt16LittleEndian(log[1..]));
        Add(SmartAttributeCatalog.Nvme.AvailableSpare, log[3]);
        Add(SmartAttributeCatalog.Nvme.AvailableSpareThreshold, log[4]);
        Add(SmartAttributeCatalog.Nvme.PercentageUsed, log[5]);
        Add(SmartAttributeCatalog.Nvme.DataUnitsRead, ReadUInt128Saturated(log.Slice(32, 16)));
        Add(SmartAttributeCatalog.Nvme.DataUnitsWritten, ReadUInt128Saturated(log.Slice(48, 16)));
        Add(SmartAttributeCatalog.Nvme.HostReadCommands, ReadUInt128Saturated(log.Slice(64, 16)));
        Add(SmartAttributeCatalog.Nvme.HostWriteCommands, ReadUInt128Saturated(log.Slice(80, 16)));
        Add(SmartAttributeCatalog.Nvme.ControllerBusyTime, ReadUInt128Saturated(log.Slice(96, 16)));
        Add(SmartAttributeCatalog.Nvme.PowerCycles, ReadUInt128Saturated(log.Slice(112, 16)));
        Add(SmartAttributeCatalog.Nvme.PowerOnHours, ReadUInt128Saturated(log.Slice(128, 16)));
        Add(SmartAttributeCatalog.Nvme.UnsafeShutdowns, ReadUInt128Saturated(log.Slice(144, 16)));
        Add(SmartAttributeCatalog.Nvme.MediaErrors, ReadUInt128Saturated(log.Slice(160, 16)));
        Add(SmartAttributeCatalog.Nvme.ErrorLogEntries, ReadUInt128Saturated(log.Slice(176, 16)));
        Add(SmartAttributeCatalog.Nvme.WarningTempTime, BinaryPrimitives.ReadUInt32LittleEndian(log[192..]));
        Add(SmartAttributeCatalog.Nvme.CriticalTempTime, BinaryPrimitives.ReadUInt32LittleEndian(log[196..]));
        return list;
    }

    /// <summary>Parses ATA IDENTIFY DEVICE data (strings are stored byte-swapped per word).</summary>
    public static AtaIdentifyInfo ParseAtaIdentify(ReadOnlySpan<byte> id)
    {
        if (id.Length < SectorSize) return new AtaIdentifyInfo("", "", "", null, null);
        var serial = SwapString(id.Slice(20, 20));
        var firmware = SwapString(id.Slice(46, 8));
        var model = SwapString(id.Slice(54, 40));

        string? transfer = null;
        var w76 = Word(id, 76);
        var w77 = Word(id, 77);
        if (w76 != 0x0000 && w76 != 0xFFFF)
        {
            var max = (w76 & 0x8) != 0 ? 3 : (w76 & 0x4) != 0 ? 2 : (w76 & 0x2) != 0 ? 1 : 0;
            var cur = (w77 >> 1) & 0x7;
            if (max > 0)
                transfer = $"{SataSpeed(cur == 0 ? max : cur)} | {SataSpeed(max)}";
        }

        int? rotation = null;
        var w217 = Word(id, 217);
        if (w217 == 1) rotation = 0; // non-rotating (SSD)
        else if (w217 is >= 0x0401 and <= 0xFFFE) rotation = w217;

        return new AtaIdentifyInfo(model, serial, firmware, transfer, rotation);
    }

    private static ushort Word(ReadOnlySpan<byte> data, int word) => BinaryPrimitives.ReadUInt16LittleEndian(data[(word * 2)..]);

    private static string SataSpeed(int gen) => gen switch
    {
        1 => "SATA/150",
        2 => "SATA/300",
        3 => "SATA/600",
        _ => "SATA"
    };

    private static string SwapString(ReadOnlySpan<byte> bytes)
    {
        var buf = new byte[bytes.Length];
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            buf[i] = bytes[i + 1];
            buf[i + 1] = bytes[i];
        }
        return Encoding.ASCII.GetString(buf).Trim('\0', ' ');
    }

    private static ulong ReadUInt128Saturated(ReadOnlySpan<byte> s)
    {
        var low = BinaryPrimitives.ReadUInt64LittleEndian(s);
        var high = BinaryPrimitives.ReadUInt64LittleEndian(s[8..]);
        return high != 0 ? ulong.MaxValue : low;
    }
}
