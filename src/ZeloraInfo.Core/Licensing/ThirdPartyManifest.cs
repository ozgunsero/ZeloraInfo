// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeloraInfo.Core.Licensing;

/// <summary>
/// Model of <c>third-party-licenses.json</c> — the single source of truth for every bundled
/// third-party component. Update it whenever a PackageReference is added, removed or upgraded.
/// </summary>
public sealed class ThirdPartyManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string? Updated { get; set; }
    public List<ThirdPartyComponent> Components { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ThirdPartyManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<ThirdPartyManifest>(json, Options) ?? new ThirdPartyManifest();
        manifest.Components ??= [];
        manifest.Components = manifest.Components
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return manifest;
    }
}

public sealed class ThirdPartyComponent
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>SPDX identifier, e.g. "MIT", "Apache-2.0".</summary>
    public string License { get; set; } = "";
    /// <summary>Usage description keyed by language code ("tr", "en", "de").</summary>
    public Dictionary<string, string> Purpose { get; set; } = [];
    public string ProjectUrl { get; set; } = "";
    public string LicenseUrl { get; set; } = "";
    public string Copyright { get; set; } = "";
    /// <summary>"runtime" (shipped with the app) or "development" (build/test only).</summary>
    public string Scope { get; set; } = "runtime";
    public string LicenseText { get; set; } = "";

    [JsonIgnore]
    public string PurposeText => Purpose.TryGetValue(Localization.LanguageCatalog.CurrentLanguage, out var p) ? p
        : Purpose.TryGetValue("en", out var en) ? en : Purpose.Values.FirstOrDefault() ?? "";

    public bool Matches(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || License.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Version.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || PurposeText.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
