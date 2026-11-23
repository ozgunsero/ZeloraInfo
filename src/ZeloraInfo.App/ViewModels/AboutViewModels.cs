// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeloraInfo.App.Services;
using ZeloraInfo.Core.Licensing;
using ZeloraInfo.Core.Localization;

namespace ZeloraInfo.App.ViewModels;

/// <summary>
/// About page with three tabs: overview, the full Apache 2.0 license and the third-party licenses.
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    public const int OverviewTab = 0;
    public const int LicenseTab = 1;
    public const int ThirdPartyTab = 2;

    private readonly AppServices _services;

    public AboutViewModel(AppServices services)
    {
        _services = services;
        License = new LicenseViewModel(services);
        ThirdParty = new ThirdPartyViewModel(services);
        try
        {
            Components = AppInfo.LoadThirdPartyManifest().Components;
        }
        catch (Exception ex)
        {
            Components = [];
            ErrorMessage = LF("Tp.ManifestError", ex.Message);
        }
        BuildRows();
    }

    public override string PageKey => "About";

    public IReadOnlyList<ThirdPartyComponent> Components { get; }
    public string Copyright => "Copyright 2026 Şero Teknoloji";

    public LicenseViewModel License { get; }
    public ThirdPartyViewModel ThirdParty { get; }

    [ObservableProperty] private IReadOnlyList<InfoRow> _rows = [];
    [ObservableProperty] private int _selectedTabIndex = OverviewTab;

    private void BuildRows()
    {
        Rows =
        [
            new InfoRow(L("About.Version"), AppInfo.InformationalVersion.Split('+')[0]),
            new InfoRow(L("About.Build"), AppInfo.BuildDate.ToString("yyyy-MM-dd HH:mm", LanguageCatalog.CurrentCulture) + "  ·  " + (AppInfo.InformationalVersion.Contains('+') ? AppInfo.InformationalVersion.Split('+')[1][..Math.Min(8, AppInfo.InformationalVersion.Split('+')[1].Length)] : "release")),
            new InfoRow(L("About.Runtime"), AppInfo.Runtime),
            new InfoRow(L("About.Developer"), "Şero Teknoloji"),
            new InfoRow(L("Col.License"), "Apache License 2.0"),
        ];
    }

    [RelayCommand] private void OpenLicense() => SelectedTabIndex = LicenseTab;
    [RelayCommand] private void OpenThirdParty() => SelectedTabIndex = ThirdPartyTab;
    [RelayCommand] private void OpenUrl(string url) => _services.Ui.OpenUrl(url);

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        BuildRows();
        OnPropertyChanged(nameof(Components));
        License.OnLanguageChanged();
        ThirdParty.OnLanguageChanged();
    }
}

public sealed partial class LicenseViewModel(AppServices services) : ViewModelBase
{
    public override string PageKey => "License";

    public string LicenseText { get; } = string.IsNullOrWhiteSpace(AppInfo.LicenseText) ? "Apache License, Version 2.0 — https://www.apache.org/licenses/LICENSE-2.0" : AppInfo.LicenseText;

    [RelayCommand] private void Copy() => services.Ui.CopyToClipboard(LicenseText);

    [RelayCommand] private void Export() => services.Ui.SaveText("ZeloraInfo-LICENSE.txt", "Text (*.txt)|*.txt", LicenseText);

    [RelayCommand] private void OpenOnline() => services.Ui.OpenUrl("https://www.apache.org/licenses/LICENSE-2.0");
}

public sealed partial class ThirdPartyViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly IReadOnlyList<ThirdPartyComponent> _all;

    public ThirdPartyViewModel(AppServices services)
    {
        _services = services;
        try
        {
            _all = AppInfo.LoadThirdPartyManifest().Components;
        }
        catch (Exception ex)
        {
            _all = [];
            ErrorMessage = LF("Tp.ManifestError", ex.Message);
        }
        ApplyFilter();
        SelectedComponent = Components.FirstOrDefault();
    }

    public override string PageKey => "ThirdParty";

    public ObservableCollection<ThirdPartyComponent> Components { get; } = [];

    [ObservableProperty] private string _filter = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedScope), nameof(SelectedPurpose))]
    private ThirdPartyComponent? _selectedComponent;
    [ObservableProperty] private string _countText = "";

    public bool HasSelection => SelectedComponent is not null;
    public string SelectedScope => SelectedComponent is null ? "" : L("Tp.Scope." + SelectedComponent.Scope);
    public string SelectedPurpose => SelectedComponent?.PurposeText ?? "";

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var selected = SelectedComponent;
        Components.Clear();
        foreach (var c in _all.Where(c => c.Matches(Filter))) Components.Add(c);
        SelectedComponent = selected is not null && Components.Contains(selected) ? selected : Components.FirstOrDefault();
        CountText = LF("Tp.Count", Components.Count);
    }

    [RelayCommand]
    private void CopyLicense()
    {
        if (SelectedComponent is null) return;
        var c = SelectedComponent;
        _services.Ui.CopyToClipboard($"{c.Name} {c.Version} — {c.License}\n{c.Copyright}\n{c.ProjectUrl}\n\n{c.LicenseText}");
    }

    [RelayCommand] private void OpenUrl(string? url) => _services.Ui.OpenUrl(url);

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        CountText = LF("Tp.Count", Components.Count);
        OnPropertyChanged(nameof(SelectedScope));
        OnPropertyChanged(nameof(SelectedPurpose));
        var s = SelectedComponent;
        SelectedComponent = null;
        SelectedComponent = s;
    }
}
