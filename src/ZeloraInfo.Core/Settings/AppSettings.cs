// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using ZeloraInfo.Core.Benchmark;

namespace ZeloraInfo.Core.Settings;

/// <summary>Five built-in themes plus "follow Windows" (Midnight when dark, Daylight when light).</summary>
public enum AppTheme
{
    Midnight,
    Graphite,
    Aurora,
    Daylight,
    Sand,
    System
}

public static class AppThemeInfo
{
    public static readonly AppTheme[] Selectable = [AppTheme.Midnight, AppTheme.Graphite, AppTheme.Aurora, AppTheme.Daylight, AppTheme.Sand, AppTheme.System];

    public static bool IsDark(AppTheme theme) => theme is AppTheme.Midnight or AppTheme.Graphite or AppTheme.Aurora;

    /// <summary>Resolves <see cref="AppTheme.System"/> to a concrete theme.</summary>
    public static AppTheme Resolve(AppTheme theme, bool systemIsDark)
        => theme == AppTheme.System ? (systemIsDark ? AppTheme.Midnight : AppTheme.Daylight) : theme;
}

public enum CloseAction
{
    /// <summary>Show a dialog: minimize to tray or exit (with "remember my choice").</summary>
    Ask,
    MinimizeToTray,
    Exit
}

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit
}

/// <summary>
/// Root of the persisted JSON configuration (%APPDATA%\ZeloraInfo\settings.json).
/// Every property has a safe default so a missing or partial file never breaks startup.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public GeneralSettings General { get; set; } = new();
    public SmartSettings Smart { get; set; } = new();
    public BenchmarkSettings Benchmark { get; set; } = new();
    public HardwareSettings Hardware { get; set; } = new();

    public static AppSettings CreateDefault() => new();

    /// <summary>Clamps out-of-range values coming from a hand-edited file.</summary>
    public AppSettings Normalize()
    {
        General ??= new GeneralSettings();
        Smart ??= new SmartSettings();
        Benchmark ??= new BenchmarkSettings();
        Hardware ??= new HardwareSettings();
        Hardware.LiveRefreshMs = Math.Clamp(Hardware.LiveRefreshMs, 250, 10_000);
        Hardware.CpuBenchmarkSeconds = Math.Clamp(Hardware.CpuBenchmarkSeconds, 2, 30);

        if (!Localization.LanguageCatalog.SupportedLanguages.Contains(General.Language))
            General.Language = Localization.LanguageCatalog.DefaultLanguage;
        if (!Enum.IsDefined(General.Theme)) General.Theme = AppTheme.Midnight;
        if (!Enum.IsDefined(General.CloseAction)) General.CloseAction = CloseAction.Ask;
        General.UiScalePercent = Math.Clamp(General.UiScalePercent, 90, 130);
        if (!GeneralSettings.StartPages.Contains(General.StartPage)) General.StartPage = "DiskHealth";

        Smart.RefreshIntervalSeconds = Math.Clamp(Smart.RefreshIntervalSeconds, 0, 3600);
        Smart.CautionTemperatureC = Math.Clamp(Smart.CautionTemperatureC, 30, 100);
        Smart.CriticalTemperatureC = Math.Clamp(Smart.CriticalTemperatureC, Smart.CautionTemperatureC + 1, 110);

        Benchmark.TestSizeMiB = Math.Clamp(Benchmark.TestSizeMiB, 16, 64 * 1024);
        Benchmark.Iterations = Math.Clamp(Benchmark.Iterations, 1, 9);
        Benchmark.DurationSeconds = Math.Clamp(Benchmark.DurationSeconds, 1, 60);
        Benchmark.IntervalSeconds = Math.Clamp(Benchmark.IntervalSeconds, 0, 60);
        if (Benchmark.Tests is null || Benchmark.Tests.Count == 0)
            Benchmark.Tests = BenchmarkTestDefinition.DefaultProfile();
        foreach (var t in Benchmark.Tests)
            t.Normalize();
        return this;
    }
}

public sealed class GeneralSettings
{
    public string Language { get; set; } = Localization.LanguageCatalog.DefaultLanguage;
    public AppTheme Theme { get; set; } = AppTheme.Midnight;
    /// <summary>UI zoom in percent (90–130).</summary>
    public int UiScalePercent { get; set; } = 100;
    /// <summary>Navigation key of the page shown at startup.</summary>
    public string StartPage { get; set; } = "DiskHealth";
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; }
    /// <summary>What the window close (X) button does.</summary>
    public CloseAction CloseAction { get; set; } = CloseAction.Ask;
    /// <summary>Ask for elevation (UAC) on every launch so S.M.A.R.T. is always available.</summary>
    public bool AlwaysRunAsAdministrator { get; set; }

    public static readonly string[] StartPages = ["DiskHealth", "Benchmark", "System", "CpuDetails", "Memory", "RamDetails", "Display"];
}

public sealed class SmartSettings
{
    /// <summary>0 disables automatic refresh.</summary>
    public int RefreshIntervalSeconds { get; set; } = 60;
    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;
    public int CautionTemperatureC { get; set; } = 55;
    public int CriticalTemperatureC { get; set; } = 65;
    /// <summary>When true, a non-zero reallocated/pending/uncorrectable raw count is shown as "Caution".</summary>
    public bool TreatReallocatedAsCaution { get; set; } = true;
}

public sealed class HardwareSettings
{
    /// <summary>Refresh interval of live CPU / memory telemetry (ms).</summary>
    public int LiveRefreshMs { get; set; } = 1000;
    /// <summary>Mask module / disk serial numbers on screen and in reports.</summary>
    public bool MaskSerialNumbers { get; set; }
    /// <summary>Duration of each phase (single / multi thread) of the CPU quick benchmark.</summary>
    public int CpuBenchmarkSeconds { get; set; } = 5;
}

public sealed class BenchmarkSettings
{
    public int TestSizeMiB { get; set; } = 1024;
    public int Iterations { get; set; } = 5;
    public int DurationSeconds { get; set; } = 5;
    public int IntervalSeconds { get; set; } = 1;
    public BenchmarkMode Mode { get; set; } = BenchmarkMode.ReadWrite;
    public string? LastTargetPath { get; set; }
    public List<BenchmarkTestDefinition> Tests { get; set; } = BenchmarkTestDefinition.DefaultProfile();

    /// <summary>Delete the test file right after every run (always recommended).</summary>
    public bool DeleteTestFileAfterRun { get; set; } = true;
    /// <summary>On startup, remove orphaned test files left by a crashed/killed run.</summary>
    public bool CleanupOrphanedFilesOnStartup { get; set; } = true;
    /// <summary>Show a confirmation before benchmarking the Windows system drive.</summary>
    public bool WarnOnSystemDrive { get; set; } = true;
}
