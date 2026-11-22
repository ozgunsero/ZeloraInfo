// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Runtime.InteropServices;
using ZeloraInfo.Core.Abstractions;
using ZeloraInfo.Core.Formatting;
using ZeloraInfo.Core.SystemInfo;
using ZeloraInfo.Infrastructure.Interop;

namespace ZeloraInfo.Infrastructure.SystemInfo;

/// <summary>
/// Monitor information from GDI (EnumDisplayMonitors / EnumDisplaySettings), Shcore (per-monitor DPI),
/// the DisplayConfig API (friendly name, connector, preferred mode, HDR / advanced color) and
/// WmiMonitorBasicDisplayParams (physical size from EDID).
/// </summary>
public sealed class DisplayInfoService : IDisplayInfoService
{
    public DisplaySnapshot GetSnapshot()
    {
        var monitors = new List<(IntPtr Handle, MONITORINFOEX Info)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(h, ref mi)) monitors.Add((h, mi));
            return true;
        }, IntPtr.Zero);

        var adapters = GetAdapterNames();
        var paths = DisplayConfig.Read();
        var physical = ReadPhysicalSizes();

        var list = new List<DisplayInfo>();
        foreach (var (handle, mi) in monitors)
        {
            var current = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            EnumDisplaySettings(mi.szDevice, ENUM_CURRENT_SETTINGS, ref current);

            // Highest refresh rate available at the current resolution, and the largest resolution overall.
            double maxRefresh = current.dmDisplayFrequency;
            (int W, int H) maxRes = ((int)current.dmPelsWidth, (int)current.dmPelsHeight);
            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            for (var i = 0; EnumDisplaySettings(mi.szDevice, i, ref mode) && i < 2000; i++)
            {
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight)
                    maxRefresh = Math.Max(maxRefresh, mode.dmDisplayFrequency);
                if ((long)mode.dmPelsWidth * mode.dmPelsHeight > (long)maxRes.W * maxRes.H)
                    maxRes = ((int)mode.dmPelsWidth, (int)mode.dmPelsHeight);
            }

            uint dpiX = 96;
            try
            {
                if (GetDpiForMonitor(handle, 0, out var dx, out _) == 0) dpiX = dx;
            }
            catch (DllNotFoundException)
            {
            }

            paths.TryGetValue(mi.szDevice, out var path);
            (double W, double H)? size = null;
            if (path?.DevicePath is { } dp && physical.TryGetValue(NormalizeInstance(dp), out var s)) size = s;

            var width = (int)current.dmPelsWidth;
            var height = (int)current.dmPelsHeight;
            double? diagonal = size is { } sz && sz.W > 0 && sz.H > 0 ? Math.Sqrt(sz.W * sz.W + sz.H * sz.H) / 2.54 : null;

            list.Add(new DisplayInfo
            {
                DeviceName = mi.szDevice,
                FriendlyName = string.IsNullOrWhiteSpace(path?.FriendlyName) ? mi.szDevice.Replace(@"\\.\", "") : path!.FriendlyName!,
                AdapterName = adapters.TryGetValue(mi.szDevice, out var a) ? a : "",
                IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                Width = width,
                Height = height,
                PreferredWidth = path?.PreferredWidth ?? maxRes.W,
                PreferredHeight = path?.PreferredHeight ?? maxRes.H,
                RefreshRateHz = path?.CurrentRefresh ?? current.dmDisplayFrequency,
                PreferredRefreshRateHz = path?.PreferredRefresh,
                MaxRefreshRateHz = maxRefresh,
                BitsPerPixel = (int)current.dmBitsPerPel,
                BitsPerColorChannel = path?.BitsPerChannel,
                HdrSupported = path?.HdrSupported,
                HdrEnabled = path?.HdrEnabled,
                DpiX = dpiX,
                ScalePercent = (int)Math.Round(dpiX * 100d / 96d),
                PhysicalWidthCm = size?.W,
                PhysicalHeightCm = size?.H,
                DiagonalInches = diagonal is null ? null : Math.Round(diagonal.Value, 1),
                AspectRatio = AspectRatioHelper.Describe(width, height),
                Orientation = current.dmDisplayOrientation switch
                {
                    1 => "Portrait",
                    2 => "LandscapeFlipped",
                    3 => "PortraitFlipped",
                    _ => "Landscape"
                },
                ConnectionType = path?.Connection ?? "",
                Left = mi.rcMonitor.Left, Top = mi.rcMonitor.Top, Right = mi.rcMonitor.Right, Bottom = mi.rcMonitor.Bottom,
                WorkLeft = mi.rcWork.Left, WorkTop = mi.rcWork.Top, WorkRight = mi.rcWork.Right, WorkBottom = mi.rcWork.Bottom,
            });
        }

        list = list.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Left).ThenBy(d => d.Top).ToList();
        return new DisplaySnapshot
        {
            Displays = list,
            VirtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN),
            VirtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN),
            VirtualWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN),
            VirtualHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN),
        };
    }

    /// <summary>"\\?\DISPLAY#DEL40B5#5&amp;1a2b&amp;0&amp;UID4352#{guid}" → "DISPLAY\DEL40B5\5&amp;1A2B&amp;0&amp;UID4352"</summary>
    internal static string NormalizeInstance(string value)
    {
        var s = value.Trim();
        if (s.StartsWith(@"\\?\", StringComparison.Ordinal)) s = s[4..];
        var brace = s.IndexOf("#{", StringComparison.Ordinal);
        if (brace >= 0) s = s[..brace];
        s = s.Replace('#', '\\');
        if (s.Length > 2 && s[^2] == '_' && char.IsDigit(s[^1])) s = s[..^2]; // WMI InstanceName suffix "_0"
        return s.ToUpperInvariant();
    }

    private static Dictionary<string, (double W, double H)> ReadPhysicalSizes()
    {
        var map = new Dictionary<string, (double, double)>();
        foreach (var o in Wmi.Query(Wmi.RootWmi, "SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams"))
        {
            var w = o.Int("MaxHorizontalImageSize") ?? 0;
            var h = o.Int("MaxVerticalImageSize") ?? 0;
            if (w > 0 && h > 0) map[NormalizeInstance(o.Str("InstanceName"))] = (w, h);
        }
        return map;
    }

    private static Dictionary<string, string> GetAdapterNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            map[dd.DeviceName] = dd.DeviceString;
            dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        }
        return map;
    }

    // ------------------------------------------------------------------ Win32

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint MONITORINFOF_PRIMARY = 1;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    private static extern bool EnumDisplayDevices(string? device, uint devNum, ref DISPLAY_DEVICE displayDevice, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}

/// <summary>Wrapper around QueryDisplayConfig / DisplayConfigGetDeviceInfo.</summary>
internal static class DisplayConfig
{
    internal sealed record PathData(string? FriendlyName, string? DevicePath, string Connection, double? CurrentRefresh,
        int? PreferredWidth, int? PreferredHeight, double? PreferredRefresh, bool? HdrSupported, bool? HdrEnabled, int? BitsPerChannel);

    public static Dictionary<string, PathData> Read()
    {
        var result = new Dictionary<string, PathData>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0) return result;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;

            for (var i = 0; i < pathCount; i++)
            {
                var p = paths[i];
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = Header(1, Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), p.sourceInfo.adapterId, p.sourceInfo.id)
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = Header(2, Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(), p.targetInfo.adapterId, p.targetInfo.id)
                };
                var hasTarget = DisplayConfigGetDeviceInfo(ref target) == 0;

                var preferred = new DISPLAYCONFIG_TARGET_PREFERRED_MODE
                {
                    header = Header(3, Marshal.SizeOf<DISPLAYCONFIG_TARGET_PREFERRED_MODE>(), p.targetInfo.adapterId, p.targetInfo.id)
                };
                var hasPreferred = DisplayConfigGetDeviceInfo(ref preferred) == 0;

                var color = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = Header(9, Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(), p.targetInfo.adapterId, p.targetInfo.id)
                };
                var hasColor = DisplayConfigGetDeviceInfo(ref color) == 0;

                double? Rate(uint num, uint den) => den == 0 ? null : Math.Round((double)num / den, 2);

                result[source.viewGdiDeviceName] = new PathData(
                    hasTarget ? target.monitorFriendlyDeviceName : null,
                    hasTarget ? target.monitorDevicePath : null,
                    Connection(hasTarget ? target.outputTechnology : p.targetInfo.outputTechnology),
                    Rate(p.targetInfo.refreshRate.Numerator, p.targetInfo.refreshRate.Denominator),
                    hasPreferred ? (int)preferred.width : null,
                    hasPreferred ? (int)preferred.height : null,
                    hasPreferred ? Rate(preferred.targetMode.vSyncFreq.Numerator, preferred.targetMode.vSyncFreq.Denominator) : null,
                    hasColor ? (color.value & 0x1) != 0 : null,
                    hasColor ? (color.value & 0x2) != 0 : null,
                    hasColor && color.bitsPerColorChannel > 0 ? (int)color.bitsPerColorChannel : null);
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
        return result;
    }

    private static string Connection(uint tech) => tech switch
    {
        0 => "VGA",
        1 => "S-Video",
        2 or 3 => "Composite",
        4 => "DVI",
        5 => "HDMI",
        6 => "LVDS",
        8 => "D-Jpn",
        9 => "SDI",
        10 => "DisplayPort",
        11 => "eDP",
        12 => "UDI",
        13 => "UDI (embedded)",
        14 => "SDTV",
        15 => "Miracast",
        16 => "Indirect (wired)",
        17 => "Indirect (virtual)",
        0x80000000 => "Internal",
        _ => "—"
    };

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER Header(uint type, int size, LUID adapter, uint id)
        => new() { type = type, size = (uint)size, adapterId = adapter, id = id };

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;
        public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_PREFERRED_MODE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint width;
        public uint height;
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out int numPathArrayElements, out int numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref int numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref int numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_PREFERRED_MODE request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO request);
}
