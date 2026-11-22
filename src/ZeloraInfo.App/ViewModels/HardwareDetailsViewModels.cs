// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeloraInfo.App.Services;
using ZeloraInfo.Core.Benchmark;
using ZeloraInfo.Core.Formatting;
using ZeloraInfo.Core.Hardware;
using ZeloraInfo.Core.Localization;

namespace ZeloraInfo.App.ViewModels;

// =====================================================================================
//  CPU details
// =====================================================================================

public sealed partial class CoreRow : ObservableObject
{
    public CoreRow(int index) => Index = index;
    public int Index { get; }
    public string Label => "#" + Index;
    [ObservableProperty] private string _frequency = "—";
    [ObservableProperty] private double _usage;
    [ObservableProperty] private string _usageText = "—";
}

public sealed partial class CpuDetailsViewModel(AppServices services) : LivePageViewModel(services)
{
    private CpuZSnapshot? _snapshot;
    private CpuZLiveSample? _live;
    private CancellationTokenSource? _benchCts;
    private CpuBenchmarkResult? _bench;

    public override string PageKey => "CpuDetails";

    public ObservableCollection<InfoRow> ClockRows { get; } = [];
    public ObservableCollection<string> Instructions { get; } = [];
    public ObservableCollection<CoreRow> CoreRows { get; } = [];

    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _subline = "";
    [ObservableProperty] private double _usage;
    [ObservableProperty] private string _usageText = "—";
    [ObservableProperty] private string? _note;
    [ObservableProperty] private bool _isBenchmarking;
    [ObservableProperty] private double _benchProgress;
    [ObservableProperty] private string _benchStatus = "";
    [ObservableProperty] private string _singleScore = "—";
    [ObservableProperty] private string _multiScore = "—";
    [ObservableProperty] private string _ratioText = "";

    protected override async Task LoadAsync(CancellationToken ct)
    {
        _snapshot = await Services.CpuZ.GetSnapshotAsync(ct);
        await Task.Run(() => _live = Services.CpuZ.SampleLive(), ct);
    }

    protected override void OnTick()
    {
        if (_snapshot is null) return;
        _ = Task.Run(() => Services.CpuZ.SampleLive()).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion) return;
            _live = t.Result;
            ApplyLive();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ApplyLive()
    {
        if (_live is null || _snapshot is null) return;
        var c = LanguageCatalog.CurrentCulture;
        Usage = _live.UsagePercent;
        UsageText = _live.UsagePercent.ToString("0", c) + " %";

        string Mhz(double? v) => v is null ? L("Common.NA") : v.Value.ToString("N0", c) + " MHz";
        var bus = _snapshot.BusClockMHz.ToString("0.0", c) + " MHz" + (_snapshot.BusClockEstimated ? " " + L("Cpu.Estimated") : "");
        var rows = new List<InfoRow>
        {
            new(L("Cpu.CoreSpeed"), Mhz(_live.AverageMHz)),
            new(L("Cpu.MaxCoreSpeed"), Mhz(_live.MaxMHz)),
            new(L("Cpu.Multiplier"), _live.Multiplier is { } m ? "× " + m.ToString("0.0", c) : L("Common.NA")),
            new(L("Cpu.BusSpeed"), bus),
            new(L("Cpu.BaseSpeed"), _snapshot.BaseClockMHz > 0 ? Mhz(_snapshot.BaseClockMHz) : L("Common.NA")),
            new(L("Cpu.MaxSpeed"), Mhz(_snapshot.MaxClockMHz)),
        };
        ClockRows.Clear();
        foreach (var r in rows) ClockRows.Add(r);

        if (CoreRows.Count != _live.Cores.Count)
        {
            CoreRows.Clear();
            foreach (var core in _live.Cores) CoreRows.Add(new CoreRow(core.Index));
        }
        for (var i = 0; i < _live.Cores.Count; i++)
        {
            var s = _live.Cores[i];
            CoreRows[i].Frequency = s.FrequencyMHz is { } f ? f.ToString("N0", c) + " MHz" : "—";
            CoreRows[i].Usage = s.UsagePercent ?? 0;
            CoreRows[i].UsageText = s.UsagePercent is { } u ? u.ToString("0", c) + " %" : "—";
        }
    }

    protected override void BuildSections()
    {
        if (_snapshot is null) return;
        var s = _snapshot;
        var c = LanguageCatalog.CurrentCulture;
        string NA(string? v) => string.IsNullOrWhiteSpace(v) ? L("Common.NA") : v!;
        string Hex(int? v) => v is null ? L("Common.NA") : $"{v:X} ({v})";

        Headline = s.Name;
        Subline = string.Join("  ·  ", new[] { s.Vendor, s.CodeName, s.ProcessNode, s.Package }.Where(x => !string.IsNullOrWhiteSpace(x)));
        Note = !s.CpuIdAvailable ? L("Cpu.NoCpuId") : s.HypervisorPresent ? L("Cpu.HypervisorNote") : null;

        var topology = new List<InfoRow>
        {
            new(L("Cpu.Packages"), s.Packages.ToString(c)),
            new(L("Cpu.Cores"), s.Cores.ToString(c)),
            new(L("Cpu.Threads"), s.Threads.ToString(c)),
        };
        if (s.PerformanceCores is { } p && s.EfficiencyCores is { } e)
            topology.Add(new InfoRow(L("Cpu.HybridLabel"), LF("Cpu.Hybrid", p, e)));
        topology.Add(new InfoRow(L("Cpu.Hypervisor"), s.HypervisorPresent ? L("Common.Yes") : L("Common.No")));

        var caches = s.Caches.Select(cache =>
        {
            var size = ByteFormatter.FormatBinary(cache.SizeBytes, 0, c);
            var assoc = cache.Associativity == 0xFF ? L("Cpu.FullyAssoc") : $"{cache.Associativity}-way";
            var value = $"{cache.Count} × {size}  ·  {assoc}  ·  {cache.LineSizeBytes} B line";
            return new InfoRow(cache.Label, value, LF("Cpu.SharedBy", cache.SharedByThreads));
        }).ToList();
        if (caches.Count == 0) caches.Add(new InfoRow(L("Cpu.Caches"), L("Common.NA")));

        ReplaceSections(
        [
            new InfoSection(L("Cpu.Processor"),
            [
                new InfoRow(L("Cpu.Name"), s.Name),
                new InfoRow(L("Cpu.CodeName"), NA(s.CodeName)),
                new InfoRow(L("Cpu.Package"), NA(s.Package)),
                new InfoRow(L("Cpu.Technology"), NA(s.ProcessNode)),
                new InfoRow(L("Cpu.CoreVoltage"), L("Common.NA"), L("Cpu.NA.Driver")),
                new InfoRow(L("Cpu.Specification"), s.Specification),
                new InfoRow(L("Cpu.Family"), Hex(s.Family)),
                new InfoRow(L("Cpu.Model"), Hex(s.Model)),
                new InfoRow(L("Cpu.Stepping"), Hex(s.Stepping)),
                new InfoRow(L("Cpu.ExtFamily"), Hex(s.ExtFamily)),
                new InfoRow(L("Cpu.ExtModel"), Hex(s.ExtModel)),
                new InfoRow(L("Cpu.Revision"), L("Common.NA"), L("Cpu.NA.Driver")),
                new InfoRow(L("Cpu.Microcode"), NA(s.MicrocodeRevision)),
            ]),
            new InfoSection(L("Cpu.Topology"), topology),
            new InfoSection(L("Cpu.Caches"), caches),
        ]);

        Instructions.Clear();
        foreach (var i in s.Instructions) Instructions.Add(i);
        ApplyLive();
        UpdateBenchTexts();
    }

    private bool CanBench() => !IsBenchmarking;

    [RelayCommand(CanExecute = nameof(CanBench))]
    private async Task RunBenchmarkAsync()
    {
        IsBenchmarking = true;
        RunBenchmarkCommand.NotifyCanExecuteChanged();
        CancelBenchmarkCommand.NotifyCanExecuteChanged();
        _benchCts = new CancellationTokenSource();
        _bench = null;
        UpdateBenchTexts();
        var seconds = Services.Settings.Current.Hardware.CpuBenchmarkSeconds;
        var progress = new Progress<CpuBenchmarkProgress>(p =>
        {
            BenchProgress = p.Percent;
            BenchStatus = LF("Cpu.Bench.Running", p.MultiThread ? LF("Cpu.Bench.Multi", Environment.ProcessorCount) : L("Cpu.Bench.Single"));
        });
        try
        {
            _bench = await Services.CpuZ.RunBenchmarkAsync(TimeSpan.FromSeconds(seconds), progress, _benchCts.Token);
            BenchStatus = L("Bench.Phase.Completed");
        }
        catch (OperationCanceledException)
        {
            BenchStatus = L("Cpu.Bench.Cancelled");
            BenchProgress = 0;
        }
        finally
        {
            IsBenchmarking = false;
            _benchCts.Dispose();
            _benchCts = null;
            RunBenchmarkCommand.NotifyCanExecuteChanged();
            CancelBenchmarkCommand.NotifyCanExecuteChanged();
            UpdateBenchTexts();
        }
    }

    private bool CanCancelBench() => IsBenchmarking;

    [RelayCommand(CanExecute = nameof(CanCancelBench))]
    private void CancelBenchmark() => _benchCts?.Cancel();

    private void UpdateBenchTexts()
    {
        var c = LanguageCatalog.CurrentCulture;
        SingleScore = _bench is null ? "—" : _bench.SingleThreadScore.ToString("N0", c);
        MultiScore = _bench is null ? "—" : _bench.MultiThreadScore.ToString("N0", c);
        RatioText = _bench is null ? "" : $"{L("Cpu.Bench.Ratio")}: × {_bench.MultiThreadRatio.ToString("0.00", c)}  ·  {LF("Cpu.Bench.Multi", _bench.Threads)}";
        if (!IsBenchmarking && _bench is null && string.IsNullOrEmpty(BenchStatus)) BenchStatus = L("Bench.Phase.Idle");
    }

    private string BuildText()
    {
        var sections = Sections.ToList();
        sections.Insert(1, new InfoSection(L("Cpu.Clocks"), ClockRows));
        sections.Add(new InfoSection(L("Cpu.Instructions"), [new InfoRow(L("Cpu.Instructions"), string.Join(", ", Instructions))]));
        if (_bench is not null)
            sections.Add(new InfoSection(L("Cpu.Bench"), [new InfoRow(L("Cpu.Bench.Single"), SingleScore), new InfoRow(LF("Cpu.Bench.Multi", _bench.Threads), MultiScore)]));
        return SectionsToText(Title, sections);
    }

    [RelayCommand]
    private void CopyReport() => Services.Ui.CopyToClipboard(BuildText());

    [RelayCommand]
    private void Export(string format)
    {
        if (_snapshot is null) return;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (format == "json")
        {
            var json = JsonSerializer.Serialize(new { application = "Zelora Info", version = AppInfo.Version, timestamp = DateTimeOffset.Now, cpu = _snapshot, live = _live, benchmark = _bench }, BenchmarkReportFormatter.JsonOptions);
            Services.Ui.SaveText($"ZeloraInfo-CPU-{stamp}.json", "JSON (*.json)|*.json", json);
        }
        else
        {
            Services.Ui.SaveText($"ZeloraInfo-CPU-{stamp}.txt", "Text (*.txt)|*.txt", BuildText());
        }
    }

    public override void OnLanguageChanged()
    {
        BenchStatus = "";
        base.OnLanguageChanged();
    }

    public override void Dispose()
    {
        _benchCts?.Cancel();
        base.Dispose();
    }
}

// =====================================================================================
//  RAM details
// =====================================================================================

public sealed class SlotRow
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required bool IsPopulated { get; init; }
    public required IReadOnlyList<InfoRow> Rows { get; init; }
}

public sealed partial class RamDetailsViewModel(AppServices services) : LivePageViewModel(services)
{
    private RamZSnapshot? _snapshot;

    public override string PageKey => "RamDetails";

    public ObservableCollection<SlotRow> Slots { get; } = [];

    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private string _usageText = "—";
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _slotsSummary = "";

    protected override async Task LoadAsync(CancellationToken ct) => _snapshot = await Services.RamZ.GetSnapshotAsync(ct);

    protected override void OnTick()
    {
        var s = Services.RamZ.GetStatus();
        var c = LanguageCatalog.CurrentCulture;
        UsagePercent = s.LoadPercent;
        UsageText = $"{ByteFormatter.FormatBinary(s.UsedPhysicalBytes, 1, c)} / {ByteFormatter.FormatBinary(s.TotalPhysicalBytes, 1, c)}  ({s.LoadPercent} %)";
    }

    protected override void BuildSections()
    {
        if (_snapshot is null) return;
        var s = _snapshot;
        var c = LanguageCatalog.CurrentCulture;
        var na = L("Common.NA");
        var mask = Services.Settings.Current.Hardware.MaskSerialNumbers;
        string MTs(int? v) => v is > 0 ? $"{v} MT/s" : na;

        Headline = $"{ByteFormatter.FormatBinary(s.TotalInstalledBytes, 0, c)}  {s.MemoryType}" + (s.ConfiguredSpeedMTs is > 0 ? $"  @ {s.ConfiguredSpeedMTs} MT/s" : "");
        SlotsSummary = s.TotalSlots is { } total ? LF("Ram.SlotsSummary", s.PopulatedSlots, total) : "";

        var channel = s.ChannelModeInferred && s.ChannelMode != "—" ? LF("Ram.ChannelInferred", s.ChannelMode) : s.ChannelMode;
        var general = new InfoSection(L("Ram.General"),
        [
            new InfoRow(L("Ram.Type"), s.MemoryType),
            new InfoRow(L("Ram.Size"), ByteFormatter.FormatBinary(s.TotalInstalledBytes, 0, c)),
            new InfoRow(L("Ram.Channels"), channel),
            new InfoRow(L("Ram.DramFreq"), s.DramFrequencyMHz is { } f ? f.ToString("N1", c) + " MHz" : na),
            new InfoRow(L("Ram.Effective"), MTs(s.ConfiguredSpeedMTs)),
            new InfoRow(L("Ram.Rated"), MTs(s.RatedSpeedMTs)),
            new InfoRow(L("Ram.Uncore"), na, L("Cpu.NA.Driver")),
            new InfoRow(L("Ram.Ecc"), s.Ecc is null ? na : s.Ecc.Value ? L("Common.Yes") : L("Common.No")),
            new InfoRow(L("Mem.MaxCapacityLabel"), s.MaxCapacityBytes is { } max ? ByteFormatter.FormatBinary(max, 0, c) : na),
            new InfoRow(L("Ram.Source"), s.Source),
        ]);

        var t = s.Timings;
        string Clk(int? v) => v is null ? na : v.Value.ToString(c);
        var timings = new InfoSection(L("Ram.Timings"),
        [
            new InfoRow(L("Ram.tCL"), Clk(t.Cl)),
            new InfoRow(L("Ram.tRCD"), Clk(t.Rcd)),
            new InfoRow(L("Ram.tRP"), Clk(t.Rp)),
            new InfoRow(L("Ram.tRAS"), Clk(t.Ras)),
            new InfoRow(L("Ram.CR"), t.CommandRate ?? na),
            new InfoRow(L("Ram.tRFC"), Clk(t.Rfc)),
            new InfoRow(L("Ram.Vdd"), t.Vdd is { } v ? v.ToString("0.000", c) + " V  (SMBIOS)" : na),
            new InfoRow(L("Ram.Vddq"), t.Vddq is { } q ? q.ToString("0.000", c) + " V" : na),
            new InfoRow(L("Ram.Profile"), na),
            new InfoRow("", L("Ram.TimingsNote")),
        ]);

        var pageRows = s.PageFiles.Count == 0
            ? new List<InfoRow> { new(L("Mem.PageFile.Path"), L("Mem.PageFile.None")) }
            : s.PageFiles.SelectMany(p => new[]
            {
                new InfoRow(L("Mem.PageFile.Path"), p.Path),
                new InfoRow(L("Mem.PageFile.Allocated"), ByteFormatter.FormatBinary(p.AllocatedBytes, 1, c)),
                new InfoRow(L("Mem.PageFile.Current"), ByteFormatter.FormatBinary(p.CurrentUsageBytes, 1, c)),
            }).ToList();
        pageRows.Insert(0, new InfoRow(L("Mem.Commit"), $"{ByteFormatter.FormatBinary(s.Status.CommitTotalBytes, 1, c)} / {ByteFormatter.FormatBinary(s.Status.CommitLimitBytes, 1, c)}"));

        ReplaceSections([general, timings, new InfoSection(L("Mem.PageFile"), pageRows)]);

        Slots.Clear();
        foreach (var slot in s.Slots)
        {
            var title = string.Join("  ·  ", new[] { slot.Locator, slot.Bank }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (string.IsNullOrWhiteSpace(title)) title = L("Mem.Slot");
            if (!slot.IsPopulated)
            {
                Slots.Add(new SlotRow { Title = title, Summary = L("Ram.Empty"), IsPopulated = false, Rows = [] });
                continue;
            }
            var volts = new[] { slot.MinVoltage, slot.MaxVoltage, slot.ConfiguredVoltage }.Any(v => v is not null)
                ? string.Join(" / ", new[] { slot.MinVoltage, slot.MaxVoltage, slot.ConfiguredVoltage }.Select(v => v is null ? "—" : v.Value.ToString("0.00", c) + " V"))
                : na;
            Slots.Add(new SlotRow
            {
                Title = title,
                Summary = $"{ByteFormatter.FormatBinary(slot.CapacityBytes, 0, c)}  {slot.MemoryType}  {slot.FormFactor}" + (slot.ConfiguredSpeedMTs is > 0 ? $"  @ {slot.ConfiguredSpeedMTs} MT/s" : ""),
                IsPopulated = true,
                Rows =
                [
                    new InfoRow(L("Mem.Capacity"), ByteFormatter.FormatBinary(slot.CapacityBytes, 0, c)),
                    new InfoRow(L("Ram.ModuleType"), $"{slot.MemoryType} {slot.TypeDetail}".Trim()),
                    new InfoRow(L("Mem.FormFactor"), slot.FormFactor),
                    new InfoRow(L("Ram.Rated"), MTs(slot.RatedSpeedMTs) + (slot.RatedSpeedMTs is > 0 ? $"  ({slot.RatedSpeedMTs / 2} MHz)" : "")),
                    new InfoRow(L("Mem.ConfiguredSpeed"), MTs(slot.ConfiguredSpeedMTs)),
                    new InfoRow(L("Ram.Profile"), na, L("Ram.ProfileNA")),
                    new InfoRow(L("Mem.Manufacturer"), slot.Manufacturer),
                    new InfoRow(L("Mem.PartNumber"), slot.PartNumber),
                    new InfoRow(L("Mem.Serial"), mask ? SerialMasker.Mask(slot.SerialNumber) : slot.SerialNumber),
                    new InfoRow(L("Ram.Rank"), slot.Rank?.ToString(c) ?? na),
                    new InfoRow(L("Ram.Width"), slot.DataWidth is null && slot.TotalWidth is null ? na : $"{slot.DataWidth?.ToString() ?? "—"} / {slot.TotalWidth?.ToString() ?? "—"} bit"),
                    new InfoRow(L("Ram.Channel"), slot.Channel ?? na),
                    new InfoRow(L("Ram.Voltage"), volts),
                ],
            });
        }
    }

    private string BuildText()
    {
        var sections = Sections.ToList();
        sections.AddRange(Slots.Select(sl => new InfoSection($"{L("Ram.Slots")}: {sl.Title}", sl.IsPopulated ? sl.Rows : [new InfoRow("", sl.Summary)])));
        return SectionsToText(Title, sections);
    }

    [RelayCommand]
    private void CopyReport() => Services.Ui.CopyToClipboard(BuildText());

    [RelayCommand]
    private void Export(string format)
    {
        if (_snapshot is null) return;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (format == "json")
        {
            var mask = Services.Settings.Current.Hardware.MaskSerialNumbers;
            var slots = _snapshot.Slots.Select(s => new
            {
                s.Locator, s.Bank, s.IsPopulated, s.CapacityBytes, s.MemoryType, s.TypeDetail, s.FormFactor, s.Technology,
                s.RatedSpeedMTs, s.ConfiguredSpeedMTs, s.Manufacturer, s.PartNumber,
                SerialNumber = mask ? SerialMasker.Mask(s.SerialNumber) : s.SerialNumber,
                s.Rank, s.DataWidth, s.TotalWidth, s.ConfiguredVoltage, s.MinVoltage, s.MaxVoltage, s.Channel,
            });
            var json = JsonSerializer.Serialize(new
            {
                application = "Zelora Info",
                version = AppInfo.Version,
                timestamp = DateTimeOffset.Now,
                memory = new
                {
                    _snapshot.Source, _snapshot.MemoryType, _snapshot.TotalInstalledBytes, _snapshot.ChannelMode, _snapshot.ChannelCount,
                    _snapshot.ChannelModeInferred, _snapshot.ConfiguredSpeedMTs, _snapshot.DramFrequencyMHz, _snapshot.RatedSpeedMTs,
                    _snapshot.Ecc, _snapshot.TotalSlots, _snapshot.PopulatedSlots, _snapshot.MaxCapacityBytes, _snapshot.Timings, _snapshot.Status, _snapshot.PageFiles,
                },
                slots,
            }, BenchmarkReportFormatter.JsonOptions);
            Services.Ui.SaveText($"ZeloraInfo-RAM-{stamp}.json", "JSON (*.json)|*.json", json);
        }
        else
        {
            Services.Ui.SaveText($"ZeloraInfo-RAM-{stamp}.txt", "Text (*.txt)|*.txt", BuildText());
        }
    }
}
