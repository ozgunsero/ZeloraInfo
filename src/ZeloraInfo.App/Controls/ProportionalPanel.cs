// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Windows;
using System.Windows.Controls;

namespace ZeloraInfo.App.Controls;

/// <summary>
/// Lays children out horizontally with widths proportional to the attached <c>Weight</c> property —
/// used for the stacked memory composition bar.
/// </summary>
public sealed class ProportionalPanel : Panel
{
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
        "Weight", typeof(double), typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsParentArrange | FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static double GetWeight(DependencyObject element) => (double)element.GetValue(WeightProperty);
    public static void SetWeight(DependencyObject element, double value) => element.SetValue(WeightProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var total = 0d;
        foreach (UIElement child in InternalChildren) total += Math.Max(0, GetWeight(ContainerOf(child)));
        var x = 0d;
        foreach (UIElement child in InternalChildren)
        {
            var w = total <= 0 ? 0 : finalSize.Width * Math.Max(0, GetWeight(ContainerOf(child))) / total;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
        }
        return finalSize;
    }

    /// <summary>ItemsControl wraps items in a ContentPresenter; the weight is set on the presenter via ItemContainerStyle.</summary>
    private static DependencyObject ContainerOf(UIElement child) => child;
}
