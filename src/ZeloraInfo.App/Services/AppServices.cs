// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.IO;
using System.Reflection;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.Licensing;
using ZeloraInfo.Infrastructure.Benchmark;
using ZeloraInfo.Infrastructure.Cpu;
using ZeloraInfo.Infrastructure.Disks;
using ZeloraInfo.Infrastructure.Gpu;
using ZeloraInfo.Infrastructure.Memory;
using ZeloraInfo.Infrastructure.Platform;
using ZeloraInfo.Infrastructure.Settings;
using ZeloraInfo.Infrastructure.SystemInfo;

namespace ZeloraInfo.App.Services;

/// <summary>Composition root: one instance of every service for the app lifetime.</summary>
public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        Settings = new JsonSettingsService();
        Privileges = new PrivilegeService();
        Startup = new StartupRegistrationService();
        Disks = new DiskInfoService(Privileges);
        Benchmark = new BenchmarkService();
        var systemInfo = new SystemInfoService(Privileges);
        SystemInfo = systemInfo;
        Display = new DisplayInfoService();
        var memory = new MemoryInfoService();
        Memory = memory;
        CpuZ = new CpuZService();
        RamZ = new RamZService(memory);
        Gpu = new GpuService();
        MemoryMap = new MemoryMapService(Privileges);
        Localization = new LocalizationManager();
        Theme = new ThemeManager();
        Ui = new UiService();
        Tray = new TrayIconService();
    }

    public ISettingsService Settings { get; }
    public IPrivilegeService Privileges { get; }
    public IStartupRegistrationService Startup { get; }
    public IDiskInfoService Disks { get; }
    public IBenchmarkService Benchmark { get; }
    public ISystemInfoService SystemInfo { get; }
    public IDisplayInfoService Display { get; }
    public IMemoryInfoService Memory { get; }
    public ICpuZService CpuZ { get; }
    public IRamZService RamZ { get; }
    public IGpuService Gpu { get; }
    public IMemoryMapService MemoryMap { get; }
    public LocalizationManager Localization { get; }
    public ThemeManager Theme { get; }
    public UiService Ui { get; }
    public TrayIconService Tray { get; }

    public void Dispose()
    {
        Tray.Dispose();
        (SystemInfo as IDisposable)?.Dispose();
        (CpuZ as IDisposable)?.Dispose();
        (Gpu as IDisposable)?.Dispose();
        (MemoryMap as IDisposable)?.Dispose();
    }
}

/// <summary>Version / build metadata and the embedded legal documents.</summary>
public static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static string Version => Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public static string InformationalVersion =>
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Version;

    public static DateTime BuildDate
    {
        get
        {
            try
            {
                var path = Environment.ProcessPath;
                return path is null ? DateTime.Now : File.GetLastWriteTime(path);
            }
            catch
            {
                return DateTime.Now;
            }
        }
    }

    public static string Runtime => $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})";

    public static string ReadEmbedded(string logicalName)
    {
        using var stream = Assembly.GetManifestResourceStream(logicalName);
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string LicenseText => ReadEmbedded("ZeloraInfo.LICENSE.txt");

    public static ThirdPartyManifest LoadThirdPartyManifest() => ThirdPartyManifest.Parse(ReadEmbedded("ZeloraInfo.third-party-licenses.json"));
}
