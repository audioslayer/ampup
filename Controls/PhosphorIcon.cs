using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AmpUp.Controls;

/// <summary>
/// Renders a Phosphor Duotone icon from the PhosphorIcons.xaml resource dictionary.
/// The base layer renders at 0.2 opacity (duotone fill), the detail layer at full opacity.
/// Usage: &lt;controls:PhosphorIcon IconName="Microphone" IconColor="White" IconSize="20" /&gt;
/// </summary>
public class PhosphorIcon : Grid
{
    public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register(
        nameof(IconName), typeof(string), typeof(PhosphorIcon),
        new FrameworkPropertyMetadata("", OnIconChanged));

    public string IconName
    {
        get => (string)GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public static readonly DependencyProperty IconColorProperty = DependencyProperty.Register(
        nameof(IconColor), typeof(Color), typeof(PhosphorIcon),
        new FrameworkPropertyMetadata(Color.FromRgb(0xE8, 0xE8, 0xE8), OnIconChanged));

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(PhosphorIcon),
        new FrameworkPropertyMetadata(20.0, OnIconChanged));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly DependencyProperty BaseOpacityProperty = DependencyProperty.Register(
        nameof(BaseOpacity), typeof(double), typeof(PhosphorIcon),
        new FrameworkPropertyMetadata(0.2, OnIconChanged));

    public double BaseOpacity
    {
        get => (double)GetValue(BaseOpacityProperty);
        set => SetValue(BaseOpacityProperty, value);
    }

    private readonly Path _basePath = new();
    private readonly Path _detailPath = new();

    public PhosphorIcon()
    {
        Width = 20;
        Height = 20;
        _basePath.Stretch = Stretch.Uniform;
        _detailPath.Stretch = Stretch.Uniform;
        Children.Add(_basePath);
        Children.Add(_detailPath);
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PhosphorIcon)d).UpdateIcon();
    }

    private void UpdateIcon()
    {
        Width = IconSize;
        Height = IconSize;

        var name = IconName;
        if (string.IsNullOrEmpty(name))
        {
            _basePath.Data = null;
            _detailPath.Data = null;
            return;
        }

        var baseKey = $"Ph.{name}.Base";
        var detailKey = $"Ph.{name}.Detail";

        _basePath.Data = TryFindResource(baseKey) as Geometry;
        _detailPath.Data = TryFindResource(detailKey) as Geometry;

        var baseBrush = new SolidColorBrush(IconColor);
        baseBrush.Opacity = BaseOpacity;
        baseBrush.Freeze();

        var detailBrush = new SolidColorBrush(IconColor);
        detailBrush.Freeze();

        _basePath.Fill = baseBrush;
        _detailPath.Fill = detailBrush;
    }
}
