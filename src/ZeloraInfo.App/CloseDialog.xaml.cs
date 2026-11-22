// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Windows;
using ZeloraInfo.App.Services;
using ZeloraInfo.Core.Settings;

namespace ZeloraInfo.App;

/// <summary>Asked when the window is closed: minimize to the system tray or exit (optionally remembered).</summary>
public partial class CloseDialog : Window
{
    public CloseDialog(AppServices services)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => services.Theme.ApplyTitleBar(this);
    }

    public CloseAction? Choice { get; private set; }
    public bool Remember => RememberBox.IsChecked == true;

    private void OnTray(object sender, RoutedEventArgs e)
    {
        Choice = CloseAction.MinimizeToTray;
        DialogResult = true;
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        Choice = CloseAction.Exit;
        DialogResult = true;
    }
}
