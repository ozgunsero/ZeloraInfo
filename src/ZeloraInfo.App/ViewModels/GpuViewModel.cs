// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeloraInfo.App.Services;
using ZeloraInfo.Core.Benchmark;
using ZeloraInfo.Core.Disks;
using ZeloraInfo.Core.Formatting;
using ZeloraInfo.Core.Hardware;
using ZeloraInfo.Core.Localization;
using ZeloraInfo.Core.Settings;

namespace ZeloraInfo.App.ViewModels;

public sealed partial class GpuEngineRow : ObservableObject
{
    public GpuEngineRow(string name) => Name = name;
    public string Name { get; }
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _percentText = "";
}

public sealed partial class GpuItemViewModel(GpuAdapterInfo info) : ObservableObject
{
    public GpuAdapterInfo Info { get; } = info;
    public string Title => Info.Name;

    [ObservableProperty] private string _subtitle = "";

    public void Rebuild()
    {
        var c = LanguageCatalog.CurrentCulture;
        var type = Info.IsSoftware ? LanguageCatalog.Get("Gpu.Type.Software")
            : Info.IsIntegrated == true ? LanguageCatalog.Get("Gpu.Type.Integrated")
            : Info.IsIntegrated == false ? LanguageCatalog.Get("Gpu.Type.Discrete") : null;
        Subtitle = string.Join("  ·  ", new[] { Info.Vendor, Info.DedicatedVideoMemory > 0 ? ByteFormatter.FormatBinary(Info.DedicatedVideoMemory, 0, c) : null, type }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}

/// <summary>GPU-Z style page: identity, memory, driver, DirectX 12 capabilities, PCIe link and live sensors.</summary>
public sealed partial class GpuViewModel(AppServices services) : LivePageViewModel(services)
{
    private IReadOnlyList<GpuAdapterInfo> _adapters = [];
    private IReadOnlyList<GpuLiveSample> _live = [];
    private bool _sampling;

    public override string PageKey => "Gpu";

    public ObservableCollection<GpuItemViewModel> Adapters { get; } = [];
    public ObservableCollection<GpuEngineRow> Engines { get; } = [];

    [ObservableProperty] private GpuItemViewModel? _selectedAdapter;
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _subline = "";
    [ObservableProperty] private bool _hasNoAdapters;
    [ObservableProperty] private bool _hasMultipleAdapters;

    [ObservableProperty] private double _utilization;
    [ObservableProperty] private string _utilizationText = "—";
    [ObservableProperty] private string _temperatureText = "—";
    [ObservableProperty] private TemperatureLevel _temperatureLevel = TemperatureLevel.Unknown;
    [ObservableProperty] private string _temperatureLimitText = "";
    [ObservableProperty] private string _fanText = "—";
    [ObservableProperty] private string _fanDetail = "";
    [ObservableProperty] private double _power;
    [ObservableProperty] private string _powerText = "—";
    [ObservableProperty] private string _coreClockText = "—";
    [ObservableProperty] private string _coreClockDetail = "";
    [ObservableProperty] private string _memoryClockText = "—";
    [ObservableProperty] private string _memoryClockDetail = "";
    [ObservableProperty] private double _vramPercent;
    [ObservableProperty] private string _vramText = "—";
    [ObservableProperty] private string _sharedText = "—";

    partial void OnSelectedAdapterChanged(GpuItemViewModel? value)
    {
        BuildSections();
        ApplyLive();
    }

    protected override async Task LoadAsync(CancellationToken ct)
    {
        _adapters = await Services.Gpu.GetAdaptersAsync(ct);
        _live = await Task.Run(Services.Gpu.SampleLive, ct);
        var previous = SelectedAdapter?.Info.Luid;
        Adapters.Clear();
        foreach (var a in _adapters)
        {
            var item = new GpuItemViewModel(a);
            item.Rebuild();
            Adapters.Add(item);
        }
        HasNoAdapters = Adapters.Count == 0;
        HasMultipleAdapters = Adapters.Count > 1;
        SelectedAdapter = Adapters.FirstOrDefault(a => a.Info.Luid == previous) ?? Adapters.FirstOrDefault();
    }

    protected override void OnTick()
    {
        if (!Loaded || _sampling) return;
        _sampling = true;
        _ = Task.Run(Services.Gpu.SampleLive).ContinueWith(t =>
        {
            _sampling = false;
            if (t.Status != TaskStatus.RanToCompletion) return;
            _live = t.Result;
            ApplyLive();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ApplyLive()
    {
        var info = SelectedAdapter?.Info;
        var s = info is null ? null : _live.FirstOrDefault(x => x.Luid == info.Luid);
        var c = LanguageCatalog.CurrentCulture;
        var na = L("Common.NA");
        string Mhz(double? v) => v is null ? na : v.Value.ToString("N0", c) + " MHz";

        Utilization = s?.UtilizationPercent ?? 0;
        UtilizationText = s is null ? "—" : s.UtilizationPercent.ToString("0", c) + " %";

        var unit = Services.Settings.Current.Smart.TemperatureUnit;
        TemperatureText = s?.TemperatureC is { } t
            ? (unit == TemperatureUnit.Fahrenheit ? TemperatureFormatter.CelsiusToFahrenheit(t).ToString("0", c) + " °F" : t.ToString("0.0", c) + " °C")
            : na;
        var warn = info?.TemperatureWarningC ?? 83;
        var max = info?.TemperatureMaxC ?? 93;
        TemperatureLevel = s?.TemperatureC is not { } tc ? TemperatureLevel.Unknown : tc >= max ? TemperatureLevel.Critical : tc >= warn ? TemperatureLevel.Caution : TemperatureLevel.Normal;
        TemperatureLimitText = info?.TemperatureWarningC is { } w && info.TemperatureMaxC is { } m ? LF("Gpu.TempLimitShort", w.ToString("0", c), m.ToString("0", c)) : "";

        FanText = s?.FanRpm is { } rpm ? rpm.ToString("N0", c) + " RPM" : na;
        FanDetail = s?.FanRpm is { } r && info?.MaxFanRpm is { } mf && mf > 0 ? $"{Math.Round(r * 100d / mf):0} %  ·  {LF("Gpu.MaxOf", mf.ToString("N0", c) + " RPM")}" : "";
        Power = Math.Min(100, s?.PowerPercent ?? 0);
        PowerText = s?.PowerPercent is { } p ? p.ToString("0.0", c) + " %" : na;
        CoreClockText = Mhz(s?.CoreClockMHz);
        CoreClockDetail = s?.MaxCoreClockMHz is { } mc ? LF("Gpu.MaxOf", Mhz(mc)) : "";
        MemoryClockText = Mhz(s?.MemoryClockMHz);
        MemoryClockDetail = s?.MaxMemoryClockMHz is { } mm ? LF("Gpu.MaxOf", Mhz(mm)) : "";

        var total = info?.DedicatedVideoMemory ?? 0;
        VramPercent = s?.DedicatedUsedBytes is { } used && total > 0 ? Math.Min(100, used * 100d / total) : 0;
        VramText = s?.DedicatedUsedBytes is { } u
            ? $"{ByteFormatter.FormatBinary(u, 1, c)} / {ByteFormatter.FormatBinary(total, 1, c)}" + (total > 0 ? $"  ({VramPercent:0} %)" : "")
            : na;
        SharedText = s?.SharedUsedBytes is { } sh ? $"{ByteFormatter.FormatBinary(sh, 1, c)} / {ByteFormatter.FormatBinary(info?.SharedSystemMemory ?? 0, 1, c)}" : na;

        var engines = s?.Engines ?? [];
        if (Engines.Count != engines.Count || Engines.Select(e => e.Name).Zip(engines.Select(e => e.Name)).Any(z => z.First != z.Second))
        {
            Engines.Clear();
            foreach (var e in engines) Engines.Add(new GpuEngineRow(e.Name));
        }
        for (var i = 0; i < engines.Count; i++)
        {
            Engines[i].Percent = engines[i].Percent;
            Engines[i].PercentText = engines[i].Percent.ToString("0", c) + " %";
        }
    }

    protected override void BuildSections()
    {
        foreach (var a in Adapters) a.Rebuild();
        var info = SelectedAdapter?.Info;
        if (info is null)
        {
            ReplaceSections([]);
            Headline = Subline = "";
            return;
        }
        var c = LanguageCatalog.CurrentCulture;
        var na = L("Common.NA");
        string Or(string? v) => string.IsNullOrWhiteSpace(v) ? na : v!;
        string Bytes(ulong v) => v == 0 ? "0" : ByteFormatter.FormatBinary(v, 1, c);
        string YesNo(bool? v) => v is null ? na : L(v.Value ? "Common.Yes" : "Common.No");

        Headline = info.Name;
        Subline = string.Join("  ·  ", new[] { info.Vendor, info.Architecture, info.DedicatedVideoMemory > 0 ? ByteFormatter.FormatBinary(info.DedicatedVideoMemory, 0, c) + " VRAM" : null, info.BusInterface }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var type = info.IsSoftware ? L("Gpu.Type.Software") : info.IsIntegrated == true ? L("Gpu.Type.Integrated") : info.IsIntegrated == false ? L("Gpu.Type.Discrete") : na;
        var driver = info.DriverVendorVersion is { Length: > 0 } vv ? $"{info.DriverVersion}  ({info.Vendor} {vv})" : info.DriverVersion;
        var hags = info.HardwareSchedulingSupported switch
        {
            true => L(info.HardwareSchedulingEnabled == true ? "Common.Enabled" : "Common.Disabled"),
            false => L("Common.NotSupported"),
            _ => na
        };
        var link = string.IsNullOrEmpty(info.CurrentLink) ? na : info.CurrentLink;

        ReplaceSections(
        [
            new InfoSection(L("Gpu.Identity"),
            [
                new InfoRow(L("Gpu.Name"), info.Name),
                new InfoRow(L("Gpu.Vendor"), info.Vendor),
                new InfoRow(L("Gpu.Type"), type),
                new InfoRow(L("Gpu.Architecture"), Or(info.Architecture)),
                new InfoRow(L("Gpu.ChipType"), Or(info.ChipType)),
                new InfoRow(L("Gpu.Bios"), Or(info.BiosVersion)),
                new InfoRow(L("Gpu.PciId"), Or(info.PciIdText)),
            ]),
            new InfoSection(L("Gpu.Memory"),
            [
                new InfoRow(L("Gpu.DedicatedVideo"), Bytes(info.DedicatedVideoMemory)),
                new InfoRow(L("Gpu.DedicatedSystem"), Bytes(info.DedicatedSystemMemory)),
                new InfoRow(L("Gpu.SharedSystem"), Bytes(info.SharedSystemMemory)),
                new InfoRow(L("Gpu.Bandwidth"), info.MaxMemoryBandwidth is { } bw ? (bw / 1e9).ToString("N1", c) + " GB/s" : na),
            ]),
            new InfoSection(L("Gpu.Driver"),
            [
                new InfoRow(L("Gpu.DriverVersion"), Or(driver)),
                new InfoRow(L("Gpu.DriverDate"), info.DriverDate?.ToString("d", c) ?? na),
                new InfoRow(L("Gpu.DriverProvider"), Or(info.DriverProvider)),
                new InfoRow(L("Gpu.Wddm"), Or(info.WddmVersion)),
                new InfoRow(L("Gpu.Hags"), hags),
            ]),
            new InfoSection(L("Gpu.DirectX"),
            [
                new InfoRow(L("Gpu.FeatureLevel"), Or(info.MaxFeatureLevel)),
                new InfoRow(L("Gpu.ShaderModel"), Or(info.ShaderModel)),
                new InfoRow(L("Gpu.Raytracing"), Or(info.RaytracingTier)),
                new InfoRow(L("Gpu.MeshShader"), Or(info.MeshShaderTier)),
                new InfoRow(L("Gpu.Vrs"), Or(info.VariableShadingRateTier)),
                new InfoRow(L("Gpu.Binding"), Or(info.ResourceBindingTier)),
                new InfoRow(L("Gpu.Ultimate"), YesNo(info.DirectX12Ultimate)),
            ]),
            new InfoSection(L("Gpu.Bus"),
            [
                new InfoRow(L("Gpu.BusInterface"), Or(info.BusInterface)),
                new InfoRow(L("Gpu.CurrentLink"), link, string.IsNullOrEmpty(info.CurrentLink) ? null : L("Gpu.LinkNote")),
                new InfoRow(L("Gpu.Nodes"), info.NodeCount?.ToString(c) ?? na),
                new InfoRow(L("Gpu.Luid"), "0x" + info.Luid.ToString("X16")),
            ]),
            new InfoSection(L("Gpu.Limits"),
            [
                new InfoRow(L("Gpu.TempLimit"), info.TemperatureWarningC is { } w && info.TemperatureMaxC is { } m ? $"{w.ToString("0", c)} °C / {m.ToString("0", c)} °C" : na),
                new InfoRow(L("Gpu.MaxFan"), info.MaxFanRpm is { } f ? f.ToString("N0", c) + " RPM" : na),
                new InfoRow("", L("Gpu.SensorNote")),
            ]),
        ]);
    }

    private string BuildText()
    {
        var live = new InfoSection(L("Gpu.Live"),
        [
            new InfoRow(L("Gpu.Utilization"), UtilizationText),
            new InfoRow(L("Gpu.Temperature"), TemperatureText),
            new InfoRow(L("Gpu.Fan"), FanText),
            new InfoRow(L("Gpu.Power"), PowerText),
            new InfoRow(L("Gpu.CoreClock"), CoreClockText),
            new InfoRow(L("Gpu.MemoryClock"), MemoryClockText),
            new InfoRow(L("Gpu.VramUsage"), VramText),
            new InfoRow(L("Gpu.SharedUsage"), SharedText),
        ]);
        return SectionsToText($"{Title} — {Headline}", Sections.Append(live));
    }

    [RelayCommand]
    private void CopyReport() => Services.Ui.CopyToClipboard(BuildText());

    [RelayCommand]
    private void Export(string format)
    {
        if (SelectedAdapter is null) return;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (format == "json")
        {
            var json = JsonSerializer.Serialize(new { application = "Zelora Info", version = AppInfo.Version, timestamp = DateTimeOffset.Now, adapters = _adapters, live = _live },
                BenchmarkReportFormatter.JsonOptions);
            Services.Ui.SaveText($"ZeloraInfo-GPU-{stamp}.json", "JSON (*.json)|*.json", json);
        }
        else
        {
            Services.Ui.SaveText($"ZeloraInfo-GPU-{stamp}.txt", "Text (*.txt)|*.txt", BuildText());
        }
    }
}
