// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeloraInfo.App.Services;
using ZeloraInfo.Core.Benchmark;
using ZeloraInfo.Core.Localization;
using ZeloraInfo.Core.Settings;

namespace ZeloraInfo.App.ViewModels;

public sealed partial class ThemeOption : ObservableObject
{
    public ThemeOption(AppTheme theme)
    {
        Theme = theme;
        var b = Services.ThemeManager.PreviewBrushes(theme);
        Background = b[0];
        Card = b[1];
        Accent = b[2];
        Text = b[3];
    }

    public AppTheme Theme { get; }
    public string Name => LanguageCatalog.Get("Theme." + Theme);
    public string Description => LanguageCatalog.Get("Theme." + Theme + ".Desc");
    public Brush Background { get; }
    public Brush Card { get; }
    public Brush Accent { get; }
    public Brush Text { get; }

    [ObservableProperty] private bool _isSelected;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }
}

/// <summary>
/// Central control panel: every option of the app is edited here, applied immediately and persisted to JSON.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private bool _suspend;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Themes = AppThemeInfo.Selectable.Select(t => new ThemeOption(t)).ToList();
        BuildChoices();
        SyncThemeSelection();
    }

    public override string PageKey => "Settings";

    private AppSettings S => _services.Settings.Current;

    public IReadOnlyList<ThemeOption> Themes { get; }
    public IReadOnlyList<Choice<string>> Languages { get; } = LanguageCatalog.SupportedLanguages
        .Select(l => new Choice<string>(l, LanguageCatalog.LanguageNames[l])).ToList();
    public IReadOnlyList<Choice<string>> StartPages { get; private set; } = [];
    public IReadOnlyList<Choice<CloseAction>> CloseActions { get; private set; } = [];
    public IReadOnlyList<Choice<TemperatureUnit>> TemperatureUnits { get; private set; } = [];
    public IReadOnlyList<Choice<BenchmarkMode>> BenchmarkModes { get; private set; } = [];
    public IReadOnlyList<Choice<int>> TestSizes { get; } = new[] { 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536 }
        .Select(m => new Choice<int>(m, m >= 1024 ? $"{m / 1024} GiB" : $"{m} MiB")).ToList();
    public IReadOnlyList<Choice<int>> LiveRefreshChoices { get; } = new[] { 250, 500, 1000, 2000, 3000, 5000, 10000 }
        .Select(ms => new Choice<int>(ms, ms < 1000 ? $"{ms} ms" : $"{ms / 1000} s")).ToList();
    public IReadOnlyList<Choice<int>> SmartRefreshChoices { get; private set; } = [];
    public IReadOnlyList<int> IterationChoices { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9];
    public IReadOnlyList<int> DurationChoices { get; } = [1, 2, 3, 5, 10, 20, 30];
    public IReadOnlyList<int> IntervalChoices { get; } = [0, 1, 3, 5, 10, 30];
    public IReadOnlyList<int> CpuBenchChoices { get; } = [2, 3, 5, 10, 15, 30];

    public string SettingsFilePath => _services.Settings.SettingsFilePath;
    public bool IsAdministrator => _services.Privileges.IsAdministrator;

    private void BuildChoices()
    {
        StartPages = GeneralSettings.StartPages.Select(k => new Choice<string>(k, L("Nav." + k))).ToList();
        CloseActions =
        [
            new(CloseAction.Ask, L("Set.Close.Ask")),
            new(CloseAction.MinimizeToTray, L("Set.Close.Tray")),
            new(CloseAction.Exit, L("Set.Close.Exit")),
        ];
        TemperatureUnits =
        [
            new(TemperatureUnit.Celsius, L("Set.TempUnit.Celsius")),
            new(TemperatureUnit.Fahrenheit, L("Set.TempUnit.Fahrenheit")),
        ];
        BenchmarkModes =
        [
            new(BenchmarkMode.ReadWrite, L("Bench.Mode.ReadWrite")),
            new(BenchmarkMode.ReadOnly, L("Bench.Mode.ReadOnly")),
            new(BenchmarkMode.WriteOnly, L("Bench.Mode.WriteOnly")),
        ];
        SmartRefreshChoices = new[] { 0, 10, 30, 60, 120, 300, 600, 1800 }
            .Select(s => new Choice<int>(s, s == 0 ? L("Common.Disabled") : s < 60 ? $"{s} s" : $"{s / 60} min")).ToList();
        foreach (var name in new[] { nameof(StartPages), nameof(CloseActions), nameof(TemperatureUnits), nameof(BenchmarkModes), nameof(SmartRefreshChoices) })
            OnPropertyChanged(name);
    }

    /// <summary>Applies a change, persists it and raises PropertyChanged for the bound property.</summary>
    private void Update(Action<AppSettings> change, string propertyName, Action? apply = null)
    {
        if (_suspend) return;
        change(S);
        S.Normalize();
        try
        {
            _services.Settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Ui.ShowError(LF("Common.ExportFailed", ex.Message));
        }
        apply?.Invoke();
        OnPropertyChanged(propertyName);
    }

    // ------------------------------------------------------------------ appearance

    public string Language
    {
        get => S.General.Language;
        set => Update(s => s.General.Language = value, nameof(Language), () => _services.Localization.Apply(value));
    }

    [RelayCommand]
    private void SelectTheme(ThemeOption option)
        => Update(s => s.General.Theme = option.Theme, nameof(Themes), () =>
        {
            _services.Theme.Apply(option.Theme);
            SyncThemeSelection();
        });

    private void SyncThemeSelection()
    {
        foreach (var t in Themes) t.IsSelected = t.Theme == S.General.Theme;
    }

    public int UiScalePercent
    {
        get => S.General.UiScalePercent;
        set => Update(s => s.General.UiScalePercent = value, nameof(UiScalePercent));
    }

    public string StartPage
    {
        get => S.General.StartPage;
        set => Update(s => s.General.StartPage = value, nameof(StartPage));
    }

    // ------------------------------------------------------------------ behaviour

    public bool StartWithWindows
    {
        get => S.General.StartWithWindows;
        set => Update(s => s.General.StartWithWindows = value, nameof(StartWithWindows), () =>
        {
            try
            {
                _services.Startup.SetRegistered(value, S.General.StartMinimized ? "--minimized" : "");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException)
            {
                _services.Ui.ShowError(LF("Set.StartupFailed", ex.Message));
            }
        });
    }

    public bool StartMinimized
    {
        get => S.General.StartMinimized;
        set => Update(s => s.General.StartMinimized = value, nameof(StartMinimized), () =>
        {
            if (S.General.StartWithWindows)
            {
                try { _services.Startup.SetRegistered(true, value ? "--minimized" : ""); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
            }
        });
    }

    public bool MinimizeToTray
    {
        get => S.General.MinimizeToTray;
        set => Update(s => s.General.MinimizeToTray = value, nameof(MinimizeToTray));
    }

    public CloseAction CloseAction
    {
        get => S.General.CloseAction;
        set => Update(s => s.General.CloseAction = value, nameof(CloseAction));
    }

    public bool AlwaysRunAsAdministrator
    {
        get => S.General.AlwaysRunAsAdministrator;
        set => Update(s => s.General.AlwaysRunAsAdministrator = value, nameof(AlwaysRunAsAdministrator));
    }

    [RelayCommand]
    private void RestartAsAdmin() => App.RestartElevated();

    // ------------------------------------------------------------------ disk health

    public int SmartRefreshSeconds
    {
        get => S.Smart.RefreshIntervalSeconds;
        set => Update(s => s.Smart.RefreshIntervalSeconds = value, nameof(SmartRefreshSeconds));
    }

    public TemperatureUnit TemperatureUnit
    {
        get => S.Smart.TemperatureUnit;
        set => Update(s => s.Smart.TemperatureUnit = value, nameof(TemperatureUnit));
    }

    public int CautionTemperatureC
    {
        get => S.Smart.CautionTemperatureC;
        set => Update(s => s.Smart.CautionTemperatureC = value, nameof(CautionTemperatureC), () => OnPropertyChanged(nameof(CriticalTemperatureC)));
    }

    public int CriticalTemperatureC
    {
        get => S.Smart.CriticalTemperatureC;
        set => Update(s => s.Smart.CriticalTemperatureC = value, nameof(CriticalTemperatureC));
    }

    public bool TreatReallocatedAsCaution
    {
        get => S.Smart.TreatReallocatedAsCaution;
        set => Update(s => s.Smart.TreatReallocatedAsCaution = value, nameof(TreatReallocatedAsCaution));
    }

    // ------------------------------------------------------------------ hardware monitoring

    public int LiveRefreshMs
    {
        get => S.Hardware.LiveRefreshMs;
        set => Update(s => s.Hardware.LiveRefreshMs = value, nameof(LiveRefreshMs));
    }

    public bool MaskSerialNumbers
    {
        get => S.Hardware.MaskSerialNumbers;
        set => Update(s => s.Hardware.MaskSerialNumbers = value, nameof(MaskSerialNumbers));
    }

    public int CpuBenchmarkSeconds
    {
        get => S.Hardware.CpuBenchmarkSeconds;
        set => Update(s => s.Hardware.CpuBenchmarkSeconds = value, nameof(CpuBenchmarkSeconds));
    }

    // ------------------------------------------------------------------ benchmark defaults & cleanup

    public int BenchTestSizeMiB
    {
        get => S.Benchmark.TestSizeMiB;
        set => Update(s => s.Benchmark.TestSizeMiB = value, nameof(BenchTestSizeMiB));
    }

    public int BenchIterations
    {
        get => S.Benchmark.Iterations;
        set => Update(s => s.Benchmark.Iterations = value, nameof(BenchIterations));
    }

    public int BenchDuration
    {
        get => S.Benchmark.DurationSeconds;
        set => Update(s => s.Benchmark.DurationSeconds = value, nameof(BenchDuration));
    }

    public int BenchInterval
    {
        get => S.Benchmark.IntervalSeconds;
        set => Update(s => s.Benchmark.IntervalSeconds = value, nameof(BenchInterval));
    }

    public BenchmarkMode BenchMode
    {
        get => S.Benchmark.Mode;
        set => Update(s => s.Benchmark.Mode = value, nameof(BenchMode));
    }

    public bool DeleteTestFileAfterRun
    {
        get => S.Benchmark.DeleteTestFileAfterRun;
        set => Update(s => s.Benchmark.DeleteTestFileAfterRun = value, nameof(DeleteTestFileAfterRun));
    }

    public bool CleanupOrphanedFilesOnStartup
    {
        get => S.Benchmark.CleanupOrphanedFilesOnStartup;
        set => Update(s => s.Benchmark.CleanupOrphanedFilesOnStartup = value, nameof(CleanupOrphanedFilesOnStartup));
    }

    public bool WarnOnSystemDrive
    {
        get => S.Benchmark.WarnOnSystemDrive;
        set => Update(s => s.Benchmark.WarnOnSystemDrive = value, nameof(WarnOnSystemDrive));
    }

    [RelayCommand]
    private void CleanupNow()
    {
        var count = _services.Benchmark.CleanupOrphanedTestFiles(App.BenchmarkCleanupRoots(S));
        _services.Ui.Toast(LF("Bench.CleanupDone", count));
    }

    // ------------------------------------------------------------------ maintenance

    [RelayCommand]
    private void OpenSettingsFolder() => _services.Ui.OpenFolder(Path.GetDirectoryName(SettingsFilePath)!);

    [RelayCommand]
    private void ResetDefaults()
    {
        if (!_services.Ui.Confirm(L("Set.ResetDefaults"), L("Set.ResetConfirm"))) return;
        var startup = S.General.StartWithWindows;
        _services.Settings.ResetToDefaults();
        if (startup)
        {
            try { _services.Startup.SetRegistered(false); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
        }
        _suspend = true;
        _services.Localization.Apply(S.General.Language);
        _services.Theme.Apply(S.General.Theme);
        SyncThemeSelection();
        _suspend = false;
        OnPropertyChanged(string.Empty);
        _services.Ui.Toast(L("Set.ResetDone"));
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        BuildChoices();
        foreach (var t in Themes) t.Refresh();
        // Re-raise so ComboBoxes re-select their (rebuilt) items.
        _suspend = true;
        OnPropertyChanged(string.Empty);
        _suspend = false;
    }
}
