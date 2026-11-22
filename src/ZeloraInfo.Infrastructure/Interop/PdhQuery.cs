// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Runtime.InteropServices;

namespace ZeloraInfo.Infrastructure.Interop;

/// <summary>Minimal Performance Data Helper wrapper (English counter paths, locale independent).</summary>
internal sealed partial class PdhQuery : IDisposable
{
    private IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = new(StringComparer.Ordinal);

    public PdhQuery()
    {
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) _query = IntPtr.Zero;
        }
        catch (DllNotFoundException)
        {
            _query = IntPtr.Zero;
        }
    }

    public bool IsValid => _query != IntPtr.Zero;

    public bool Add(string path)
    {
        if (!IsValid || _counters.ContainsKey(path)) return _counters.ContainsKey(path);
        if (PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter) != 0) return false;
        _counters[path] = counter;
        return true;
    }

    public bool Collect() => IsValid && PdhCollectQueryData(_query) == 0;

    public double? Read(string path)
    {
        if (!_counters.TryGetValue(path, out var counter)) return null;
        const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var value) != 0) return null;
        return value.CStatus is 0 or 1 ? value.DoubleValue : null;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(IntPtr query);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(IntPtr query);
}
