// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Management;

namespace ZeloraInfo.Infrastructure.Interop;

/// <summary>Defensive WMI helpers: a failing query yields an empty list instead of an exception.</summary>
internal static class Wmi
{
    public const string Cimv2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
    public const string RootWmi = @"root\wmi";

    public static List<ManagementBaseObject> Query(string scope, string query)
    {
        var result = new List<ManagementBaseObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            searcher.Options.Timeout = TimeSpan.FromSeconds(15);
            foreach (var o in searcher.Get())
                result.Add(o);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Namespace/class not available or access denied: callers fall back gracefully.
        }
        return result;
    }

    public static object? Get(this ManagementBaseObject o, string name)
    {
        try { return o[name]; }
        catch (ManagementException) { return null; }
    }

    public static string Str(this ManagementBaseObject o, string name) => (o.Get(name)?.ToString() ?? "").Trim();

    public static long? Long(this ManagementBaseObject o, string name)
    {
        var v = o.Get(name);
        if (v is null) return null;
        try { return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    public static ulong? ULong(this ManagementBaseObject o, string name)
    {
        var v = o.Get(name);
        if (v is null) return null;
        try { return Convert.ToUInt64(v, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    public static int? Int(this ManagementBaseObject o, string name)
    {
        var l = o.Long(name);
        return l is null || l > int.MaxValue || l < int.MinValue ? null : (int)l;
    }

    public static bool? Bool(this ManagementBaseObject o, string name) => o.Get(name) is bool b ? b : null;

    public static DateTime? Date(this ManagementBaseObject o, string name)
    {
        var s = o.Str(name);
        if (string.IsNullOrEmpty(s)) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s); }
        catch { return null; }
    }
}
