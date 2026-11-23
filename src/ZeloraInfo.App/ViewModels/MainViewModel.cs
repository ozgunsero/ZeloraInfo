// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeloraInfo.App.Services;
using ZeloraInfo.Core.Localization;

namespace ZeloraInfo.App.ViewModels;

public sealed partial class NavItem : ObservableObject
{
    private readonly Func<ViewModelBase> _factory;
    private ViewModelBase? _instance;

    public NavItem(string key, string glyph, Func<ViewModelBase> factory)
    {
        Key = key;
        Glyph = glyph;
        _factory = factory;
    }

    public string Key { get; }
    public string Glyph { get; }
    public string Title => LanguageCatalog.Get("Nav." + Key);
    public ViewModelBase Page => _instance ??= _factory();
    public bool IsCreated => _instance is not null;

    [ObservableProperty] private bool _isSelected;

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

public sealed class NavGroup(string headerKey, IEnumerable<NavItem> items) : ObservableObject
{
    public string HeaderKey { get; } = headerKey;
    public string Header => LanguageCatalog.Get(HeaderKey);
    public ObservableCollection<NavItem> Items { get; } = new(items);
    public void Refresh() => OnPropertyChanged(nameof(Header));
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.6) };

    public MainViewModel(AppServices services)
    {
        _services = services;
        NavGroups =
        [
            new NavGroup("Nav.Group.Storage",
            [
                new NavItem("DiskHealth", "", () => new DiskHealthViewModel(services)),
                new NavItem("Benchmark", "", () => new BenchmarkViewModel(services)),
            ]),
            new NavGroup("Nav.Group.Hardware",
            [
                new NavItem("System", "", () => new SystemViewModel(services)),
                new NavItem("CpuDetails", "", () => new CpuDetailsViewModel(services)),
                new NavItem("Gpu", "", () => new GpuViewModel(services)),
                new NavItem("Memory", "", () => new MemoryViewModel(services)),
                new NavItem("RamDetails", "", () => new RamDetailsViewModel(services)),
                new NavItem("Display", "", () => new DisplayViewModel(services)),
            ]),
            new NavGroup("Nav.Group.App",
            [
                new NavItem("Settings", "", () => new SettingsViewModel(services)),
                new NavItem("About", "", () => new AboutViewModel(services)),
            ]),
        ];

        services.Localization.LanguageChanged += (_, _) => OnLanguageChanged();
        services.Settings.SettingsChanged += (_, _) => OnPropertyChanged(nameof(UiScale));
        services.Ui.ToastRequested += (_, message) => ShowToast(message);
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };
    }

    public ObservableCollection<NavGroup> NavGroups { get; }

    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private NavItem? _selectedItem;
    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private bool _isToastVisible;

    public double UiScale => _services.Settings.Current.General.UiScalePercent / 100d;

    public bool IsAdministrator => _services.Privileges.IsAdministrator;
    public string PrivilegeText => LanguageCatalog.Get(IsAdministrator ? "Priv.Admin" : "Priv.Restricted");
    public string VersionText => "v" + AppInfo.Version;

    public IEnumerable<NavItem> AllItems => NavGroups.SelectMany(g => g.Items);

    [RelayCommand]
    public void Navigate(string key)
    {
        var item = AllItems.FirstOrDefault(i => i.Key == key);
        if (item is null || ReferenceEquals(item, SelectedItem)) return;

        CurrentPage?.OnDeactivated();
        foreach (var i in AllItems) i.IsSelected = ReferenceEquals(i, item);
        SelectedItem = item;
        CurrentPage = item.Page;
        CurrentPage.OnActivated();
    }

    [RelayCommand]
    private void RestartAsAdmin() => App.RestartElevated();

    public void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnLanguageChanged()
    {
        foreach (var g in NavGroups)
        {
            g.Refresh();
            foreach (var i in g.Items)
            {
                i.RefreshTitle();
                if (i.IsCreated) i.Page.OnLanguageChanged();
            }
        }
        OnPropertyChanged(nameof(PrivilegeText));
    }

    public void Shutdown()
    {
        CurrentPage?.OnDeactivated();
        foreach (var i in AllItems.Where(i => i.IsCreated))
            (i.Page as IDisposable)?.Dispose();
    }
}
