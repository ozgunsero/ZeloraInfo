// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Text.RegularExpressions;
using ZeloraInfo.Core.Localization;
using ZeloraInfo.Core.Settings;
using ZeloraInfo.Infrastructure.Settings;

namespace ZeloraInfo.Tests;

public class LanguageCatalogTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.Compiled);

    [Fact]
    public void SupportsSixLanguages()
    {
        Assert.Equal(["tr", "en", "de", "fr", "it", "es"], LanguageCatalog.SupportedLanguages);
        foreach (var lang in LanguageCatalog.SupportedLanguages)
            Assert.True(LanguageCatalog.LanguageNames.ContainsKey(lang));
    }

    [Fact]
    public void EveryKeyHasAllSixTranslations()
    {
        var missing = new List<string>();
        foreach (var (key, e) in LanguageCatalog.Entries)
        {
            if (string.IsNullOrWhiteSpace(e.Tr)) missing.Add($"{key} [tr]");
            if (string.IsNullOrWhiteSpace(e.En)) missing.Add($"{key} [en]");
            if (string.IsNullOrWhiteSpace(e.De)) missing.Add($"{key} [de]");
            if (!LanguageCatalog.RomanceEntries.TryGetValue(key, out var r))
            {
                missing.Add($"{key} [fr/it/es]");
                continue;
            }
            if (string.IsNullOrWhiteSpace(r.Fr)) missing.Add($"{key} [fr]");
            if (string.IsNullOrWhiteSpace(r.It)) missing.Add($"{key} [it]");
            if (string.IsNullOrWhiteSpace(r.Es)) missing.Add($"{key} [es]");
        }
        Assert.True(missing.Count == 0, "Missing translations: " + string.Join(", ", missing));
    }

    [Fact]
    public void RomanceTableHasNoOrphanKeys()
    {
        var orphans = LanguageCatalog.RomanceEntries.Keys.Where(k => !LanguageCatalog.Entries.ContainsKey(k)).ToList();
        Assert.True(orphans.Count == 0, "Orphan keys: " + string.Join(", ", orphans));
    }

    [Fact]
    public void FormatPlaceholdersMatchAcrossLanguages()
    {
        var problems = new List<string>();
        foreach (var key in LanguageCatalog.Entries.Keys)
        {
            var reference = Indexes(LanguageCatalog.Get(key, "en"));
            foreach (var lang in LanguageCatalog.SupportedLanguages)
            {
                if (!reference.SetEquals(Indexes(LanguageCatalog.Get(key, lang))))
                    problems.Add($"{key} [{lang}]");
            }
        }
        Assert.True(problems.Count == 0, "Placeholder mismatch: " + string.Join(", ", problems));

        static HashSet<int> Indexes(string s) => Placeholder.Matches(s).Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();
    }

    [Fact]
    public void FormattingWorksInEveryLanguage()
    {
        var previous = LanguageCatalog.CurrentLanguage;
        try
        {
            foreach (var lang in LanguageCatalog.SupportedLanguages)
            {
                LanguageCatalog.CurrentLanguage = lang;
                foreach (var (key, _) in LanguageCatalog.Entries)
                {
                    var text = LanguageCatalog.Get(key);
                    var count = Placeholder.Matches(text).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1;
                    if (count > 0) _ = LanguageCatalog.Format(key, Enumerable.Repeat((object?)1, count).ToArray());
                }
            }
        }
        finally
        {
            LanguageCatalog.CurrentLanguage = previous;
        }
    }

    [Fact]
    public void UnknownKeyFallsBackToKey() => Assert.Equal("Does.Not.Exist", LanguageCatalog.Get("Does.Not.Exist", "fr"));
}

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZeloraInfoTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var s = new JsonSettingsService(FilePath).Load();
        Assert.Equal(AppTheme.Midnight, s.General.Theme);
        Assert.Equal(CloseAction.Ask, s.General.CloseAction);
        Assert.Equal(4, s.Benchmark.Tests.Count);
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var svc = new JsonSettingsService(FilePath);
        svc.Load();
        svc.Current.General.Language = "de";
        svc.Current.General.Theme = AppTheme.Sand;
        svc.Current.General.CloseAction = CloseAction.MinimizeToTray;
        svc.Current.Smart.TemperatureUnit = TemperatureUnit.Fahrenheit;
        svc.Current.Hardware.MaskSerialNumbers = true;
        svc.Save();

        var loaded = new JsonSettingsService(FilePath).Load();
        Assert.Equal("de", loaded.General.Language);
        Assert.Equal(AppTheme.Sand, loaded.General.Theme);
        Assert.Equal(CloseAction.MinimizeToTray, loaded.General.CloseAction);
        Assert.Equal(TemperatureUnit.Fahrenheit, loaded.Smart.TemperatureUnit);
        Assert.True(loaded.Hardware.MaskSerialNumbers);
        Assert.Contains("\"Sand\"", File.ReadAllText(FilePath)); // enums persisted as readable strings
    }

    [Fact]
    public void CorruptFileIsBackedUpAndReplacedByDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json");
        var s = new JsonSettingsService(FilePath).Load();
        Assert.Equal(AppTheme.Midnight, s.General.Theme);
        Assert.NotEmpty(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
    }

    [Fact]
    public void NormalizeClampsOutOfRangeValues()
    {
        var s = AppSettings.CreateDefault();
        s.General.Language = "xx";
        s.General.UiScalePercent = 500;
        s.General.StartPage = "Nope";
        s.Smart.CautionTemperatureC = 90;
        s.Smart.CriticalTemperatureC = 20;
        s.Benchmark.Iterations = 99;
        s.Hardware.LiveRefreshMs = 1;
        s.Benchmark.Tests = [];
        s.Normalize();

        Assert.Contains(s.General.Language, LanguageCatalog.SupportedLanguages);
        Assert.Equal(130, s.General.UiScalePercent);
        Assert.Equal("DiskHealth", s.General.StartPage);
        Assert.True(s.Smart.CriticalTemperatureC > s.Smart.CautionTemperatureC);
        Assert.Equal(9, s.Benchmark.Iterations);
        Assert.Equal(250, s.Hardware.LiveRefreshMs);
        Assert.Equal(4, s.Benchmark.Tests.Count);
    }

    [Fact]
    public void ResetRestoresDefaultsAndRaisesEvent()
    {
        var svc = new JsonSettingsService(FilePath);
        svc.Load();
        svc.Current.General.Theme = AppTheme.Aurora;
        svc.Save();
        var raised = false;
        svc.SettingsChanged += (_, _) => raised = true;
        svc.ResetToDefaults();
        Assert.True(raised);
        Assert.Equal(AppTheme.Midnight, new JsonSettingsService(FilePath).Load().General.Theme);
    }

    [Fact]
    public void SystemThemeResolvesToMidnightOrDaylight()
    {
        Assert.Equal(AppTheme.Midnight, AppThemeInfo.Resolve(AppTheme.System, systemIsDark: true));
        Assert.Equal(AppTheme.Daylight, AppThemeInfo.Resolve(AppTheme.System, systemIsDark: false));
        Assert.Equal(AppTheme.Graphite, AppThemeInfo.Resolve(AppTheme.Graphite, systemIsDark: false));
        Assert.Equal(5, AppThemeInfo.Selectable.Count(t => t != AppTheme.System));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
