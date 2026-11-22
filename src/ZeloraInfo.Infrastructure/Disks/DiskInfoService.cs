// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.Disks;
using ZeloraInfo.Core.Settings;
using ZeloraInfo.Infrastructure.Interop;
using static ZeloraInfo.Infrastructure.Interop.NativeMethods;

namespace ZeloraInfo.Infrastructure.Disks;

/// <summary>
/// Enumerates physical disks (Windows Storage WMI, falling back to Win32_DiskDrive) and reads health data:
/// <list type="bullet">
/// <item>SATA/ATA: SMART READ DATA/THRESHOLDS + IDENTIFY via SMART_RCV_DRIVE_DATA (admin).</item>
/// <item>NVMe: Health Information log page 02h via IOCTL_STORAGE_QUERY_PROPERTY (admin).</item>
/// <item>Fallback: MSFT_StorageReliabilityCounter (admin) and MSFT_PhysicalDisk.HealthStatus (no admin).</item>
/// </list>
/// </summary>
public sealed class DiskInfoService(IPrivilegeService privileges) : IDiskInfoService
{
    public Task<IReadOnlyList<DiskInfo>> GetDisksAsync(SmartSettings settings, CancellationToken cancellationToken = default)
        => Task.Run(() => (IReadOnlyList<DiskInfo>)Scan(settings, cancellationToken), cancellationToken);

    private List<DiskInfo> Scan(SmartSettings settings, CancellationToken ct)
    {
        var letters = GetDriveLettersByDisk();
        var systemLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':').ToUpperInvariant() ?? "C";
        var disks = EnumerateStorageDisks(letters, systemLetter);
        if (disks.Count == 0) disks = EnumerateWin32Disks(letters, systemLetter);

        var isAdmin = privileges.IsAdministrator;
        Dictionary<int, ReliabilityCounters>? reliability = null;

        foreach (var disk in disks)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (isAdmin) ReadSmart(disk);

                if (disk.SmartSource == SmartSource.None && isAdmin)
                {
                    reliability ??= ReadReliabilityCounters();
                    if (reliability.TryGetValue(disk.Index, out var rc)) ApplyReliability(disk, rc);
                }

                if (disk.SmartSource == SmartSource.None)
                {
                    disk.SmartUnavailableReason = !isAdmin ? "Smart.Unavailable.NeedsAdmin"
                        : disk.Bus == DiskBusKind.Usb ? "Smart.Unavailable.Usb"
                        : disk.Bus is DiskBusKind.Virtual or DiskBusKind.Raid ? "Smart.Unavailable.Virtual"
                        : "Smart.Unavailable.Unsupported";
                }
            }
            catch (Exception)
            {
                disk.SmartSource = SmartSource.None;
                disk.SmartUnavailableReason = "Smart.Unavailable.Error";
            }

            disk.TransferMode ??= disk.Bus switch
            {
                DiskBusKind.Nvme => "PCIe | NVMe",
                DiskBusKind.Usb => "USB",
                DiskBusKind.Sata => "SATA",
                DiskBusKind.Sas => "SAS",
                _ => null
            };
            SmartHealthEvaluator.Evaluate(disk, settings);
        }

        return disks.OrderBy(d => d.Index).ToList();
    }

    // ------------------------------------------------------------------ enumeration

    private static Dictionary<int, List<string>> GetDriveLettersByDisk()
    {
        var map = new Dictionary<int, List<string>>();
        foreach (var p in Wmi.Query(Wmi.Storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
        {
            var disk = p.Int("DiskNumber");
            var letterObj = p.Get("DriveLetter");
            var letter = letterObj switch
            {
                char c => c,
                ushort u => (char)u,
                _ => '\0'
            };
            if (disk is null || letter == '\0' || !char.IsLetter(letter)) continue;
            if (!map.TryGetValue(disk.Value, out var list)) map[disk.Value] = list = [];
            list.Add(char.ToUpperInvariant(letter) + ":");
        }
        foreach (var l in map.Values) l.Sort(StringComparer.Ordinal);
        return map;
    }

    private static List<DiskInfo> EnumerateStorageDisks(Dictionary<int, List<string>> letters, string systemLetter)
    {
        var list = new List<DiskInfo>();
        foreach (var o in Wmi.Query(Wmi.Storage, "SELECT * FROM MSFT_PhysicalDisk"))
        {
            if (!int.TryParse(o.Str("DeviceId"), out var index)) continue;
            var bus = MapBus(o.Int("BusType") ?? 0);
            var media = (o.Int("MediaType") ?? 0) switch
            {
                3 => DiskMediaKind.Hdd,
                4 => DiskMediaKind.Ssd,
                5 => DiskMediaKind.Scm,
                _ => bus == DiskBusKind.Nvme ? DiskMediaKind.Ssd : DiskMediaKind.Unknown
            };
            letters.TryGetValue(index, out var l);
            var model = FirstNonEmpty(o.Str("Model"), o.Str("FriendlyName"));
            var spindle = o.Long("SpindleSpeed");
            list.Add(new DiskInfo
            {
                Index = index,
                Model = model,
                SerialNumber = o.Str("SerialNumber"),
                Firmware = o.Str("FirmwareVersion"),
                Bus = bus,
                Media = media,
                SizeBytes = o.Long("Size") ?? 0,
                DriveLetters = l ?? [],
                IsSystemDisk = l?.Contains(systemLetter + ":") == true,
                IsRemovable = bus is DiskBusKind.Usb or DiskBusKind.SdCard,
                RotationRateRpm = spindle is > 0 and < 0xFFFFFFFF ? (int)spindle : media == DiskMediaKind.Ssd ? 0 : null,
                WindowsHealth = (o.Int("HealthStatus") ?? 5) switch
                {
                    0 => HealthStatus.Good,
                    1 => HealthStatus.Caution,
                    2 => HealthStatus.Bad,
                    _ => HealthStatus.Unknown
                },
            });
        }
        return list;
    }

    private static List<DiskInfo> EnumerateWin32Disks(Dictionary<int, List<string>> letters, string systemLetter)
    {
        var list = new List<DiskInfo>();
        foreach (var o in Wmi.Query(Wmi.Cimv2, "SELECT * FROM Win32_DiskDrive"))
        {
            var index = o.Int("Index") ?? -1;
            if (index < 0) continue;
            var iface = o.Str("InterfaceType").ToUpperInvariant();
            var bus = iface switch
            {
                "USB" => DiskBusKind.Usb,
                "IDE" => DiskBusKind.Sata,
                "SCSI" => o.Str("PNPDeviceID").Contains("NVME", StringComparison.OrdinalIgnoreCase) ? DiskBusKind.Nvme : DiskBusKind.Scsi,
                _ => DiskBusKind.Unknown
            };
            letters.TryGetValue(index, out var l);
            list.Add(new DiskInfo
            {
                Index = index,
                Model = o.Str("Model"),
                SerialNumber = o.Str("SerialNumber"),
                Firmware = o.Str("FirmwareRevision"),
                Bus = bus,
                Media = bus == DiskBusKind.Nvme ? DiskMediaKind.Ssd : DiskMediaKind.Unknown,
                SizeBytes = o.Long("Size") ?? 0,
                DriveLetters = l ?? [],
                IsSystemDisk = l?.Contains(systemLetter + ":") == true,
                IsRemovable = o.Str("MediaType").Contains("Removable", StringComparison.OrdinalIgnoreCase) || bus == DiskBusKind.Usb,
                WindowsHealth = o.Str("Status").Equals("OK", StringComparison.OrdinalIgnoreCase) ? HealthStatus.Good
                    : o.Str("Status").Equals("Pred Fail", StringComparison.OrdinalIgnoreCase) ? HealthStatus.Bad : HealthStatus.Unknown,
            });
        }
        return list;
    }

    private static DiskBusKind MapBus(int busType) => busType switch
    {
        1 => DiskBusKind.Scsi,
        2 or 3 or 11 => DiskBusKind.Sata,
        7 => DiskBusKind.Usb,
        8 => DiskBusKind.Raid,
        10 => DiskBusKind.Sas,
        12 or 13 => DiskBusKind.SdCard,
        14 or 15 or 16 => DiskBusKind.Virtual,
        17 => DiskBusKind.Nvme,
        0 => DiskBusKind.Unknown,
        _ => DiskBusKind.Other
    };

    // ------------------------------------------------------------------ SMART

    private static void ReadSmart(DiskInfo disk)
    {
        using var handle = CreateFile($@"\\.\PhysicalDrive{disk.Index}", GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return;

        if (disk.Bus is DiskBusKind.Nvme or DiskBusKind.Unknown or DiskBusKind.Scsi or DiskBusKind.Raid)
        {
            var log = QueryNvmeHealthLog(handle);
            if (log is not null)
            {
                disk.Attributes = SmartParsers.ParseNvmeHealthLog(log);
                disk.SmartSource = SmartSource.Nvme;
                return;
            }
        }

        if (disk.Bus != DiskBusKind.Nvme)
        {
            var identify = AtaCommand(handle, disk.Index, feature: 0, command: 0xEC, cylLow: 0, cylHigh: 0);
            if (identify is not null)
            {
                var info = SmartParsers.ParseAtaIdentify(identify);
                disk.TransferMode = info.TransferMode ?? disk.TransferMode;
                if (info.RotationRate is { } rpm) disk.RotationRateRpm = rpm;
            }

            var data = AtaCommand(handle, disk.Index, feature: 0xD0, command: 0xB0, cylLow: 0x4F, cylHigh: 0xC2);
            if (data is not null && data.Any(b => b != 0))
            {
                var thresholds = AtaCommand(handle, disk.Index, feature: 0xD1, command: 0xB0, cylLow: 0x4F, cylHigh: 0xC2);
                var attrs = SmartParsers.ParseAtaAttributes(data, thresholds ?? []);
                if (attrs.Count > 0)
                {
                    disk.Attributes = attrs;
                    disk.SmartSource = SmartSource.Ata;
                }
            }
        }
    }

    /// <summary>SMART_RCV_DRIVE_DATA with a SENDCMDINPARAMS built by hand (pack 1).</summary>
    private static byte[]? AtaCommand(SafeFileHandle handle, int driveNumber, byte feature, byte command, byte cylLow, byte cylHigh)
    {
        const int inSize = 32;                // sizeof(SENDCMDINPARAMS) - 1
        const int outHeader = 16;             // sizeof(SENDCMDOUTPARAMS) - 1
        var inBuf = new byte[inSize];
        BinaryPrimitives.WriteInt32LittleEndian(inBuf, SmartParsers.SectorSize); // cBufferSize
        inBuf[4] = feature;                   // bFeaturesReg
        inBuf[5] = 1;                         // bSectorCountReg
        inBuf[6] = 1;                         // bSectorNumberReg
        inBuf[7] = cylLow;                    // bCylLowReg
        inBuf[8] = cylHigh;                   // bCylHighReg
        inBuf[9] = (byte)(0xA0 | ((driveNumber & 1) << 4)); // bDriveHeadReg
        inBuf[10] = command;                  // bCommandReg
        inBuf[12] = (byte)driveNumber;        // bDriveNumber

        var outBuf = new byte[outHeader + SmartParsers.SectorSize];
        if (!DeviceIoControl(handle, SMART_RCV_DRIVE_DATA, inBuf, inBuf.Length, outBuf, outBuf.Length, out var returned, IntPtr.Zero))
            return null;
        if (returned < outHeader + SmartParsers.SectorSize) return null;
        if (outBuf[4] != 0) return null;      // DriverStatus.bDriverError
        return outBuf.AsSpan(outHeader, SmartParsers.SectorSize).ToArray();
    }

    /// <summary>IOCTL_STORAGE_QUERY_PROPERTY / StorageDeviceProtocolSpecificProperty / NVMe log page 02h.</summary>
    private static byte[]? QueryNvmeHealthLog(SafeFileHandle handle)
    {
        const int headerSize = 8;             // STORAGE_PROPERTY_QUERY up to AdditionalParameters
        const int protocolDataSize = 40;      // sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA)
        var buffer = new byte[headerSize + protocolDataSize + SmartParsers.SectorSize];
        var s = buffer.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(s[0..], 50);  // StorageDeviceProtocolSpecificProperty
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], 0);   // PropertyStandardQuery
        BinaryPrimitives.WriteInt32LittleEndian(s[8..], 3);   // ProtocolTypeNvme
        BinaryPrimitives.WriteInt32LittleEndian(s[12..], 2);  // NVMeDataTypeLogPage
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], 2);  // NVME_LOG_PAGE_HEALTH_INFO
        BinaryPrimitives.WriteInt32LittleEndian(s[20..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], protocolDataSize); // ProtocolDataOffset
        BinaryPrimitives.WriteInt32LittleEndian(s[28..], SmartParsers.SectorSize); // ProtocolDataLength

        var output = new byte[buffer.Length];
        if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, buffer, buffer.Length, output, output.Length, out var returned, IntPtr.Zero))
            return null;

        // STORAGE_PROTOCOL_DATA_DESCRIPTOR { Version, Size, STORAGE_PROTOCOL_SPECIFIC_DATA }
        var o = output.AsSpan();
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(o[(8 + 16)..]);
        var dataLength = BinaryPrimitives.ReadInt32LittleEndian(o[(8 + 20)..]);
        var start = 8 + dataOffset;
        if (dataLength < SmartParsers.SectorSize || start + SmartParsers.SectorSize > returned) return null;
        var log = o.Slice(start, SmartParsers.SectorSize).ToArray();
        return log.Any(b => b != 0) ? log : null;
    }

    // ------------------------------------------------------------------ reliability counters fallback

    private sealed record ReliabilityCounters(int? Temperature, long? PowerOnHours, long? StartStop, int? Wear, long? ReadErrors);

    private static Dictionary<int, ReliabilityCounters> ReadReliabilityCounters()
    {
        var map = new Dictionary<int, ReliabilityCounters>();
        foreach (var o in Wmi.Query(Wmi.Storage, "SELECT * FROM MSFT_StorageReliabilityCounter"))
        {
            if (!int.TryParse(o.Str("DeviceId"), out var id)) continue;
            map[id] = new ReliabilityCounters(o.Int("Temperature"), o.Long("PowerOnHours"), o.Long("StartStopCycleCount"), o.Int("Wear"), o.Long("ReadErrorsTotal"));
        }
        return map;
    }

    private static void ApplyReliability(DiskInfo disk, ReliabilityCounters rc)
    {
        if (rc.Temperature is null && rc.PowerOnHours is null && rc.Wear is null) return;
        disk.SmartSource = SmartSource.StorageReliability;
        if (rc.Temperature is > 0 and < 125) disk.TemperatureC = rc.Temperature;
        if (rc.PowerOnHours is > 0) disk.PowerOnHours = rc.PowerOnHours;
        if (rc.StartStop is > 0) disk.PowerOnCount = rc.StartStop;
        if (rc.Wear is >= 0 and <= 100) disk.LifeRemainingPercent = 100 - rc.Wear;
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}
