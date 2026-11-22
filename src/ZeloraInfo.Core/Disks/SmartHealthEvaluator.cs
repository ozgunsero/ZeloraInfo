// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using ZeloraInfo.Core.Settings;

namespace ZeloraInfo.Core.Disks;

public enum TemperatureLevel
{
    Unknown,
    Normal,
    Caution,
    Critical
}

/// <summary>
/// Pure, hardware-independent health logic. Derives the overall health, per-attribute status,
/// summary values (temperature, power-on hours …) and the localized reasons from raw SMART data.
/// </summary>
public static class SmartHealthEvaluator
{
    public static void Evaluate(DiskInfo disk, SmartSettings settings)
    {
        disk.HealthReasons.Clear();
        var health = HealthStatus.Unknown;

        switch (disk.SmartSource)
        {
            case SmartSource.Ata:
                health = EvaluateAta(disk, settings);
                break;
            case SmartSource.Nvme:
                health = EvaluateNvme(disk);
                break;
            case SmartSource.StorageReliability:
                health = HealthStatus.Good;
                break;
        }

        // Windows' own predictive failure flag always wins if it is worse.
        if (disk.WindowsHealth == HealthStatus.Bad)
        {
            health = Max(health, HealthStatus.Bad);
            disk.HealthReasons.Add("Health.Reason.WindowsUnhealthy");
        }
        else if (disk.WindowsHealth == HealthStatus.Caution)
        {
            health = Max(health, HealthStatus.Caution);
            disk.HealthReasons.Add("Health.Reason.WindowsWarning");
        }
        else if (health == HealthStatus.Unknown && disk.WindowsHealth == HealthStatus.Good)
        {
            health = HealthStatus.Good;
        }

        if (disk.LifeRemainingPercent is { } life && life <= 10)
        {
            health = Max(health, HealthStatus.Caution);
            disk.HealthReasons.Add("Health.Reason.LowLife");
        }

        disk.Health = health;
    }

    public static TemperatureLevel GetTemperatureLevel(int? celsius, SmartSettings settings)
    {
        if (celsius is null) return TemperatureLevel.Unknown;
        if (celsius >= settings.CriticalTemperatureC) return TemperatureLevel.Critical;
        if (celsius >= settings.CautionTemperatureC) return TemperatureLevel.Caution;
        return TemperatureLevel.Normal;
    }

    private static HealthStatus EvaluateAta(DiskInfo disk, SmartSettings settings)
    {
        var health = HealthStatus.Good;
        var byId = new Dictionary<int, SmartAttribute>();
        foreach (var a in disk.Attributes)
            byId.TryAdd(a.Id, a);

        foreach (var a in disk.Attributes)
        {
            a.Status = AttributeStatus.Ok;
            if (a.Threshold is > 0 and var th && a.Current is > 0 and var cur && cur <= th)
            {
                a.Status = AttributeStatus.Bad;
                health = HealthStatus.Bad;
                AddOnce(disk, "Health.Reason.ThresholdExceeded");
            }
        }

        if (settings.TreatReallocatedAsCaution)
        {
            foreach (var id in SmartAttributeCatalog.CriticalRawAttributes)
            {
                if (byId.TryGetValue(id, out var a) && (a.RawValue & 0xFFFFFFFF) > 0)
                {
                    if (a.Status == AttributeStatus.Ok) a.Status = AttributeStatus.Caution;
                    health = Max(health, HealthStatus.Caution);
                    AddOnce(disk, id switch
                    {
                        SmartAttributeCatalog.ReallocatedSectors => "Health.Reason.Reallocated",
                        SmartAttributeCatalog.CurrentPendingSectors => "Health.Reason.Pending",
                        _ => "Health.Reason.Uncorrectable"
                    });
                }
            }
        }

        // Summary values.
        if (TryTemperature(byId, SmartAttributeCatalog.Temperature, out var t) || TryTemperature(byId, SmartAttributeCatalog.AirflowTemperature, out t))
            disk.TemperatureC ??= t;
        if (byId.TryGetValue(SmartAttributeCatalog.PowerOnHours, out var poh))
            disk.PowerOnHours ??= (long)(poh.RawValue & 0xFFFFFFFF);
        if (byId.TryGetValue(SmartAttributeCatalog.PowerCycleCount, out var pcc))
            disk.PowerOnCount ??= (long)(pcc.RawValue & 0xFFFFFFFF);
        if (byId.TryGetValue(SmartAttributeCatalog.TotalLbasWritten, out var w))
            disk.HostWritesBytes ??= SafeMultiply(w.RawValue, 512);
        if (byId.TryGetValue(SmartAttributeCatalog.TotalLbasRead, out var r))
            disk.HostReadsBytes ??= SafeMultiply(r.RawValue, 512);

        if (disk.Media != DiskMediaKind.Hdd)
        {
            foreach (var id in SmartAttributeCatalog.LifeAttributes)
            {
                if (byId.TryGetValue(id, out var a) && a.Current is >= 0 and <= 100)
                {
                    disk.LifeRemainingPercent ??= a.Current;
                    break;
                }
            }
        }

        return health;
    }

    private static HealthStatus EvaluateNvme(DiskInfo disk)
    {
        var health = HealthStatus.Good;
        ulong Raw(int id) => disk.Attributes.FirstOrDefault(a => a.Id == id)?.RawValue ?? 0;
        bool Has(int id) => disk.Attributes.Any(a => a.Id == id);

        foreach (var a in disk.Attributes) a.Status = AttributeStatus.Ok;

        var critical = Raw(SmartAttributeCatalog.Nvme.CriticalWarning);
        if (critical != 0)
        {
            health = HealthStatus.Bad;
            Mark(disk, SmartAttributeCatalog.Nvme.CriticalWarning, AttributeStatus.Bad);
            AddOnce(disk, "Health.Reason.NvmeCriticalWarning");
        }

        if (Has(SmartAttributeCatalog.Nvme.AvailableSpare) && Has(SmartAttributeCatalog.Nvme.AvailableSpareThreshold))
        {
            var spare = Raw(SmartAttributeCatalog.Nvme.AvailableSpare);
            var threshold = Raw(SmartAttributeCatalog.Nvme.AvailableSpareThreshold);
            if (threshold > 0 && threshold <= 100 && spare < threshold)
            {
                health = HealthStatus.Bad;
                Mark(disk, SmartAttributeCatalog.Nvme.AvailableSpare, AttributeStatus.Bad);
                AddOnce(disk, "Health.Reason.NvmeSpare");
            }
        }

        if (Has(SmartAttributeCatalog.Nvme.PercentageUsed))
        {
            var used = (int)Math.Min(255, Raw(SmartAttributeCatalog.Nvme.PercentageUsed));
            disk.LifeRemainingPercent ??= Math.Max(0, 100 - used);
            if (used >= 90)
            {
                health = Max(health, HealthStatus.Caution);
                Mark(disk, SmartAttributeCatalog.Nvme.PercentageUsed, AttributeStatus.Caution);
            }
        }

        if (Raw(SmartAttributeCatalog.Nvme.MediaErrors) > 0)
        {
            health = Max(health, HealthStatus.Caution);
            Mark(disk, SmartAttributeCatalog.Nvme.MediaErrors, AttributeStatus.Caution);
            AddOnce(disk, "Health.Reason.NvmeMediaErrors");
        }

        if (Has(SmartAttributeCatalog.Nvme.CompositeTemperature))
        {
            var kelvin = (long)Raw(SmartAttributeCatalog.Nvme.CompositeTemperature);
            if (kelvin > 200) disk.TemperatureC ??= (int)(kelvin - 273);
        }
        if (Has(SmartAttributeCatalog.Nvme.PowerOnHours)) disk.PowerOnHours ??= (long)Math.Min(long.MaxValue, Raw(SmartAttributeCatalog.Nvme.PowerOnHours));
        if (Has(SmartAttributeCatalog.Nvme.PowerCycles)) disk.PowerOnCount ??= (long)Math.Min(long.MaxValue, Raw(SmartAttributeCatalog.Nvme.PowerCycles));
        // NVMe "data unit" = 1000 × 512 bytes.
        if (Has(SmartAttributeCatalog.Nvme.DataUnitsRead)) disk.HostReadsBytes ??= SafeMultiply(Raw(SmartAttributeCatalog.Nvme.DataUnitsRead), 512_000);
        if (Has(SmartAttributeCatalog.Nvme.DataUnitsWritten)) disk.HostWritesBytes ??= SafeMultiply(Raw(SmartAttributeCatalog.Nvme.DataUnitsWritten), 512_000);

        return health;
    }

    private static bool TryTemperature(Dictionary<int, SmartAttribute> byId, int id, out int celsius)
    {
        celsius = 0;
        if (!byId.TryGetValue(id, out var a)) return false;
        var value = (int)(a.RawValue & 0xFF);
        if (value is <= 0 or > 125) return false;
        celsius = value;
        return true;
    }

    private static long SafeMultiply(ulong value, long factor)
    {
        if (value == 0) return 0;
        return value > (ulong)(long.MaxValue / factor) ? long.MaxValue : (long)value * factor;
    }

    private static void Mark(DiskInfo disk, int id, AttributeStatus status)
    {
        var a = disk.Attributes.FirstOrDefault(x => x.Id == id);
        if (a is not null) a.Status = status;
    }

    private static void AddOnce(DiskInfo disk, string key)
    {
        if (!disk.HealthReasons.Contains(key)) disk.HealthReasons.Add(key);
    }

    private static HealthStatus Max(HealthStatus a, HealthStatus b) => (HealthStatus)Math.Max((int)a, (int)b);
}
