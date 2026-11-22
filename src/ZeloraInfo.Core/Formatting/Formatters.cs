// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Globalization;
using ZeloraInfo.Core.Settings;

namespace ZeloraInfo.Core.Formatting;

public static class ByteFormatter
{
    private static readonly string[] DecimalUnits = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
    private static readonly string[] BinaryUnits = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];

    /// <summary>Decimal units (1 GB = 10^9 bytes) — the convention used by disk vendors.</summary>
    public static string FormatDecimal(double bytes, int decimals = 1, CultureInfo? culture = null)
        => Format(bytes, 1000d, DecimalUnits, decimals, culture);

    /// <summary>Binary units (1 GiB = 2^30 bytes) — the convention used for RAM.</summary>
    public static string FormatBinary(double bytes, int decimals = 1, CultureInfo? culture = null)
        => Format(bytes, 1024d, BinaryUnits, decimals, culture);

    private static string Format(double bytes, double step, string[] units, int decimals, CultureInfo? culture)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (double.IsNaN(bytes) || bytes < 0) return "—";
        var unit = 0;
        while (bytes >= step && unit < units.Length - 1)
        {
            bytes /= step;
            unit++;
        }
        var format = unit == 0 ? "0" : "0." + new string('0', Math.Max(0, decimals));
        return bytes.ToString(format, culture) + " " + units[unit];
    }
}

public static class TemperatureFormatter
{
    public static double CelsiusToFahrenheit(double c) => c * 9d / 5d + 32d;
    public static double FahrenheitToCelsius(double f) => (f - 32d) * 5d / 9d;

    public static string Format(int? celsius, TemperatureUnit unit)
    {
        if (celsius is null) return "—";
        return unit == TemperatureUnit.Fahrenheit
            ? $"{Math.Round(CelsiusToFahrenheit(celsius.Value)):0} °F"
            : $"{celsius.Value} °C";
    }
}

public static class AspectRatioHelper
{
    /// <summary>Exact ratios mapped to the marketing names monitors are sold with (e.g. 8:5 → "16:10", 43:18 → "21:9").</summary>
    private static readonly (int W, int H, string Label)[] Common =
    [
        (4, 3, "4:3"), (5, 4, "5:4"), (3, 2, "3:2"), (8, 5, "16:10"), (16, 9, "16:9"), (17, 9, "17:9"),
        (64, 27, "21:9"), (43, 18, "21:9"), (12, 5, "21:9"), (7, 3, "21:9"), (32, 9, "32:9"), (32, 10, "32:10"),
        (1, 1, "1:1"), (3, 4, "3:4"), (9, 16, "9:16"), (5, 8, "10:16"),
    ];

    /// <summary>Returns a human-friendly ratio, snapping near-miss resolutions (1366×768 → 16:9, 3440×1440 → 21:9).</summary>
    public static string Describe(int width, int height)
    {
        if (width <= 0 || height <= 0) return "—";
        var ratio = (double)width / height;
        var best = Common.MinBy(c => Math.Abs((double)c.W / c.H - ratio));
        if (Math.Abs((double)best.W / best.H - ratio) < 0.012) return best.Label;
        var g = Gcd(width, height);
        var (w, h) = (width / g, height / g);
        return w <= 32 && h <= 32 ? $"{w}:{h}" : ratio.ToString("0.00", CultureInfo.InvariantCulture) + ":1";
    }

    public static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return Math.Abs(a);
    }
}

public static class DurationFormatter
{
    /// <summary>"3d 04:12:09" style (days only when non-zero).</summary>
    public static string Format(TimeSpan span, string dayUnit = "d")
        => span.Days > 0 ? $"{span.Days}{dayUnit} {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
}
