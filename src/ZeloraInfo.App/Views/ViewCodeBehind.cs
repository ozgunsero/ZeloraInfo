// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZeloraInfo.App.Views;

internal static class ScrollHelper
{
    /// <summary>Bubbles mouse-wheel events from inner grids/lists to the page ScrollViewer.</summary>
    public static void ForwardWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject d) return;
        e.Handled = true;
        var parent = VisualTreeHelper.GetParent(d);
        while (parent is not null and not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
        if (parent is UIElement target)
            target.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
    }
}

public partial class DiskHealthView : UserControl
{
    public DiskHealthView() => InitializeComponent();
    private void OnGridMouseWheel(object sender, MouseWheelEventArgs e) => ScrollHelper.ForwardWheel(sender, e);
}

public partial class BenchmarkView : UserControl
{
    public BenchmarkView() => InitializeComponent();
}

public partial class SystemView : UserControl
{
    public SystemView() => InitializeComponent();
}

public partial class DisplayView : UserControl
{
    public DisplayView() => InitializeComponent();
}

public partial class MemoryView : UserControl
{
    public MemoryView() => InitializeComponent();
}

public partial class MemoryOverviewView : UserControl
{
    public MemoryOverviewView() => InitializeComponent();
}

public partial class MemoryMapView : UserControl
{
    public MemoryMapView() => InitializeComponent();
}

public partial class CpuDetailsView : UserControl
{
    public CpuDetailsView() => InitializeComponent();
}

public partial class GpuView : UserControl
{
    public GpuView() => InitializeComponent();
}

public partial class RamDetailsView : UserControl
{
    public RamDetailsView() => InitializeComponent();
}

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}

public partial class AboutView : UserControl
{
    public AboutView() => InitializeComponent();
}

public partial class LicenseView : UserControl
{
    public LicenseView() => InitializeComponent();
}

public partial class ThirdPartyView : UserControl
{
    public ThirdPartyView() => InitializeComponent();
}
