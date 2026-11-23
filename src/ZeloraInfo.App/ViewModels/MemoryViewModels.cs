// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeloraInfo.App.Services;
using ZeloraInfo.Core.Benchmark;
using ZeloraInfo.Core.Formatting;
using ZeloraInfo.Core.Hardware;
using ZeloraInfo.Core.Localization;

namespace ZeloraInfo.App.ViewModels;

/// <summary>
/// RAM page host with three tabs: Overview, Details / SPD and Memory Map (RAMMap style).
/// Only the visible tab runs its live timer.
/// </summary>
public sealed partial class MemoryViewModel : ViewModelBase, IDisposable
{
    public const int OverviewTab = 0;
    public const int DetailsTab = 1;
    public const int MapTab = 2;

    private bool _active;

    public MemoryViewModel(AppServices services)
    {
        Overview = new MemoryOverviewViewModel(services);
        Details = new RamDetailsViewModel(services);
        Map = new MemoryMapViewModel(services);
    }

    public override string PageKey => "Memory";

    public MemoryOverviewViewModel Overview { get; }
    public RamDetailsViewModel Details { get; }
    public MemoryMapViewModel Map { get; }

    [ObservableProperty] private int _selectedTabIndex;

    private ViewModelBase Tab(int index) => index switch
    {
        DetailsTab => Details,
        MapTab => Map,
        _ => Overview
    };

    partial void OnSelectedTabIndexChanged(int oldValue, int newValue)
    {
        if (!_active) return;
        Tab(oldValue).OnDeactivated();
        Tab(newValue).OnActivated();
    }

    public override void OnActivated()
    {
        _active = true;
        Tab(SelectedTabIndex).OnActivated();
    }

    public override void OnDeactivated()
    {
        _active = false;
        Tab(SelectedTabIndex).OnDeactivated();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Overview.OnLanguageChanged();
        Details.OnLanguageChanged();
        Map.OnLanguageChanged();
    }

    public void Dispose()
    {
        Overview.Dispose();
        Details.Dispose();
        Map.Dispose();
    }
}

// =====================================================================================
//  Memory map (RAMMap style)
// =====================================================================================

public sealed class CompositionSegment
{
    public required string Label { get; init; }
    public required string BrushKey { get; init; }
    public ulong Bytes { get; init; }
    public double Percent { get; init; }
    public string BytesText { get; init; } = "";
    public string PercentText { get; init; } = "";
    public System.Windows.Media.Brush Brush => System.Windows.Application.Current.TryFindResource(BrushKey) as System.Windows.Media.Brush
                                               ?? System.Windows.Media.Brushes.Gray;
}

public sealed class BarRow
{
    public required string Label { get; init; }
    public string Value { get; init; } = "";
    public string? Detail { get; init; }
    public double Percent { get; init; }
}

public sealed class ProcessRow(MemoryProcessInfo p)
{
    public string Name { get; } = string.IsNullOrEmpty(p.Name) ? $"PID {p.ProcessId}" : p.Name;
    public int ProcessId { get; } = p.ProcessId;
    public int SessionId { get; } = p.SessionId;
    public ulong WorkingSet { get; } = p.WorkingSet;
    public ulong PrivateWorkingSet { get; } = p.PrivateWorkingSet;
    public ulong SharedWorkingSet { get; } = p.SharedWorkingSet;
    public ulong PrivateBytes { get; } = p.PrivateBytes;
    public ulong PagedPool { get; } = p.PagedPool;
    public ulong NonPagedPool { get; } = p.NonPagedPool;
    public uint PageFaults { get; } = p.PageFaults;
}

public sealed class RangeRow(PhysicalRange r)
{
    public string Start { get; } = $"0x{r.Start:X12}";
    public string End { get; } = $"0x{r.End:X12}";
    public ulong Length { get; } = r.Length;
}

public sealed partial class MemoryMapViewModel : LivePageViewModel
{
    private MemoryMapSnapshot? _snapshot;
    private IReadOnlyList<MemoryProcessInfo> _processes = [];
    private IReadOnlyList<PhysicalRange> _ranges = [];
    private bool _sampling;

    public MemoryMapViewModel(AppServices services) : base(services)
    {
        ProcessView = CollectionViewSource.GetDefaultView(Processes);
        ProcessView.Filter = o => o is ProcessRow r && (string.IsNullOrWhiteSpace(ProcessFilter)
            || r.Name.Contains(ProcessFilter, StringComparison.OrdinalIgnoreCase) || r.ProcessId.ToString().Contains(ProcessFilter));
        ProcessView.SortDescriptions.Add(new SortDescription(nameof(ProcessRow.WorkingSet), ListSortDirection.Descending));
    }

    public override string PageKey => "MemoryMap";

    public ObservableCollection<CompositionSegment> Composition { get; } = [];
    public ObservableCollection<BarRow> ListRows { get; } = [];
    public ObservableCollection<BarRow> PriorityRows { get; } = [];
    public ObservableCollection<BarRow> ConsumerRows { get; } = [];
    public ObservableCollection<InfoRow> SystemRows { get; } = [];
    public ObservableCollection<ProcessRow> Processes { get; } = [];
    public ObservableCollection<RangeRow> Ranges { get; } = [];
    public ICollectionView ProcessView { get; }

    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private string _processSummary = "";
    [ObservableProperty] private string _rangeSummary = "";
    [ObservableProperty] private string _processFilter = "";
    [ObservableProperty] private bool _isEmptying;

    public bool IsAdministrator => Services.Privileges.IsAdministrator;
    public bool CanEmpty => IsAdministrator && !IsEmptying;

    partial void OnProcessFilterChanged(string value) => ProcessView.Refresh();
    partial void OnIsEmptyingChanged(bool value) => OnPropertyChanged(nameof(CanEmpty));

    protected override async Task LoadAsync(CancellationToken ct)
    {
        var map = Services.MemoryMap;
        (_snapshot, _processes, _ranges) = await Task.Run(() => (map.GetSnapshot(), map.GetProcesses(), map.GetPhysicalRanges()), ct);
    }

    protected override void OnTick()
    {
        if (!Loaded || _sampling) return;
        _sampling = true;
        _ = Task.Run(Services.MemoryMap.GetSnapshot).ContinueWith(t =>
        {
            _sampling = false;
            if (t.Status != TaskStatus.RanToCompletion) return;
            _snapshot = t.Result;
            BuildSnapshot();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void BuildSections()
    {
        BuildSnapshot();
        BuildProcesses();
        BuildRanges();
    }

    private void BuildSnapshot()
    {
        var s = _snapshot;
        if (s is null) return;
        var c = LanguageCatalog.CurrentCulture;
        string B(ulong v) => ByteFormatter.FormatBinary(v, 1, c);
        var total = Math.Max(1d, s.InstalledBytes);
        double Pct(ulong v) => v * 100d / total;

        SourceText = s.Source == Core.Hardware.MemoryListSource.Kernel ? L("Map.Source.Kernel") : L("Map.Source.Counters");
        TotalText = LF("Map.Total", B(s.InstalledBytes), B(s.TotalPhysicalBytes));

        // Stacked bar (Resource Monitor style)
        var segments = new (string Key, string Brush, ulong Bytes)[]
        {
            ("Map.InUse", "Brush.Accent", s.InUseBytes),
            ("Map.Modified", "Brush.Caution", s.Lists.Modified),
            ("Map.Standby", "Brush.Write", s.Lists.Standby),
            ("Map.Free", "Brush.Good", s.Lists.FreeAndZeroed),
            ("Map.HardwareReserved", "Brush.Unknown", s.HardwareReservedBytes),
        };
        Composition.Clear();
        foreach (var (key, brush, bytes) in segments)
            Composition.Add(new CompositionSegment { Label = L(key), BrushKey = brush, Bytes = bytes, Percent = Pct(bytes), BytesText = B(bytes), PercentText = Pct(bytes).ToString("0.0", c) + " %" });

        // Page lists
        ListRows.Clear();
        var physical = Math.Max(1d, s.TotalPhysicalBytes);
        void List(string key, ulong v, string? detail = null) => ListRows.Add(new BarRow { Label = L(key), Value = B(v), Percent = v * 100d / physical, Detail = detail });
        if (s.Source == Core.Hardware.MemoryListSource.Kernel)
        {
            List("Map.Zeroed", s.Lists.Zeroed);
            List("Map.FreeList", s.Lists.Free);
        }
        else
        {
            List("Map.FreeZeroed", s.Lists.FreeAndZeroed);
        }
        List("Map.Modified", s.Lists.Modified);
        if (s.Source == Core.Hardware.MemoryListSource.Kernel) List("Map.ModifiedNoWrite", s.Lists.ModifiedNoWrite);
        List("Map.Standby", s.Lists.Standby);
        if (s.Source == Core.Hardware.MemoryListSource.Kernel) List("Map.Bad", s.Lists.Bad);

        // Standby by priority
        PriorityRows.Clear();
        var standby = Math.Max(1d, s.Lists.Standby);
        foreach (var p in s.Lists.StandbyByPriority)
        {
            PriorityRows.Add(new BarRow
            {
                Label = LF("Map.Priority", p.Priority),
                Value = B(p.Bytes),
                Percent = p.Bytes * 100d / standby,
                Detail = p.Repurposed is { } r ? LF("Map.Repurposed", B(r)) : null,
            });
        }

        // Active memory consumers
        ConsumerRows.Clear();
        foreach (var cons in s.Consumers)
            ConsumerRows.Add(new BarRow { Label = L("Map.Consumer." + cons.Key), Value = B(cons.Bytes), Percent = cons.Bytes * 100d / physical });

        SystemRows.Clear();
        SystemRows.Add(new InfoRow(L("Map.Commit"), $"{B(s.CommitTotal)} / {B(s.CommitLimit)}"));
        SystemRows.Add(new InfoRow(L("Map.CommitPeak"), B(s.CommitPeak)));
        SystemRows.Add(new InfoRow(L("Map.Available"), B(s.AvailableBytes)));
        SystemRows.Add(new InfoRow(L("Map.PageSize"), $"{s.PageSize.ToString("N0", c)} B"));
        SystemRows.Add(new InfoRow(L("Map.Processes"), s.ProcessCount.ToString("N0", c)));
        SystemRows.Add(new InfoRow(L("Map.Threads"), s.ThreadCount.ToString("N0", c)));
        SystemRows.Add(new InfoRow(L("Map.Handles"), s.HandleCount.ToString("N0", c)));
    }

    private void BuildProcesses()
    {
        Processes.Clear();
        foreach (var p in _processes.Where(p => p.ProcessId != 0)) Processes.Add(new ProcessRow(p));
        var c = LanguageCatalog.CurrentCulture;
        ProcessSummary = LF("Map.ProcessSummary", Processes.Count.ToString("N0", c),
            ByteFormatter.FormatBinary(_processes.Aggregate(0UL, (a, p) => a + p.WorkingSet), 1, c),
            ByteFormatter.FormatBinary(_processes.Aggregate(0UL, (a, p) => a + p.PrivateWorkingSet), 1, c));
    }

    private void BuildRanges()
    {
        Ranges.Clear();
        foreach (var r in _ranges) Ranges.Add(new RangeRow(r));
        RangeSummary = _ranges.Count == 0 ? L("Common.NA")
            : LF("Map.RangeSummary", _ranges.Count, ByteFormatter.FormatBinary(_ranges.Aggregate(0UL, (a, r) => a + r.Length), 1, LanguageCatalog.CurrentCulture));
    }

    // ------------------------------------------------------------------ empty actions

    [RelayCommand]
    private async Task EmptyAsync(string actionName)
    {
        if (!Enum.TryParse<MemoryEmptyAction>(actionName, out var action) || !CanEmpty) return;
        var title = L("Map.Empty." + action);
        if (!Services.Ui.Confirm(title, L("Map.Empty." + action + ".Desc") + Environment.NewLine + Environment.NewLine + L("Map.Empty.Confirm"))) return;

        IsEmptying = true;
        try
        {
            var result = await Task.Run(() => Services.MemoryMap.Empty(action));
            if (result.Success)
                Services.Ui.Toast(LF("Map.Empty.Done", title, ByteFormatter.FormatBinary(result.FreedBytes, 1, LanguageCatalog.CurrentCulture)));
            else
                Services.Ui.ShowError(LF("Map.Empty.Failed", title, result.Error));
            await RefreshAsync();
        }
        finally
        {
            IsEmptying = false;
        }
    }

    [RelayCommand]
    private void RestartAsAdmin() => App.RestartElevated();

    // ------------------------------------------------------------------ export

    private string BuildText()
    {
        var c = LanguageCatalog.CurrentCulture;
        var sections = new List<InfoSection>
        {
            new(L("Map.Composition"), Composition.Select(s => new InfoRow(s.Label, $"{s.BytesText}  ({s.PercentText})"))),
            new(L("Map.Lists"), ListRows.Select(r => new InfoRow(r.Label, r.Value))),
            new(L("Map.Priorities"), PriorityRows.Select(r => new InfoRow(r.Label, r.Value, r.Detail))),
            new(L("Map.Consumers"), ConsumerRows.Select(r => new InfoRow(r.Label, r.Value))),
            new(L("Map.System"), SystemRows),
            new(L("Map.Ranges"), Ranges.Select(r => new InfoRow($"{r.Start} – {r.End}", ByteFormatter.FormatBinary(r.Length, 1, c)))),
            new(L("Map.Processes"), _processes.OrderByDescending(p => p.WorkingSet).Take(30)
                .Select(p => new InfoRow($"{p.Name} ({p.ProcessId})", $"WS {ByteFormatter.FormatBinary(p.WorkingSet, 1, c)} · Private WS {ByteFormatter.FormatBinary(p.PrivateWorkingSet, 1, c)} · Private {ByteFormatter.FormatBinary(p.PrivateBytes, 1, c)}"))),
        };
        return SectionsToText(L("Map.Title") + " — " + SourceText, sections);
    }

    [RelayCommand]
    private void CopyReport() => Services.Ui.CopyToClipboard(BuildText());

    [RelayCommand]
    private void Export(string format)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (format == "json")
        {
            var json = JsonSerializer.Serialize(new { application = "Zelora Info", version = AppInfo.Version, timestamp = DateTimeOffset.Now, snapshot = _snapshot, processes = _processes, physicalRanges = _ranges },
                BenchmarkReportFormatter.JsonOptions);
            Services.Ui.SaveText($"ZeloraInfo-MemoryMap-{stamp}.json", "JSON (*.json)|*.json", json);
        }
        else
        {
            Services.Ui.SaveText($"ZeloraInfo-MemoryMap-{stamp}.txt", "Text (*.txt)|*.txt", BuildText());
        }
    }
}
