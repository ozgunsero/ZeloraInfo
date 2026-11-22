// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using ZeloraInfo.Core.Localization;

namespace ZeloraInfo.App.Services;

/// <summary>
/// Projects <see cref="LanguageCatalog"/> into a merged ResourceDictionary. Every view uses
/// <c>{DynamicResource Key}</c>, so swapping the dictionary updates the whole UI instantly.
/// </summary>
public sealed class LocalizationManager
{
    private ResourceDictionary? _current;

    public event EventHandler? LanguageChanged;

    public string Language => LanguageCatalog.CurrentLanguage;

    public void Apply(string language)
    {
        if (!LanguageCatalog.SupportedLanguages.Contains(language)) language = "en";
        LanguageCatalog.CurrentLanguage = language;

        var dictionary = new ResourceDictionary();
        foreach (var key in LanguageCatalog.Entries.Keys)
            dictionary[key] = LanguageCatalog.Get(key, language);

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dictionary);
        _current = dictionary;

        var culture = LanguageCatalog.CurrentCulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        foreach (Window w in Application.Current.Windows)
            w.Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
