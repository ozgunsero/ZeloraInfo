// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ZeloraInfo.Core.Localization;

namespace ZeloraInfo.App.ViewModels;

/// <summary>Base for every page: busy/error state, activation hooks and language refresh.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    protected static string L(string key) => LanguageCatalog.Get(key);
    protected static string LF(string key, params object?[] args) => LanguageCatalog.Format(key, args);

    /// <summary>Navigation key used for the page title/subtitle resources.</summary>
    public abstract string PageKey { get; }

    public string Title => L("Nav." + PageKey);
    public string Subtitle => L("Page." + PageKey + ".Subtitle");

    /// <summary>Called when the page becomes visible (start timers, first load).</summary>
    public virtual void OnActivated()
    {
    }

    /// <summary>Called when the user navigates away (stop timers).</summary>
    public virtual void OnDeactivated()
    {
    }

    /// <summary>Rebuilds every localized, code-generated string.</summary>
    public virtual void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }
}

/// <summary>Label/value pair shown in information cards.</summary>
public sealed class InfoRow(string label, string? value, string? note = null)
{
    public string Label { get; } = label;
    public string Value { get; } = string.IsNullOrWhiteSpace(value) ? "—" : value!;
    public string? Note { get; } = note;
    public bool HasNote => !string.IsNullOrEmpty(Note);
}

/// <summary>A titled card of <see cref="InfoRow"/>s.</summary>
public sealed class InfoSection(string title, IEnumerable<InfoRow> rows, string? icon = null)
{
    public string Title { get; } = title;
    public string? Icon { get; } = icon;
    public ObservableCollection<InfoRow> Rows { get; } = new(rows);
}
