// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

namespace ZeloraInfo.Core.Disks;

/// <summary>Well-known ATA SMART attribute IDs and the NVMe health-log pseudo attributes.</summary>
public static class SmartAttributeCatalog
{
    public const int ReallocatedSectors = 0x05;
    public const int PowerOnHours = 0x09;
    public const int PowerCycleCount = 0x0C;
    public const int AirflowTemperature = 0xBE;
    public const int Temperature = 0xC2;
    public const int ReallocationEvents = 0xC4;
    public const int CurrentPendingSectors = 0xC5;
    public const int OfflineUncorrectable = 0xC6;
    public const int TotalLbasWritten = 0xF1;
    public const int TotalLbasRead = 0xF2;

    /// <summary>Attributes whose non-zero raw value signals media degradation.</summary>
    public static readonly int[] CriticalRawAttributes = [ReallocatedSectors, CurrentPendingSectors, OfflineUncorrectable];

    /// <summary>Vendor attributes whose normalized "current" value is a remaining-life percentage (in priority order).</summary>
    public static readonly int[] LifeAttributes = [0xE7, 0xA9, 0xCA, 0xB1, 0xAD, 0xE9];

    private static readonly Dictionary<int, string> AtaNames = new()
    {
        [0x01] = "Read Error Rate",
        [0x02] = "Throughput Performance",
        [0x03] = "Spin-Up Time",
        [0x04] = "Start/Stop Count",
        [0x05] = "Reallocated Sectors Count",
        [0x06] = "Read Channel Margin",
        [0x07] = "Seek Error Rate",
        [0x08] = "Seek Time Performance",
        [0x09] = "Power-On Hours",
        [0x0A] = "Spin Retry Count",
        [0x0B] = "Recalibration Retries",
        [0x0C] = "Power Cycle Count",
        [0x0D] = "Soft Read Error Rate",
        [0x16] = "Current Helium Level",
        [0xA0] = "Uncorrectable Sector Count (R/W)",
        [0xA1] = "Valid Spare Blocks",
        [0xA3] = "Initial Bad Blocks",
        [0xA4] = "Total Erase Count",
        [0xA5] = "Max Erase Count",
        [0xA6] = "Min Erase Count",
        [0xA7] = "Average Erase Count",
        [0xA8] = "Max NAND Erase Count",
        [0xA9] = "Remaining Life Percentage",
        [0xAA] = "Available Reserved Space",
        [0xAB] = "Program Fail Count",
        [0xAC] = "Erase Fail Count",
        [0xAD] = "Wear Leveling Count",
        [0xAE] = "Unexpected Power Loss Count",
        [0xAF] = "Power Loss Protection Failure",
        [0xB0] = "Erase Fail Count (Chip)",
        [0xB1] = "Wear Range Delta",
        [0xB2] = "Used Reserved Block Count (Chip)",
        [0xB3] = "Used Reserved Block Count Total",
        [0xB4] = "Unused Reserved Block Count Total",
        [0xB5] = "Program Fail Count Total",
        [0xB6] = "Erase Fail Count Total",
        [0xB7] = "SATA Downshift Error Count",
        [0xB8] = "End-to-End Error",
        [0xBB] = "Reported Uncorrectable Errors",
        [0xBC] = "Command Timeout",
        [0xBD] = "High Fly Writes",
        [0xBE] = "Airflow Temperature",
        [0xBF] = "G-Sense Error Rate",
        [0xC0] = "Power-off Retract Count",
        [0xC1] = "Load/Unload Cycle Count",
        [0xC2] = "Temperature",
        [0xC3] = "Hardware ECC Recovered",
        [0xC4] = "Reallocation Event Count",
        [0xC5] = "Current Pending Sector Count",
        [0xC6] = "Uncorrectable Sector Count",
        [0xC7] = "UltraDMA CRC Error Count",
        [0xC8] = "Write Error Rate",
        [0xC9] = "Soft Read Error Rate",
        [0xCA] = "Percent Lifetime Remaining",
        [0xCB] = "Run Out Cancel",
        [0xCC] = "Soft ECC Correction",
        [0xCD] = "Thermal Asperity Rate",
        [0xCE] = "Flying Height",
        [0xD2] = "RAIN Successful Recovery Count",
        [0xDC] = "Disk Shift",
        [0xDD] = "G-Sense Error Rate",
        [0xDE] = "Loaded Hours",
        [0xDF] = "Load/Unload Retry Count",
        [0xE0] = "Load Friction",
        [0xE1] = "Load/Unload Cycle Count",
        [0xE2] = "Load 'In'-time",
        [0xE3] = "Torque Amplification Count",
        [0xE4] = "Power-Off Retract Cycle",
        [0xE6] = "GMR Head Amplitude / Drive Life Protection",
        [0xE7] = "SSD Life Left",
        [0xE8] = "Endurance Remaining",
        [0xE9] = "Media Wearout Indicator",
        [0xEA] = "Average / Max Erase Count",
        [0xEB] = "Good Block Count / System Free Block Count",
        [0xF0] = "Head Flying Hours",
        [0xF1] = "Total Host Writes",
        [0xF2] = "Total Host Reads",
        [0xF3] = "Total LBAs Written Expanded",
        [0xF4] = "Total LBAs Read Expanded",
        [0xF5] = "Remaining Rated Write Endurance",
        [0xF6] = "Cumulative Host Sectors Written",
        [0xF7] = "Host Program Page Count",
        [0xF8] = "Background Program Page Count",
        [0xF9] = "NAND Writes (1 GiB)",
        [0xFA] = "Read Error Retry Rate",
        [0xFB] = "Minimum Spares Remaining",
        [0xFC] = "Newly Added Bad Flash Block",
        [0xFE] = "Free Fall Protection",
    };

    public static class Nvme
    {
        public const int CriticalWarning = 0x01;
        public const int CompositeTemperature = 0x02;
        public const int AvailableSpare = 0x03;
        public const int AvailableSpareThreshold = 0x04;
        public const int PercentageUsed = 0x05;
        public const int DataUnitsRead = 0x06;
        public const int DataUnitsWritten = 0x07;
        public const int HostReadCommands = 0x08;
        public const int HostWriteCommands = 0x09;
        public const int ControllerBusyTime = 0x0A;
        public const int PowerCycles = 0x0B;
        public const int PowerOnHours = 0x0C;
        public const int UnsafeShutdowns = 0x0D;
        public const int MediaErrors = 0x0E;
        public const int ErrorLogEntries = 0x0F;
        public const int WarningTempTime = 0x10;
        public const int CriticalTempTime = 0x11;

        public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
        {
            [CriticalWarning] = "Critical Warning",
            [CompositeTemperature] = "Composite Temperature",
            [AvailableSpare] = "Available Spare",
            [AvailableSpareThreshold] = "Available Spare Threshold",
            [PercentageUsed] = "Percentage Used",
            [DataUnitsRead] = "Data Units Read",
            [DataUnitsWritten] = "Data Units Written",
            [HostReadCommands] = "Host Read Commands",
            [HostWriteCommands] = "Host Write Commands",
            [ControllerBusyTime] = "Controller Busy Time",
            [PowerCycles] = "Power Cycles",
            [PowerOnHours] = "Power On Hours",
            [UnsafeShutdowns] = "Unsafe Shutdowns",
            [MediaErrors] = "Media and Data Integrity Errors",
            [ErrorLogEntries] = "Number of Error Information Log Entries",
            [WarningTempTime] = "Warning Composite Temperature Time",
            [CriticalTempTime] = "Critical Composite Temperature Time",
        };
    }

    public static string GetAtaName(int id) => AtaNames.TryGetValue(id, out var n) ? n : "Vendor Specific";
}
