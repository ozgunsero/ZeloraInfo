// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.Settings;

namespace ZeloraInfo.Infrastructure.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> as human-readable JSON. Writes are atomic (temp file + replace)
/// and a corrupt file is backed up and replaced by defaults instead of crashing the app.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();

    public JsonSettingsService(string? filePath = null)
    {
        SettingsFilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZeloraInfo", "settings.json");
        Current = AppSettings.CreateDefault().Normalize();
    }

    public AppSettings Current { get; private set; }
    public string SettingsFilePath { get; }
    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    Current = (JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.CreateDefault()).Normalize();
                }
                else
                {
                    Current = AppSettings.CreateDefault().Normalize();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                TryBackupCorruptFile();
                Current = AppSettings.CreateDefault().Normalize();
            }
            return Current;
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            Current.Normalize();
            var dir = Path.GetDirectoryName(SettingsFilePath)!;
            Directory.CreateDirectory(dir);
            var tmp = SettingsFilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
            File.Move(tmp, SettingsFilePath, overwrite: true);
        }
        SettingsChanged?.Invoke(this, Current);
    }

    public AppSettings ResetToDefaults()
    {
        lock (_gate)
        {
            Current = AppSettings.CreateDefault().Normalize();
        }
        Save();
        return Current;
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
                File.Copy(SettingsFilePath, SettingsFilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch
        {
            // Backup is best effort only.
        }
    }
}
