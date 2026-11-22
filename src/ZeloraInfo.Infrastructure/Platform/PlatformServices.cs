// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using ZeloraInfo.Core.Abstractions;

namespace ZeloraInfo.Infrastructure.Platform;

public sealed class PrivilegeService : IPrivilegeService
{
    private readonly Lazy<bool> _isAdmin = new(() =>
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    });

    public bool IsAdministrator => _isAdmin.Value;

    public bool TryRestartElevated(string? arguments = null)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = arguments ?? "--elevated"
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: user said "No" to UAC
        {
            return false;
        }
    }
}

/// <summary>Registers the app under HKCU\…\Run (per user, no elevation required).</summary>
public sealed class StartupRegistrationService : IStartupRegistrationService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZeloraInfo";

    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
    }

    public void SetRegistered(bool enabled, string arguments = "--minimized")
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable.");
            key.SetValue(ValueName, $"\"{exe}\" {arguments}".Trim());
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
