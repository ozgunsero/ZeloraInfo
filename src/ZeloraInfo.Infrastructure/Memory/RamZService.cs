// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Text.RegularExpressions;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.Hardware;
using ZeloraInfo.Infrastructure.SystemInfo;

namespace ZeloraInfo.Infrastructure.Memory;

/// <summary>
/// Memory details from the raw SMBIOS table (types 16 / 17, incl. empty slots, rank, voltages, technology),
/// falling back to WMI when the firmware table is unavailable. Live usage and the page file come from
/// <see cref="MemoryInfoService"/>. DRAM timings need SPD/IMC access through a kernel driver and stay N/A.
/// </summary>
public sealed partial class RamZService(MemoryInfoService memory) : IRamZService
{
    public Core.SystemInfo.MemoryStatus GetStatus() => memory.GetStatus();

    public async Task<RamZSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var basic = await memory.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var table = await Task.Run(SmbiosParser.ReadFromFirmware, cancellationToken).ConfigureAwait(false);

        List<RamSlot> slots;
        int? totalSlots = basic.TotalSlots;
        ulong? maxCapacity = basic.MaxCapacityBytes;
        bool? ecc = null;
        string source;

        if (table is not null && table.Structures.Any(s => s.Type == 17))
        {
            source = $"SMBIOS {table.Version}";
            slots = table.Structures.Where(s => s.Type == 17).Select(MapType17).ToList();
            var arrays = table.Structures.Where(s => s.Type == 16 && (s.Byte(0x05) == 3 || s.Byte(0x05) == 0)).ToList();
            if (arrays.Count > 0)
            {
                totalSlots = arrays.Sum(a => a.Word(0x0D));
                ulong max = 0;
                foreach (var a in arrays)
                {
                    var kib = a.DWord(0x07);
                    max += kib == 0x80000000 && a.Has(0x0F, 8) ? a.QWord(0x0F) : (ulong)kib * 1024;
                }
                if (max > 0) maxCapacity = max;
                var correction = arrays[0].Byte(0x06);
                ecc = correction switch { 3 => false, 4 or 5 or 6 or 7 => true, _ => null };
            }
            if (totalSlots is null or 0) totalSlots = slots.Count;
        }
        else
        {
            source = "WMI (Win32_PhysicalMemory)";
            slots = basic.Modules.Select(m => new RamSlot
            {
                Locator = m.Slot,
                Bank = m.Bank,
                IsPopulated = true,
                CapacityBytes = m.CapacityBytes,
                MemoryType = m.MemoryType,
                FormFactor = m.FormFactor,
                RatedSpeedMTs = m.SpeedMTs,
                ConfiguredSpeedMTs = m.ConfiguredSpeedMTs,
                Manufacturer = m.Manufacturer,
                PartNumber = m.PartNumber,
                SerialNumber = m.SerialNumber,
                DataWidth = m.DataWidth,
                ConfiguredVoltage = m.VoltageV,
            }).ToList();
        }

        slots = slots.Select(s => s.IsPopulated ? WithChannel(s) : s).ToList();
        var populated = slots.Where(s => s.IsPopulated).ToList();
        ecc ??= populated.Any(s => s.TotalWidth > s.DataWidth) ? true : populated.Count > 0 ? false : null;

        var (mode, count, inferred) = InferChannelMode(populated);
        var configured = populated.Select(s => s.ConfiguredSpeedMTs).Where(v => v > 0).DefaultIfEmpty().Min();
        var rated = populated.Select(s => s.RatedSpeedMTs).Where(v => v > 0).DefaultIfEmpty().Max();
        var vdd = populated.Select(s => s.ConfiguredVoltage).FirstOrDefault(v => v > 0);

        return new RamZSnapshot
        {
            Source = source,
            MemoryType = populated.Select(s => s.MemoryType).FirstOrDefault(t => !string.IsNullOrEmpty(t) && t != "—") ?? "—",
            TotalInstalledBytes = (ulong)populated.Sum(s => (decimal)s.CapacityBytes),
            ChannelMode = mode,
            ChannelCount = count,
            ChannelModeInferred = inferred,
            ConfiguredSpeedMTs = configured,
            DramFrequencyMHz = configured is > 0 ? configured / 2d : null,
            RatedSpeedMTs = rated,
            Ecc = ecc,
            UncoreFrequencyMHz = null,
            TotalSlots = totalSlots,
            PopulatedSlots = populated.Count,
            MaxCapacityBytes = maxCapacity,
            Slots = slots,
            Timings = new MemoryTimings { Vdd = vdd, Source = vdd is null ? "" : "SMBIOS" },
            Status = basic.Status,
            PageFiles = basic.PageFiles,
        };
    }

    // ------------------------------------------------------------------ SMBIOS type 17

    internal static RamSlot MapType17(SmbiosStructure s)
    {
        var sizeWord = s.Word(0x0C);
        ulong capacity = sizeWord switch
        {
            0 or 0xFFFF => 0,
            0x7FFF => (ulong)(s.DWord(0x1C) & 0x7FFFFFFF) * 1024 * 1024,
            _ => (sizeWord & 0x8000) != 0 ? (ulong)(sizeWord & 0x7FFF) * 1024 : (ulong)sizeWord * 1024 * 1024
        };

        int? Speed(int wordOffset, int extOffset)
        {
            var w = s.Word(wordOffset);
            if (w == 0xFFFF && s.Has(extOffset, 4)) return (int)s.DWord(extOffset);
            return w is 0 or 0xFFFF ? null : w;
        }

        double? Volt(int offset) => s.Has(offset, 2) && s.Word(offset) > 0 ? s.Word(offset) / 1000d : null;
        int? Width(int offset) => s.Word(offset) is 0 or 0xFFFF ? null : s.Word(offset);

        var detail = s.Word(0x13);
        var typeDetail = (detail & (1 << 15)) != 0 ? "LRDIMM"
            : (detail & (1 << 13)) != 0 ? "Registered (Buffered)"
            : (detail & (1 << 14)) != 0 ? "Unbuffered"
            : "";

        return new RamSlot
        {
            Locator = s.Str(0x10),
            Bank = s.Str(0x11),
            IsPopulated = capacity > 0,
            CapacityBytes = capacity,
            MemoryType = capacity > 0 ? MapType(s.Byte(0x12)) : "",
            TypeDetail = capacity > 0 ? typeDetail : "",
            FormFactor = capacity > 0 ? MapFormFactor(s.Byte(0x0E)) : "",
            Technology = s.Has(0x28, 1) ? s.Byte(0x28) switch { 3 => "DRAM", 4 => "NVDIMM-N", 5 => "NVDIMM-F", 6 => "NVDIMM-P", 7 => "Intel Optane", _ => "" } : "",
            RatedSpeedMTs = capacity > 0 ? Speed(0x15, 0x54) : null,
            ConfiguredSpeedMTs = capacity > 0 && s.Has(0x20, 2) ? Speed(0x20, 0x58) : null,
            Manufacturer = capacity > 0 ? MemoryInfoService.MapVendor(Clean(s.Str(0x17))) : "",
            SerialNumber = capacity > 0 ? Clean(s.Str(0x18)) : "",
            PartNumber = capacity > 0 ? Clean(s.Str(0x1A)) : "",
            Rank = capacity > 0 && s.Has(0x1B, 1) && (s.Byte(0x1B) & 0x0F) is > 0 and var r ? r : null,
            TotalWidth = Width(0x08),
            DataWidth = Width(0x0A),
            MinVoltage = Volt(0x22),
            MaxVoltage = Volt(0x24),
            ConfiguredVoltage = Volt(0x26),
        };
    }

    internal static string MapType(byte t) => t switch
    {
        0x12 => "DDR",
        0x13 => "DDR2",
        0x14 => "DDR2 FB-DIMM",
        0x18 => "DDR3",
        0x1A => "DDR4",
        0x1B => "LPDDR",
        0x1C => "LPDDR2",
        0x1D => "LPDDR3",
        0x1E => "LPDDR4/4X",
        0x1F => "Logical non-volatile",
        0x20 => "HBM",
        0x21 => "HBM2",
        0x22 => "DDR5",
        0x23 => "LPDDR5/5X",
        0x24 => "HBM3",
        0x0F => "SDRAM",
        _ => "—"
    };

    internal static string MapFormFactor(byte f) => f switch
    {
        0x03 => "SIMM",
        0x04 => "SIP",
        0x05 => "Chip",
        0x06 => "DIP",
        0x09 => "DIMM",
        0x0A => "TSOP",
        0x0B => "Row of chips",
        0x0C => "RIMM",
        0x0D => "SO-DIMM",
        0x0E => "SRIMM",
        0x0F => "FB-DIMM",
        0x10 => "Die",
        0x11 => "CAMM",
        _ => "—"
    };

    private static string Clean(string value)
    {
        var v = value.Trim();
        return v.Length == 0 || v.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || v.Equals("Not Specified", StringComparison.OrdinalIgnoreCase)
               || v.Equals("None", StringComparison.OrdinalIgnoreCase) || v.Trim('0').Length == 0 || v.All(c => c == 'F')
            ? "" : v;
    }

    // ------------------------------------------------------------------ channel inference

    [GeneratedRegex(@"CHANNEL\s*([A-H])|CHAN\s*([A-H])|DIMM_?([A-H])\d|^([A-H])\d$|_([A-H])\d?$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelRegex();

    [GeneratedRegex(@"(?:CONTROLLER|P|NODE)\s*(\d)", RegexOptions.IgnoreCase)]
    private static partial Regex ControllerRegex();

    internal static RamSlot WithChannel(RamSlot slot)
    {
        foreach (var text in new[] { slot.Bank, slot.Locator })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var m = ChannelRegex().Match(text);
            if (!m.Success) continue;
            var letter = m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value.ToUpperInvariant();
            var c = ControllerRegex().Match(slot.Bank + " " + slot.Locator);
            var channel = c.Success ? $"{c.Groups[1].Value}-{letter}" : letter;
            return new RamSlot
            {
                Locator = slot.Locator, Bank = slot.Bank, IsPopulated = slot.IsPopulated, CapacityBytes = slot.CapacityBytes,
                MemoryType = slot.MemoryType, TypeDetail = slot.TypeDetail, FormFactor = slot.FormFactor, Technology = slot.Technology,
                RatedSpeedMTs = slot.RatedSpeedMTs, ConfiguredSpeedMTs = slot.ConfiguredSpeedMTs, Manufacturer = slot.Manufacturer,
                PartNumber = slot.PartNumber, SerialNumber = slot.SerialNumber, Rank = slot.Rank, DataWidth = slot.DataWidth,
                TotalWidth = slot.TotalWidth, ConfiguredVoltage = slot.ConfiguredVoltage, MinVoltage = slot.MinVoltage,
                MaxVoltage = slot.MaxVoltage, Channel = channel,
            };
        }
        return slot;
    }

    /// <summary>Counts distinct channels from slot labels; falls back to the module count (marked as inferred).</summary>
    internal static (string Mode, int? Count, bool Inferred) InferChannelMode(IReadOnlyList<RamSlot> populated)
    {
        if (populated.Count == 0) return ("—", null, true);
        var channels = populated.Select(s => s.Channel).Where(c => c is not null).Distinct().Count();
        var inferred = channels == 0 || populated.Any(s => s.Channel is null);
        var count = inferred ? Math.Min(populated.Count, 2) : channels;
        // Soldered LPDDR typically shows one entry per channel; desktop boards with 4 DIMMs on 2 channels are the common case.
        var name = count switch
        {
            1 => "Single",
            2 => "Dual",
            3 => "Triple",
            4 => "Quad",
            6 => "Hexa",
            8 => "Octa",
            _ => $"{count}×"
        };
        return (name, count, inferred);
    }
}
