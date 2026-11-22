// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using ZeloraInfo.Core.Settings;

namespace ZeloraInfo.App.Services;

/// <summary>
/// Applies one of the five Zelora palettes (Midnight, Graphite, Aurora, Daylight, Sand) or follows Windows.
/// Standard controls use the WPF Fluent theme (ThemeMode, .NET 9+) in the matching light/dark variant;
/// custom surfaces bind to the swapped palette dictionary. The native title bar follows via DWM.
/// </summary>
public sealed class ThemeManager
{
    private ResourceDictionary? _palette;
    private AppTheme _requested = AppTheme.Midnight;

    public ThemeManager()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _requested == AppTheme.System)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
        };
    }

    public AppTheme Effective { get; private set; } = AppTheme.Midnight;
    public bool IsDark => AppThemeInfo.IsDark(Effective);

    public event EventHandler? ThemeChanged;

    public void Apply(AppTheme theme)
    {
        _requested = theme;
        Effective = AppThemeInfo.Resolve(theme, SystemUsesDarkTheme());

#pragma warning disable WPF0001
        Application.Current.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        var dict = LoadPalette(Effective);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_palette is not null) merged.Remove(_palette);
        merged.Add(dict);
        _palette = dict;

        foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public static ResourceDictionary LoadPalette(AppTheme theme)
        => new() { Source = new Uri($"pack://application:,,,/Themes/Palette.{AppThemeInfo.Resolve(theme, SystemUsesDarkTheme())}.xaml") };

    /// <summary>Four representative colours for the theme picker preview (background, card, accent, text).</summary>
    public static Brush[] PreviewBrushes(AppTheme theme)
    {
        var d = LoadPalette(theme);
        string[] keys = ["Brush.PageBackground", "Brush.Card", "Brush.Accent", "Brush.TextPrimary"];
        return keys.Select(k => d[k] as Brush ?? Brushes.Gray).ToArray();
    }

    public void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var value = IsDark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (Windows 10 2004+ / 11)
        _ = DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return true;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
