// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.ComponentModel;
using System.Windows;
using ZeloraInfo.App.Services;
using ZeloraInfo.App.ViewModels;
using ZeloraInfo.Core.Settings;

namespace ZeloraInfo.App;

public partial class MainWindow : Window
{
    private readonly AppServices _services;

    public MainWindow(MainViewModel viewModel, AppServices services)
    {
        _services = services;
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => services.Theme.ApplyTitleBar(this);
        services.Theme.ThemeChanged += (_, _) => services.Theme.ApplyTitleBar(this);
        StateChanged += OnStateChanged;
    }

    private static App CurrentApp => (App)Application.Current;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _services.Settings.Current.General.MinimizeToTray)
            CurrentApp.HideToTray();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (App.IsExiting) return;

        e.Cancel = true;
        var general = _services.Settings.Current.General;
        var action = general.CloseAction;
        if (action == CloseAction.Ask)
        {
            var dialog = new CloseDialog(_services) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Choice is null) return; // cancelled
            action = dialog.Choice.Value;
            if (dialog.Remember)
            {
                general.CloseAction = action;
                try { _services.Settings.Save(); }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { App.LogError(ex); }
            }
        }

        if (action == CloseAction.MinimizeToTray)
            CurrentApp.HideToTray();
        else
            Dispatcher.BeginInvoke(CurrentApp.ExitApplication);
    }
}
