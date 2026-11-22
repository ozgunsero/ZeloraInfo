// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ZeloraInfo.App.Services;
using ZeloraInfo.Core.Formatting;
using ZeloraInfo.Core.Localization;
using ZeloraInfo.Core.SystemInfo;

namespace ZeloraInfo.App.ViewModels;

/// <summary>Shared helpers for pages that rebuild <see cref="InfoSection"/>s and run a live timer.</summary>
public abstract partial class LivePageViewModel : ViewModelBase, IDisposable
{
    protected readonly AppServices Services;
    private readonly DispatcherTimer _timer = new();
    protected CancellationTokenSource? Cts;

    protected LivePageViewModel(AppServices services)
    {
        Services = services;
        _timer.Tick += (_, _) => OnTick();
        services.Settings.SettingsChanged += (_, _) => _timer.Interval = TimeSpan.FromMilliseconds(services.Settings.Current.Hardware.LiveRefreshMs);
    }

    public ObservableCollection<InfoSection> Sections { get; } = [];

    protected bool Loaded { get; set; }

    public override void OnActivated()
    {
        if (!Loaded) _ = RefreshAsync();
        _timer.Interval = TimeSpan.FromMilliseconds(Services.Settings.Current.Hardware.LiveRefreshMs);
        _timer.Start();
        OnTick();
    }

    public override void OnDeactivated() => _timer.Stop();

    protected virtual void OnTick()
    {
    }

    protected abstract Task LoadAsync(CancellationToken ct);

    protected abstract void BuildSections();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        Cts?.Cancel();
        Cts = new CancellationTokenSource();
        try
        {
            await LoadAsync(Cts.Token);
            Loaded = true;
            BuildSections();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = LF("Common.LoadFailed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected void ReplaceSections(IEnumerable<InfoSection> sections)
    {
        Sections.Clear();
        foreach (var s in sections) Sections.Add(s);
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        if (Loaded) BuildSections();
        OnTick();
    }

    protected static string SectionsToText(string title, IEnumerable<InfoSection> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Zelora Info {AppInfo.Version} — {title}");
        sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        foreach (var s in sections)
        {
            sb.AppendLine();
            sb.AppendLine($"[{s.Title}]");
            foreach (var r in s.Rows) sb.AppendLine($"  {r.Label,-30}: {r.Value}");
        }
        return sb.ToString();
    }

    protected static string Date(DateTime? d) => d is null ? "—" : d.Value.ToString("g", LanguageCatalog.CurrentCulture);
    protected static string YesNo(bool? b) => b is null ? "—" : LanguageCatalog.Get(b.Value ? "Common.Enabled" : "Common.Disabled");

    public virtual void Dispose()
    {
        _timer.Stop();
        Cts?.Cancel();
    }
}

// =====================================================================================
//  System
// =====================================================================================

public sealed partial class SystemViewModel(AppServices services) : LivePageViewModel(services)
{
    private SystemSnapshot? _snapshot;

    public override string PageKey => "System";

    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private string _cpuUsageText = "—";
    [ObservableProperty] private string _clockText = "—";
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private string _osShort = "—";

    protected override async Task LoadAsync(CancellationToken ct) => _snapshot = await Services.SystemInfo.GetSnapshotAsync(ct);

    protected override void OnTick()
    {
        var sample = Services.SystemInfo.SampleLive();
        var c = LanguageCatalog.CurrentCulture;
        CpuUsage = sample.CpuUsagePercent;
        CpuUsageText = sample.CpuUsagePercent.ToString("0", c) + " %";
        ClockText = sample.CurrentClockMHz is { } mhz ? (mhz / 1000d).ToString("0.00", c) + " GHz" : "—";
        UptimeText = DurationFormatter.Format(sample.Uptime, L("Common.DaysShort"));
    }

    protected override void BuildSections()
    {
        if (_snapshot is null) return;
        var s = _snapshot;
        var c = LanguageCatalog.CurrentCulture;
        OsShort = $"{s.Os.Name} {s.Os.DisplayVersion}".Trim();
        var sections = new List<InfoSection>
        {
            new(L("Sys.OsSection"),
            [
                new InfoRow(L("Sys.OsName"), s.Os.Name),
                new InfoRow(L("Sys.DisplayVersion"), s.Os.DisplayVersion),
                new InfoRow(L("Sys.Version"), s.Os.Version),
                new InfoRow(L("Sys.Build"), s.Os.Build),
                new InfoRow(L("Sys.Architecture"), s.Os.Architecture),
                new InfoRow(L("Sys.ComputerName"), s.Os.ComputerName),
                new InfoRow(L("Sys.UserName"), s.Os.UserName),
                new InfoRow(L("Sys.LastBoot"), Date(s.Os.LastBootTime)),
                new InfoRow(L("Sys.InstallDate"), Date(s.Os.InstallDate)),
                new InfoRow(L("Sys.Firmware"), s.Os.FirmwareType),
                new InfoRow(L("Sys.SecureBoot"), YesNo(s.Os.SecureBootEnabled)),
            ]),
            new(L("Sys.CpuSection"),
            [
                new InfoRow(L("Sys.CpuName"), s.Cpu.Name),
                new InfoRow(L("Sys.Cores"), s.Cpu.Cores.ToString(c)),
                new InfoRow(L("Sys.Threads"), s.Cpu.LogicalProcessors.ToString(c)),
                new InfoRow(L("Sys.BaseClock"), s.Cpu.BaseClockMHz > 0 ? $"{s.Cpu.BaseClockMHz.ToString("N0", c)} MHz" : null),
                new InfoRow(L("Sys.L2"), s.Cpu.L2CacheKiB is { } l2 ? ByteFormatter.FormatBinary(l2 * 1024d, 1, c) : null),
                new InfoRow(L("Sys.L3"), s.Cpu.L3CacheKiB is { } l3 ? ByteFormatter.FormatBinary(l3 * 1024d, 1, c) : null),
                new InfoRow(L("Sys.Socket"), s.Cpu.Socket),
                new InfoRow(L("Sys.Virtualization"), YesNo(s.Cpu.VirtualizationEnabled)),
            ]),
            new(L("Sys.BoardSection"),
            [
                new InfoRow(L("Sys.SystemManufacturer"), s.Board.SystemManufacturer),
                new InfoRow(L("Sys.SystemModel"), s.Board.SystemModel),
                new InfoRow(L("Sys.BoardManufacturer"), s.Board.BoardManufacturer),
                new InfoRow(L("Sys.BoardModel"), $"{s.Board.BoardProduct} {s.Board.BoardVersion}".Trim()),
                new InfoRow(L("Sys.BiosVendor"), s.Board.BiosVendor),
                new InfoRow(L("Sys.BiosVersion"), s.Board.BiosVersion),
                new InfoRow(L("Sys.BiosDate"), s.Board.BiosReleaseDate?.ToString("d", c)),
            ]),
        };
        foreach (var g in s.Gpus)
        {
            sections.Add(new InfoSection($"{L("Sys.GpuSection")} — {g.Name}",
            [
                new InfoRow(L("Sys.GpuName"), g.Name),
                new InfoRow(L("Sys.GpuMemory"), g.DedicatedMemoryBytes is { } m ? ByteFormatter.FormatBinary(m, 1, c) : null),
                new InfoRow(L("Sys.GpuDriver"), g.DriverVersion),
                new InfoRow(L("Sys.GpuDriverDate"), g.DriverDate?.ToString("d", c)),
                new InfoRow(L("Sys.GpuMode"), g.CurrentMode),
            ]));
        }
        ReplaceSections(sections);
    }

    [RelayCommand]
    private void CopyReport() => Services.Ui.CopyToClipboard(SectionsToText(Title, Sections));
}

// =====================================================================================
//  Display
// =====================================================================================

public sealed partial class DisplayViewModel : LivePageViewModel
{
    private DisplaySnapshot? _snapshot;

    public DisplayViewModel(AppServices services) : base(services)
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public override string PageKey => "Display";

    [ObservableProperty] private string _summary = "";

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => System.Windows.Application.Current?.Dispatcher.BeginInvoke(async () => await RefreshAsync());

    protected override Task LoadAsync(CancellationToken ct)
    {
        _snapshot = Services.Display.GetSnapshot();
        return Task.CompletedTask;
    }

    protected override void BuildSections()
    {
        if (_snapshot is null) return;
        var c = LanguageCatalog.CurrentCulture;
        Summary = LF("Disp.Count", _snapshot.Displays.Count) + "   ·   " +
                  LF("Disp.VirtualDesktop", _snapshot.VirtualWidth, _snapshot.VirtualHeight, _snapshot.VirtualLeft, _snapshot.VirtualTop);

        var sections = new List<InfoSection>();
        var n = 1;
        foreach (var d in _snapshot.Displays)
        {
            var hdr = d.HdrSupported switch
            {
                true when d.HdrEnabled == true => L("Disp.HdrOn"),
                true => L("Disp.HdrOff"),
                false => L("Disp.HdrNo"),
                _ => null
            };
            var size = d.PhysicalWidthCm is { } w && d.PhysicalHeightCm is { } h
                ? $"{w.ToString("0", c)} × {h.ToString("0", c)} cm" + (d.DiagonalInches is { } inch ? $"  (≈ {inch.ToString("0.0", c)}\")" : "")
                : null;
            string Hz(double? v) => v is null ? "—" : v.Value.ToString("0.##", c) + " Hz";

            sections.Add(new InfoSection($"{n++}. {d.FriendlyName}{(d.IsPrimary ? "  ★ " + L("Disp.Primary") : "")}",
            [
                new InfoRow(L("Disp.Resolution"), $"{d.Width} × {d.Height}"),
                new InfoRow(L("Disp.Preferred"), d.PreferredWidth is { } pw && d.PreferredHeight is { } ph ? $"{pw} × {ph}" + (d.PreferredRefreshRateHz is { } pr ? $" @ {Hz(pr)}" : "") : null),
                new InfoRow(L("Disp.Refresh"), Hz(d.RefreshRateHz)),
                new InfoRow(L("Disp.MaxRefresh"), Hz(d.MaxRefreshRateHz)),
                new InfoRow(L("Disp.Scale"), $"{d.ScalePercent} %  ({d.DpiX} DPI)"),
                new InfoRow(L("Disp.ColorDepth"), $"{d.BitsPerPixel} bpp" + (d.BitsPerColorChannel is { } bpc ? "  ·  " + LF("Disp.BitsPerChannel", bpc) : "")),
                new InfoRow(L("Disp.Hdr"), hdr),
                new InfoRow(L("Disp.Size"), size),
                new InfoRow(L("Disp.Aspect"), d.AspectRatio),
                new InfoRow(L("Disp.Orientation"), L("Disp.Orientation." + d.Orientation)),
                new InfoRow(L("Disp.Connection"), d.ConnectionType),
                new InfoRow(L("Disp.Adapter"), d.AdapterName),
                new InfoRow(L("Disp.Position"), $"({d.Left}, {d.Top}) – ({d.Right}, {d.Bottom})"),
                new InfoRow(L("Disp.WorkArea"), $"({d.WorkLeft}, {d.WorkTop}) – ({d.WorkRight}, {d.WorkBottom})"),
                new InfoRow(L("Disp.Device"), d.DeviceName),
            ]));
        }
        ReplaceSections(sections);
    }

    [RelayCommand]
    private void CopyReport() => Services.Ui.CopyToClipboard(SectionsToText(Title, Sections));

    public override void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.Dispose();
    }
}

// =====================================================================================
//  Memory
// =====================================================================================

public sealed partial class MemoryViewModel(AppServices services) : LivePageViewModel(services)
{
    private MemorySnapshot? _snapshot;

    public override string PageKey => "Memory";

    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private string _totalText = "—";
    [ObservableProperty] private string _usedText = "—";
    [ObservableProperty] private string _freeText = "—";
    [ObservableProperty] private string _commitText = "—";
    [ObservableProperty] private string _slotsText = "";
    [ObservableProperty] private bool _hasModules;

    public ObservableCollection<InfoSection> Modules { get; } = [];

    protected override async Task LoadAsync(CancellationToken ct) => _snapshot = await Services.Memory.GetSnapshotAsync(ct);

    protected override void OnTick()
    {
        var s = Services.Memory.GetStatus();
        var c = LanguageCatalog.CurrentCulture;
        UsagePercent = s.LoadPercent;
        TotalText = ByteFormatter.FormatBinary(s.TotalPhysicalBytes, 1, c);
        UsedText = $"{ByteFormatter.FormatBinary(s.UsedPhysicalBytes, 1, c)}  ({s.LoadPercent} %)";
        FreeText = ByteFormatter.FormatBinary(s.AvailablePhysicalBytes, 1, c);
        CommitText = $"{ByteFormatter.FormatBinary(s.CommitTotalBytes, 1, c)} / {ByteFormatter.FormatBinary(s.CommitLimitBytes, 1, c)}";
    }

    protected override void BuildSections()
    {
        if (_snapshot is null) return;
        var c = LanguageCatalog.CurrentCulture;
        var mask = Services.Settings.Current.Hardware.MaskSerialNumbers;
        SlotsText = (_snapshot.TotalSlots is { } t ? LF("Mem.Slots", _snapshot.Modules.Count, t) : "") +
                    (_snapshot.MaxCapacityBytes is { } max ? "   ·   " + LF("Mem.MaxCapacity", ByteFormatter.FormatBinary(max, 0, c)) : "");

        Modules.Clear();
        foreach (var m in _snapshot.Modules)
        {
            Modules.Add(new InfoSection($"{m.Slot}  {(string.IsNullOrEmpty(m.Bank) ? "" : "· " + m.Bank)}",
            [
                new InfoRow(L("Mem.Capacity"), ByteFormatter.FormatBinary(m.CapacityBytes, 0, c)),
                new InfoRow(L("Mem.Type"), m.MemoryType),
                new InfoRow(L("Mem.Speed"), m.SpeedMTs is { } sp ? $"{sp} MT/s  ({sp / 2} MHz)" : null),
                new InfoRow(L("Mem.ConfiguredSpeed"), m.ConfiguredSpeedMTs is { } cs ? $"{cs} MT/s" : null),
                new InfoRow(L("Mem.FormFactor"), m.FormFactor),
                new InfoRow(L("Mem.Manufacturer"), m.Manufacturer),
                new InfoRow(L("Mem.PartNumber"), m.PartNumber),
                new InfoRow(L("Mem.Serial"), mask ? Core.Hardware.SerialMasker.Mask(m.SerialNumber) : m.SerialNumber),
                new InfoRow(L("Mem.Voltage"), m.VoltageV is { } v ? v.ToString("0.00", c) + " V" : null),
                new InfoRow(L("Mem.DataWidth"), m.DataWidth is { } dw ? $"{dw} bit" : null),
            ]));
        }
        HasModules = Modules.Count > 0;

        var pageRows = new List<InfoRow>();
        if (_snapshot.PageFiles.Count == 0)
        {
            pageRows.Add(new InfoRow(L("Mem.PageFile.Path"), L("Mem.PageFile.None")));
        }
        foreach (var p in _snapshot.PageFiles)
        {
            pageRows.Add(new InfoRow(L("Mem.PageFile.Path"), p.Path));
            pageRows.Add(new InfoRow(L("Mem.PageFile.Allocated"), ByteFormatter.FormatBinary(p.AllocatedBytes, 1, c)));
            pageRows.Add(new InfoRow(L("Mem.PageFile.Current"), ByteFormatter.FormatBinary(p.CurrentUsageBytes, 1, c)));
            pageRows.Add(new InfoRow(L("Mem.PageFile.Peak"), ByteFormatter.FormatBinary(p.PeakUsageBytes, 1, c)));
        }
        if (_snapshot.PageFileAutomaticallyManaged) pageRows.Add(new InfoRow("", L("Mem.PageFile.Auto")));
        ReplaceSections([new InfoSection(L("Mem.PageFile"), pageRows)]);
    }

    [RelayCommand]
    private void CopyReport() => Services.Ui.CopyToClipboard(SectionsToText(Title, Modules.Concat(Sections)));
}
