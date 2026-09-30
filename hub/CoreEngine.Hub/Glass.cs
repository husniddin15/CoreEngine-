using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace CoreEngine.Hub;

/// <summary>
/// Frosted glass, as HoYoPlay's panels are: the page's picture right behind the panel, blurred, under a tint and a
/// hairline. The picture comes from <see cref="Backdrop"/> (the page's background) through a brush that shows the part of
/// it behind the panel, so the glass follows the background as it moves. Its look is the implicit style in HubView.xaml.
/// </summary>
public sealed class Glass : ContentControl
{
    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(
        nameof(Backdrop), typeof(FrameworkElement), typeof(Glass), new PropertyMetadata(null, (d, _) => ((Glass)d).Follow()));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(Glass), new PropertyMetadata(new CornerRadius(0), (d, _) => ((Glass)d).ClipToCorners()));

    public static readonly DependencyProperty BlurProperty = DependencyProperty.Register(
        nameof(Blur), typeof(double), typeof(Glass), new PropertyMetadata(30.0, (d, _) => ((Glass)d).Frost()));

    readonly VisualBrush brush = new() { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
    Rectangle? blurred;
    Grid? root;

    public Glass()
    {
        LayoutUpdated += (_, _) => Follow();
    }

    /// <summary>The element whose picture shows through, blurred: the page's background, never an ancestor of the glass.</summary>
    public FrameworkElement? Backdrop
    {
        get => (FrameworkElement?)GetValue(BackdropProperty);
        set => SetValue(BackdropProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>The blur's radius (px): how frosted the glass is.</summary>
    public double Blur
    {
        get => (double)GetValue(BlurProperty);
        set => SetValue(BlurProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        root = GetTemplateChild("PART_Root") as Grid;
        blurred = GetTemplateChild("PART_Blur") as Rectangle;
        if (root != null) root.SizeChanged += (_, _) => ClipToCorners();
        Frost();
        ClipToCorners();
    }

    void Frost()
    {
        if (blurred == null) return;
        // Wider than the panel by the blur, so its edges blur the picture round it, not the dark outside it.
        blurred.Margin = new Thickness(-Blur);
        blurred.Fill = brush;
        blurred.IsHitTestVisible = false;
        blurred.Effect = Blur > 0 ? new BlurEffect { Radius = Blur, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance } : null;
        brush.Visual = Backdrop;
        Follow();
    }

    /// <summary>Points the brush at the part of the backdrop right behind the blurred rectangle.</summary>
    void Follow()
    {
        var backdrop = Backdrop;
        if (blurred == null || backdrop == null || blurred.ActualWidth <= 0) return;
        if (brush.Visual != backdrop) brush.Visual = backdrop;
        Point at;
        try
        {
            at = blurred.TransformToVisual(backdrop).Transform(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return; // not in the same tree yet
        }
        var box = new Rect(at.X, at.Y, blurred.ActualWidth, blurred.ActualHeight);
        if (brush.Viewbox != box) brush.Viewbox = box;
    }

    void ClipToCorners()
    {
        if (root == null) return;
        double r = CornerRadius.TopLeft;
        root.Clip = new RectangleGeometry(new Rect(root.RenderSize), r, r);
    }
}

/// <summary>Small movements of the Hub's page, as HoYoPlay's: a dialog or a menu grows in and fades in when it opens.</summary>
public static class Motion
{
    public static readonly DependencyProperty PopInProperty = DependencyProperty.RegisterAttached(
        "PopIn", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnPopIn));

    public static bool GetPopIn(DependencyObject element) => (bool)element.GetValue(PopInProperty);

    public static void SetPopIn(DependencyObject element, bool value) => element.SetValue(PopInProperty, value);

    static void OnPopIn(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true) return;
        element.IsVisibleChanged += (_, visible) =>
        {
            if (visible.NewValue is true) Pop(element);
        };
    }

    /// <summary>From 96 % and clear to its size and solid in 0.18 s; pictures made without a window (--shots) show it still.</summary>
    static void Pop(FrameworkElement element)
    {
        if (PresentationSource.FromVisual(element) == null) return;
        var scale = new ScaleTransform(0.96, 0.96);
        element.RenderTransform = scale;
        var time = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, time) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, time) { EasingFunction = ease });
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, time) { EasingFunction = ease });
    }
}
