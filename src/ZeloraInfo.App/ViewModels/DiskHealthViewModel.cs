// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeloraInfo.App.Services;
using ZeloraInfo.Core.Disks;
using ZeloraInfo.Core.Formatting;
using ZeloraInfo.Core.Hardware;
using ZeloraInfo.Core.Localization;
using ZeloraInfo.Core.Settings;

namespace ZeloraInfo.App.ViewModels;

public sealed partial class DiskHealthViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer = new();
    private CancellationTokenSource? _cts;
    private DateTime? _lastUpdated;

    public DiskHealthViewModel(AppServices services)
    {
        _services = services;
        _timer.Tick += async (_, _) => await RefreshAsync();
        services.Settings.SettingsChanged += (_, _) => ApplyTimer();
    }

    public override string PageKey => "DiskHealth";

    public ObservableCollection<DiskItemViewModel> Disks { get; } = [];

    [ObservableProperty] private DiskItemViewModel? _selectedDisk;
    [ObservableProperty] private string? _hotDiskWarning;

    public bool IsRestricted => !_services.Privileges.IsAdministrator;
    public bool HasNoDisks => !IsBusy && Disks.Count == 0 && string.IsNullOrEmpty(ErrorMessage);
    public string LastUpdatedText => _lastUpdated is null ? "" : LF("Common.LastUpdated", _lastUpdated.Value.ToString("T", LanguageCatalog.CurrentCulture));
    public string AutoRefreshText => _services.Settings.Current.Smart.RefreshIntervalSeconds > 0
        ? LF("Disk.AutoRefresh", _services.Settings.Current.Smart.RefreshIntervalSeconds) : L("Disk.AutoRefreshOff");

    public override void OnActivated()
    {
        if (Disks.Count == 0) _ = RefreshAsync();
        ApplyTimer();
    }

    public override void OnDeactivated() => _timer.Stop();

    private void ApplyTimer()
    {
        var seconds = _services.Settings.Current.Smart.RefreshIntervalSeconds;
        _timer.Stop();
        if (seconds > 0)
        {
            _timer.Interval = TimeSpan.FromSeconds(seconds);
            _timer.Start();
        }
        OnPropertyChanged(nameof(AutoRefreshText));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        try
        {
            var settings = _services.Settings.Current;
            var disks = await _services.Disks.GetDisksAsync(settings.Smart, _cts.Token);
            var previousIndex = SelectedDisk?.Index;
            Disks.Clear();
            foreach (var d in disks) Disks.Add(new DiskItemViewModel(d, settings));
            SelectedDisk = Disks.FirstOrDefault(d => d.Index == previousIndex) ?? Disks.FirstOrDefault();
            _lastUpdated = DateTime.Now;
            UpdateHotWarning();
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
            OnPropertyChanged(nameof(HasNoDisks));
            OnPropertyChanged(nameof(LastUpdatedText));
        }
    }

    [RelayCommand]
    private void RestartAsAdmin() => App.RestartElevated();

    [RelayCommand]
    private void CopyReport()
    {
        if (SelectedDisk is null) return;
        _services.Ui.CopyToClipboard(SelectedDisk.BuildReport());
    }

    private void UpdateHotWarning()
    {
        var settings = _services.Settings.Current.Smart;
        var hot = Disks.FirstOrDefault(d => d.TemperatureLevel == TemperatureLevel.Critical);
        HotDiskWarning = hot is null ? null : LF("Temp.CriticalBanner", hot.Title, TemperatureFormatter.Format(hot.Disk.TemperatureC, settings.TemperatureUnit));
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        var settings = _services.Settings.Current;
        foreach (var d in Disks) d.Rebuild(settings);
        UpdateHotWarning();
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(AutoRefreshText));
    }

    public void Dispose()
    {
        _timer.Stop();
        _cts?.Cancel();
    }
}

public sealed partial class DiskItemViewModel : ObservableObject
{
    public DiskItemViewModel(DiskInfo disk, AppSettings settings)
    {
        Disk = disk;
        Rebuild(settings);
    }

    public DiskInfo Disk { get; }
    public int Index => Disk.Index;
    public string Title => Disk.DisplayName;
    public HealthStatus Health => Disk.Health;
    public TemperatureLevel TemperatureLevel { get; private set; }

    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _healthText = "";
    [ObservableProperty] private string _lifeText = "";
    [ObservableProperty] private string _temperatureText = "";
    [ObservableProperty] private string _temperatureLevelText = "";
    [ObservableProperty] private string _assessment = "";
    [ObservableProperty] private string? _smartUnavailableText;
    [ObservableProperty] private IReadOnlyList<InfoRow> _details = [];
    [ObservableProperty] private IReadOnlyList<SmartAttributeRow> _attributes = [];

    public bool HasAttributes => Attributes.Count > 0;

    public void Rebuild(AppSettings settings)
    {
        var c = LanguageCatalog.CurrentCulture;
        string L(string k) => LanguageCatalog.Get(k);

        Subtitle = string.Join("  ·  ", new[]
        {
            ByteFormatter.FormatDecimal(Disk.SizeBytes, 1, c),
            Disk.DriveLetters.Count > 0 ? string.Join(" ", Disk.DriveLetters) : null,
            BusName(Disk.Bus),
        }.Where(s => !string.IsNullOrEmpty(s)));

        HealthText = L("Health." + Disk.Health);
        LifeText = Disk.LifeRemainingPercent is { } life ? $"{life} %" : "";
        TemperatureLevel = SmartHealthEvaluator.GetTemperatureLevel(Disk.TemperatureC, settings.Smart);
        TemperatureText = TemperatureFormatter.Format(Disk.TemperatureC, settings.Smart.TemperatureUnit);
        TemperatureLevelText = TemperatureLevel == TemperatureLevel.Unknown ? "" : L("Temp." + TemperatureLevel);
        Assessment = Disk.HealthReasons.Count == 0
            ? (Disk.Health == HealthStatus.Good ? L("Health.Reason.None") : "")
            : string.Join(Environment.NewLine, Disk.HealthReasons.Select(r => "• " + L(r)));
        SmartUnavailableText = Disk.SmartUnavailableReason is null ? null : L(Disk.SmartUnavailableReason);

        var serial = settings.Hardware.MaskSerialNumbers ? SerialMasker.Mask(Disk.SerialNumber) : Disk.SerialNumber;
        Details =
        [
            new InfoRow(L("Disk.Model"), Disk.Model),
            new InfoRow(L("Disk.Firmware"), Disk.Firmware),
            new InfoRow(L("Disk.Serial"), serial),
            new InfoRow(L("Disk.Interface"), BusName(Disk.Bus)),
            new InfoRow(L("Disk.TransferMode"), Disk.TransferMode),
            new InfoRow(L("Disk.Capacity"), $"{ByteFormatter.FormatDecimal(Disk.SizeBytes, 1, c)}  ({Disk.SizeBytes.ToString("N0", c)} B)"),
            new InfoRow(L("Disk.DriveLetters"), string.Join("  ", Disk.DriveLetters)),
            new InfoRow(L("Disk.Media"), L("Media." + Disk.Media)),
            new InfoRow(L("Disk.Rotation"), Disk.RotationRateRpm switch
            {
                0 => L("Disk.SsdNoRotation"),
                { } rpm => string.Format(c, L("Disk.Rpm"), rpm),
                _ => null
            }),
            new InfoRow(L("Disk.PowerOnHours"), Disk.PowerOnHours is { } h ? string.Format(c, L("Common.Hours"), h) : null),
            new InfoRow(L("Disk.PowerOnCount"), Disk.PowerOnCount is { } n ? string.Format(c, L("Common.Times"), n) : null),
            new InfoRow(L("Disk.HostReads"), Disk.HostReadsBytes is { } r ? ByteFormatter.FormatDecimal(r, 2, c) : null),
            new InfoRow(L("Disk.HostWrites"), Disk.HostWritesBytes is { } w ? ByteFormatter.FormatDecimal(w, 2, c) : null),
            new InfoRow(L("Disk.SmartSource"), L("Smart.Source." + Disk.SmartSource)),
            new InfoRow(L("Disk.SystemDisk"), Disk.IsSystemDisk ? L("Common.Yes") : L("Common.No")),
        ];

        Attributes = Disk.Attributes.Select(a => new SmartAttributeRow(a)).ToList();
        OnPropertyChanged(nameof(HasAttributes));
        OnPropertyChanged(nameof(TemperatureLevel));
        OnPropertyChanged(nameof(Health));
    }

    public static string BusName(DiskBusKind bus) => bus switch
    {
        DiskBusKind.Sata => "SATA",
        DiskBusKind.Nvme => "NVM Express",
        DiskBusKind.Usb => "USB",
        DiskBusKind.Sas => "SAS",
        DiskBusKind.Scsi => "SCSI",
        DiskBusKind.Raid => "RAID",
        DiskBusKind.Virtual => "Virtual",
        DiskBusKind.SdCard => "SD / MMC",
        DiskBusKind.Other => "Other",
        _ => LanguageCatalog.Get("Common.Unknown")
    };

    public string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Zelora Info {AppInfo.Version} — {LanguageCatalog.Get("Nav.DiskHealth")}");
        sb.AppendLine(new string('-', 72));
        sb.AppendLine($"{Title}  [{HealthText}{(LifeText.Length > 0 ? " " + LifeText : "")}]  {TemperatureText}");
        foreach (var r in Details) sb.AppendLine($"{r.Label,-26}: {r.Value}");
        if (!string.IsNullOrEmpty(Assessment)) sb.AppendLine().AppendLine(Assessment);
        if (Attributes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ID  Attribute                                   Cur  Wor  Thr  Raw");
            foreach (var a in Attributes)
                sb.AppendLine($"{a.IdHex,-3} {a.Name,-42} {a.Current,4} {a.Worst,4} {a.Threshold,4}  {a.Raw}");
        }
        return sb.ToString();
    }
}

public sealed class SmartAttributeRow(SmartAttribute a)
{
    public string IdHex { get; } = a.IdHex;
    public string Name { get; } = a.Name;
    public string Current { get; } = a.Current?.ToString() ?? "";
    public string Worst { get; } = a.Worst?.ToString() ?? "";
    public string Threshold { get; } = a.Threshold?.ToString() ?? "";
    public string Raw { get; } = a.RawDisplay;
    public AttributeStatus Status { get; } = a.Status;
    public string StatusText { get; } = LanguageCatalog.Get("Attr." + a.Status);
}
