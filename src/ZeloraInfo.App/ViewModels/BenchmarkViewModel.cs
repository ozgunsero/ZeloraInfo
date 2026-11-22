// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeloraInfo.App.Services;
using ZeloraInfo.Core.Benchmark;
using ZeloraInfo.Core.Formatting;
using ZeloraInfo.Core.Localization;
using ZeloraInfo.Infrastructure.Benchmark;

namespace ZeloraInfo.App.ViewModels;

/// <summary>Value + display label pair for combo boxes.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class BenchmarkViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private CancellationTokenSource? _cts;
    private BenchmarkReport? _lastReport;

    public BenchmarkViewModel(AppServices services)
    {
        _services = services;
        var s = services.Settings.Current.Benchmark;
        _testSizeMiB = s.TestSizeMiB;
        _iterations = s.Iterations;
        _durationSeconds = s.DurationSeconds;
        _intervalSeconds = s.IntervalSeconds;
        _mode = s.Mode;
        foreach (var t in s.Tests) Rows.Add(new BenchmarkRowViewModel(t.Clone()));
        BuildChoices();
        RefreshDrives();
        TargetPath = !string.IsNullOrWhiteSpace(s.LastTargetPath) && Directory.Exists(s.LastTargetPath)
            ? s.LastTargetPath
            : Drives.FirstOrDefault(d => !d.IsSystem)?.Root ?? Drives.FirstOrDefault()?.Root;
    }

    public override string PageKey => "Benchmark";

    public ObservableCollection<DriveOption> Drives { get; } = [];
    public ObservableCollection<BenchmarkRowViewModel> Rows { get; } = [];
    public IReadOnlyList<Choice<int>> TestSizes { get; private set; } = [];
    public IReadOnlyList<int> IterationChoices { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9];
    public IReadOnlyList<int> DurationChoices { get; } = [1, 2, 3, 5, 10, 20, 30];
    public IReadOnlyList<int> IntervalChoices { get; } = [0, 1, 3, 5, 10, 30];
    public IReadOnlyList<Choice<BenchmarkMode>> Modes { get; private set; } = [];
    public IReadOnlyList<Choice<BenchmarkAccess>> AccessChoices { get; private set; } = [];
    public IReadOnlyList<Choice<int>> BlockChoices { get; } = BenchmarkTestDefinition.AllowedBlockSizesKiB
        .Select(k => new Choice<int>(k, k >= 1024 ? $"{k / 1024} MiB" : $"{k} KiB")).ToList();
    public IReadOnlyList<int> QueueChoices { get; } = BenchmarkTestDefinition.AllowedQueueDepths;
    public IReadOnlyList<int> ThreadChoices { get; } = BenchmarkTestDefinition.AllowedThreads;

    [ObservableProperty] private string? _targetPath;
    [ObservableProperty] private int _testSizeMiB;
    [ObservableProperty] private int _iterations;
    [ObservableProperty] private int _durationSeconds;
    [ObservableProperty] private int _intervalSeconds;
    [ObservableProperty] private BenchmarkMode _mode;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _freeSpaceText = "";
    [ObservableProperty] private bool _hasResults;

    public bool IsIdle => !IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnTargetPathChanged(string? value) => UpdateFreeSpace();

    public override void OnActivated()
    {
        RefreshDrives();
        UpdateFreeSpace();
        if (!IsRunning) StatusText = L("Bench.Phase.Idle");
    }

    private void BuildChoices()
    {
        int[] sizes = [16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536];
        TestSizes = sizes.Select(m => new Choice<int>(m, m >= 1024 ? $"{m / 1024} GiB" : $"{m} MiB")).ToList();
        Modes =
        [
            new(BenchmarkMode.ReadWrite, L("Bench.Mode.ReadWrite")),
            new(BenchmarkMode.ReadOnly, L("Bench.Mode.ReadOnly")),
            new(BenchmarkMode.WriteOnly, L("Bench.Mode.WriteOnly")),
        ];
        AccessChoices =
        [
            new(BenchmarkAccess.Sequential, L("Bench.Access.Sequential")),
            new(BenchmarkAccess.Random, L("Bench.Access.Random")),
        ];
        OnPropertyChanged(nameof(TestSizes));
        OnPropertyChanged(nameof(Modes));
        OnPropertyChanged(nameof(AccessChoices));
    }

    private void RefreshDrives()
    {
        var selected = TargetPath;
        Drives.Clear();
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                var c = LanguageCatalog.CurrentCulture;
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : $" [{d.VolumeLabel}]";
                Drives.Add(new DriveOption(d.RootDirectory.FullName,
                    $"{d.Name.TrimEnd('\\')}{label}  —  {ByteFormatter.FormatDecimal(d.AvailableFreeSpace, 1, c)} / {ByteFormatter.FormatDecimal(d.TotalSize, 1, c)}",
                    string.Equals(d.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase)));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        if (selected is not null) TargetPath = selected;
    }

    private void UpdateFreeSpace()
    {
        FreeSpaceText = "";
        if (string.IsNullOrWhiteSpace(TargetPath)) return;
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(TargetPath))!);
            var c = LanguageCatalog.CurrentCulture;
            if (d.IsReady) FreeSpaceText = LF("Bench.FreeSpace", ByteFormatter.FormatDecimal(d.AvailableFreeSpace, 1, c), ByteFormatter.FormatDecimal(d.TotalSize, 1, c));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }
    }

    [RelayCommand]
    private void Browse()
    {
        var folder = _services.Ui.PickFolder(L("Bench.SelectFolder"), TargetPath);
        if (folder is not null) TargetPath = folder;
    }

    [RelayCommand]
    private void ResetProfile()
    {
        Rows.Clear();
        foreach (var t in BenchmarkTestDefinition.DefaultProfile()) Rows.Add(new BenchmarkRowViewModel(t));
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath) || !Directory.Exists(TargetPath))
        {
            _services.Ui.ShowError(L("Bench.InvalidTarget"));
            return;
        }

        var settings = _services.Settings.Current;
        var sizeBytes = (long)TestSizeMiB * 1024 * 1024;
        var root = Path.GetPathRoot(Path.GetFullPath(TargetPath));
        var isSystem = string.Equals(root, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);
        if (isSystem && settings.Benchmark.WarnOnSystemDrive)
        {
            var written = ByteFormatter.FormatBinary(sizeBytes * (Mode == BenchmarkMode.ReadOnly ? 1 : 1 + Iterations), 1, LanguageCatalog.CurrentCulture);
            if (!_services.Ui.Confirm(L("Bench.SystemDrive.Title"), LF("Bench.SystemDrive.Text", root, written))) return;
        }

        // Persist the chosen parameters as new defaults.
        var b = settings.Benchmark;
        b.TestSizeMiB = TestSizeMiB;
        b.Iterations = Iterations;
        b.DurationSeconds = DurationSeconds;
        b.IntervalSeconds = IntervalSeconds;
        b.Mode = Mode;
        b.LastTargetPath = TargetPath;
        b.Tests = Rows.Select(r => r.ToDefinition()).ToList();
        TrySaveSettings();

        foreach (var r in Rows) r.ClearResults();
        _lastReport = null;
        HasResults = false;
        IsRunning = true;
        Progress = 0;
        _cts = new CancellationTokenSource();

        var options = new BenchmarkOptions
        {
            TargetDirectory = TargetPath,
            TestFileSizeBytes = sizeBytes,
            Iterations = Iterations,
            DurationPerPass = TimeSpan.FromSeconds(DurationSeconds),
            IntervalBetweenTests = TimeSpan.FromSeconds(IntervalSeconds),
            Mode = Mode,
            Tests = Rows.Select(r => r.ToDefinition()).ToList(),
            DeleteTestFileAfterRun = b.DeleteTestFileAfterRun,
        };

        var progress = new Progress<BenchmarkProgress>(OnProgress);
        try
        {
            var report = await _services.Benchmark.RunAsync(options, progress,
                result => System.Windows.Application.Current.Dispatcher.Invoke(() => Rows[Rows.IndexOf(Rows.First(r => r.Matches(result.Test)))].SetResult(result)),
                _cts.Token);
            _lastReport = new BenchmarkReport
            {
                ApplicationVersion = AppInfo.Version,
                Timestamp = report.Timestamp,
                TargetDirectory = report.TargetDirectory,
                DriveDescription = Drives.FirstOrDefault(d => string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase))?.Label,
                OperatingSystem = report.OperatingSystem,
                TestFileSizeBytes = report.TestFileSizeBytes,
                Iterations = report.Iterations,
                DurationPerPassSeconds = report.DurationPerPassSeconds,
                Mode = report.Mode,
                Results = report.Results,
            };
            HasResults = _lastReport.Results.Count > 0;
            StatusText = L("Bench.Phase.Completed");
            Progress = 100;
        }
        catch (OperationCanceledException)
        {
            StatusText = L("Bench.Phase.Cancelled");
        }
        catch (InsufficientSpaceException ex)
        {
            var c = LanguageCatalog.CurrentCulture;
            StatusText = LF("Bench.Phase.Failed", LF("Bench.InsufficientSpace", ByteFormatter.FormatBinary(ex.Required, 1, c), ByteFormatter.FormatBinary(ex.Available, 1, c)));
        }
        catch (Exception ex)
        {
            StatusText = LF("Bench.Phase.Failed", ex.Message);
        }
        finally
        {
            foreach (var r in Rows) r.IsActive = false;
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
            UpdateFreeSpace();
        }
    }

    private void OnProgress(BenchmarkProgress p)
    {
        Progress = p.OverallPercent;
        BenchmarkRowViewModel? row = p.TestIndex >= 0 && p.TestIndex < Rows.Count ? Rows[p.TestIndex] : null;
        foreach (var r in Rows) r.IsActive = ReferenceEquals(r, row) && p.Phase is BenchmarkPhase.Reading or BenchmarkPhase.Writing;
        StatusText = p.Phase switch
        {
            BenchmarkPhase.Preparing => L("Bench.Phase.Preparing"),
            BenchmarkPhase.Reading => LF("Bench.Phase.Reading", row?.Label) + "  ·  " + LF("Bench.Iteration", p.Iteration, Iterations),
            BenchmarkPhase.Writing => LF("Bench.Phase.Writing", row?.Label) + "  ·  " + LF("Bench.Iteration", p.Iteration, Iterations),
            BenchmarkPhase.Interval => L("Bench.Phase.Interval"),
            BenchmarkPhase.CleaningUp => L("Bench.Phase.CleaningUp"),
            BenchmarkPhase.Completed => L("Bench.Phase.Completed"),
            _ => StatusText
        };
        if (row is not null && p.CurrentMBps is { } live && p.Phase is BenchmarkPhase.Reading or BenchmarkPhase.Writing)
            row.SetLive(p.IsRead, live);
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void CopyResults()
    {
        if (_lastReport is not null) _services.Ui.CopyToClipboard(BenchmarkReportFormatter.ToText(_lastReport));
    }

    [RelayCommand]
    private void Export(string format)
    {
        if (_lastReport is null) return;
        var stamp = _lastReport.Timestamp.ToString("yyyyMMdd-HHmmss");
        switch (format)
        {
            case "json":
                _services.Ui.SaveText($"ZeloraInfo-Benchmark-{stamp}.json", "JSON (*.json)|*.json", BenchmarkReportFormatter.ToJson(_lastReport));
                break;
            case "csv":
                _services.Ui.SaveText($"ZeloraInfo-Benchmark-{stamp}.csv", "CSV (*.csv)|*.csv", BenchmarkReportFormatter.ToCsv(_lastReport));
                break;
            default:
                _services.Ui.SaveText($"ZeloraInfo-Benchmark-{stamp}.txt", "Text (*.txt)|*.txt", BenchmarkReportFormatter.ToText(_lastReport));
                break;
        }
    }

    private void TrySaveSettings()
    {
        try
        {
            _services.Settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        var mode = Mode;
        BuildChoices();
        Mode = mode;
        foreach (var r in Rows) r.RefreshLanguage();
        if (!IsRunning) StatusText = L("Bench.Phase.Idle");
        RefreshDrives();
        UpdateFreeSpace();
    }

    public void Dispose() => _cts?.Cancel();
}

public sealed record DriveOption(string Root, string Label, bool IsSystem);

public sealed partial class BenchmarkRowViewModel : ObservableObject
{
    public BenchmarkRowViewModel(BenchmarkTestDefinition definition)
    {
        _access = definition.Access;
        _blockSizeKiB = definition.BlockSizeKiB;
        _queueDepth = definition.QueueDepth;
        _threads = definition.Threads;
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private BenchmarkAccess _access;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private int _blockSizeKiB;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private int _queueDepth;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private int _threads;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _readMBps = "—";
    [ObservableProperty] private string _writeMBps = "—";
    [ObservableProperty] private string _readDetail = "";
    [ObservableProperty] private string _writeDetail = "";

    public string Label => ToDefinition().Label;

    public BenchmarkTestDefinition ToDefinition() => new()
    {
        Access = Access,
        BlockSizeKiB = BlockSizeKiB,
        QueueDepth = QueueDepth,
        Threads = Threads,
    };

    public bool Matches(BenchmarkTestDefinition t)
        => t.Access == Access && t.BlockSizeKiB == BlockSizeKiB && t.QueueDepth == QueueDepth && t.Threads == Threads;

    public void ClearResults()
    {
        _read = _write = null;
        ReadMBps = WriteMBps = "—";
        ReadDetail = WriteDetail = "";
    }

    public void SetLive(bool isRead, double mbps)
    {
        if ((isRead ? _read : _write) is not null) return; // final value already set
        var text = mbps.ToString("N2", LanguageCatalog.CurrentCulture);
        if (isRead) ReadMBps = text; else WriteMBps = text;
    }

    private BenchmarkResult? _read, _write;

    public void SetResult(BenchmarkResult r)
    {
        if (r.IsRead) _read = r; else _write = r;
        RefreshLanguage();
    }

    public void RefreshLanguage()
    {
        var c = LanguageCatalog.CurrentCulture;
        string Detail(BenchmarkResult r) => $"{r.Iops.ToString("N0", c)} IOPS  ·  {r.AverageLatencyMicroseconds.ToString("N1", c)} µs";
        if (_read is not null)
        {
            ReadMBps = _read.MBps.ToString("N2", c);
            ReadDetail = Detail(_read);
        }
        if (_write is not null)
        {
            WriteMBps = _write.MBps.ToString("N2", c);
            WriteDetail = Detail(_write);
        }
    }

    partial void OnAccessChanged(BenchmarkAccess value) => ResetStored();
    partial void OnBlockSizeKiBChanged(int value) => ResetStored();
    partial void OnQueueDepthChanged(int value) => ResetStored();
    partial void OnThreadsChanged(int value) => ResetStored();

    private void ResetStored() => ClearResults();
}
