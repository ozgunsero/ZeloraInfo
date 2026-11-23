// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ZeloraInfo.Core.Disks;

namespace ZeloraInfo.App.Converters;

/// <summary>Maps HealthStatus / AttributeStatus / TemperatureLevel to palette brushes (parameter "Soft" for backgrounds).</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value switch
        {
            HealthStatus.Good or AttributeStatus.Ok or TemperatureLevel.Normal => "Good",
            HealthStatus.Caution or AttributeStatus.Caution or TemperatureLevel.Caution => "Caution",
            HealthStatus.Bad or AttributeStatus.Bad or TemperatureLevel.Critical => "Bad",
            _ => "Unknown"
        };
        var suffix = parameter as string == "Soft" ? "Soft" : "";
        return Application.Current.TryFindResource($"Brush.{level}{suffix}") as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Bytes (ulong/long/double) → "1.2 GiB" in the current UI culture.</summary>
public sealed class BytesToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double? bytes = value switch
        {
            ulong u => u,
            long l => l,
            double d => d,
            int i => i,
            _ => null
        };
        return bytes is null ? "—" : Core.Formatting.ByteFormatter.FormatBinary(bytes.Value, 1, Core.Localization.LanguageCatalog.CurrentCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value is null || value is string s && string.IsNullOrWhiteSpace(s) || value is System.Collections.ICollection { Count: 0 };
        var invert = parameter as string == "Invert";
        return empty ^ invert ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Equality → bool (for RadioButton groups bound to enums/strings).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value?.ToString(), parameter?.ToString());
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? parameter! : Binding.DoNothing;
}
