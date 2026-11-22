// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Windows;
using ZeloraInfo.Core.Localization;
using WinForms = System.Windows.Forms;

namespace ZeloraInfo.App.Services;

/// <summary>
/// System tray icon (WinForms NotifyIcon) showing the Zelora Info icon. The context menu gives direct access
/// to every page of the app plus Open / Exit; texts follow the active language.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private WinForms.NotifyIcon? _icon;
    private WinForms.ContextMenuStrip? _menu;
    private IReadOnlyList<(string Key, string? GroupKey)> _pages = [];
    private bool _balloonShown;

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<string>? NavigateRequested;

    public bool IsVisible
    {
        get => _icon?.Visible == true;
        set
        {
            if (value) EnsureCreated();
            if (_icon is not null) _icon.Visible = value;
        }
    }

    /// <summary>Registers the navigable pages (nav key + group header key) shown in the context menu.</summary>
    public void SetPages(IReadOnlyList<(string Key, string? GroupKey)> pages)
    {
        _pages = pages;
        if (_menu is not null) BuildMenu();
    }

    public void ShowRunningHint()
    {
        if (_icon is null || _balloonShown) return;
        _balloonShown = true;
        _icon.ShowBalloonTip(3000, LanguageCatalog.Get("App.Title"), LanguageCatalog.Get("Tray.Running"), WinForms.ToolTipIcon.Info);
    }

    public void RefreshTexts()
    {
        if (_menu is not null) BuildMenu();
    }

    private void EnsureCreated()
    {
        if (_icon is not null) return;

        _menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };
        BuildMenu();

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Zelora Info",
            ContextMenuStrip = _menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ZeloraInfo.ico"));
            if (info?.Stream is { } stream)
            {
                using (stream)
                {
                    // Pick the frame matching the current tray icon size (16 px at 100 %, 20/24/32 px at higher DPI).
                    return new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException)
        {
        }
        return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
    }

    private void BuildMenu()
    {
        if (_menu is null) return;
        _menu.Items.Clear();

        var open = new WinForms.ToolStripMenuItem(LanguageCatalog.Get("Tray.Open"), null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty))
        {
            Font = new System.Drawing.Font(_menu.Font, System.Drawing.FontStyle.Bold),
        };
        _menu.Items.Add(open);

        string? lastGroup = null;
        foreach (var (key, group) in _pages)
        {
            if (group != lastGroup)
            {
                _menu.Items.Add(new WinForms.ToolStripSeparator());
                if (group is not null)
                    _menu.Items.Add(new WinForms.ToolStripLabel(LanguageCatalog.Get(group)) { ForeColor = System.Drawing.SystemColors.GrayText });
                lastGroup = group;
            }
            var pageKey = key;
            _menu.Items.Add(new WinForms.ToolStripMenuItem("    " + LanguageCatalog.Get("Nav." + key), null, (_, _) => NavigateRequested?.Invoke(this, pageKey)));
        }

        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(new WinForms.ToolStripMenuItem(LanguageCatalog.Get("Tray.Exit"), null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));
    }

    public void Dispose()
    {
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
        _menu?.Dispose();
        _menu = null;
    }
}
