using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CoreEngine.Hub;

/// <summary>
/// The Hub's page (HubView.xaml). Besides what the model binds, it moves a little as HoYoPlay's page does: the picture
/// settles in when the window opens and the words slide in after it; the picture follows the mouse a few pixels; dust
/// drifts up in the light round the robot; and the news banners change every few seconds, or with a click on their dots.
/// None of it runs for the pictures made without a window (--shots): the page is drawn as it stands.
/// </summary>
public partial class HubView : UserControl
{
    const double BannerSeconds = 6.5;
    readonly DispatcherTimer bannerTimer = new() { Interval = TimeSpan.FromSeconds(BannerSeconds) };
    int banner;

    public HubView()
    {
        InitializeComponent();
        // A long folder shows its end, the part that says where the game goes (…\Programs\CoreEngine).
        PathBox.TextChanged += (_, _) => Dispatcher.BeginInvoke(() => PathBox.ScrollToHorizontalOffset(double.MaxValue), DispatcherPriority.Loaded);
        TopBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try
            {
                Window.GetWindow(this)?.DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };
        foreach (var dot in BannerDots.Children.OfType<Button>())
            dot.Click += (_, _) =>
            {
                ShowBanner(int.Parse((string)dot.Tag, CultureInfo.InvariantCulture));
                bannerTimer.Stop(); // a click holds the banner a full turn
                bannerTimer.Start();
            };
        bannerTimer.Tick += (_, _) => ShowBanner((banner + 1) % 3);
        Loaded += (_, _) => Arrive();
        Unloaded += (_, _) => bannerTimer.Stop();
        Page.MouseMove += (_, e) => Follow(e.GetPosition(Page));
        Page.MouseLeave += (_, _) => Follow(null);
    }

    Grid BannerAt(int i) => i == 0 ? Banner0 : i == 1 ? Banner1 : Banner2;

    /// <summary>One banner fades in over the one shown; its dot grows into a gold bar.</summary>
    void ShowBanner(int next)
    {
        if (next == banner) return;
        var from = BannerAt(banner);
        var to = BannerAt(next);
        var time = TimeSpan.FromMilliseconds(600);
        to.Visibility = Visibility.Visible;
        Panel.SetZIndex(to, 1);
        Panel.SetZIndex(from, 0);
        var fade = new DoubleAnimation(0, 1, time) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        fade.Completed += (_, _) =>
        {
            if (banner != next) return;
            from.BeginAnimation(OpacityProperty, null);
            from.Opacity = 0;
            from.Visibility = Visibility.Hidden;
        };
        to.BeginAnimation(OpacityProperty, fade);
        banner = next;
        foreach (var dot in BannerDots.Children.OfType<Button>())
        {
            bool on = (string)dot.Tag == next.ToString(CultureInfo.InvariantCulture);
            dot.BeginAnimation(WidthProperty, new DoubleAnimation(on ? 20 : 8, TimeSpan.FromMilliseconds(250)));
            dot.Background = on ? (Brush)FindResource("Gold") : new SolidColorBrush(Color.FromArgb(0x80, 0xFB, 0xF6, 0xEC));
        }
    }

    /// <summary>The window opens: the picture settles from a little closer, the words and the button slide in after it.</summary>
    void Arrive()
    {
        bannerTimer.Start();
        var settle = new CubicEase { EasingMode = EasingMode.EaseOut };
        BgScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.08, 1.03, TimeSpan.FromSeconds(1.6)) { EasingFunction = settle });
        BgScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.08, 1.03, TimeSpan.FromSeconds(1.6)) { EasingFunction = settle });
        SlideIn(Title, new Vector(-24, 0), 0.15);
        SlideIn(News, new Vector(0, 0), 0.3); // glass fades in where it stands: its frost is drawn for where it is
        SlideIn(Action, new Vector(0, 22), 0.4);
        MakeMotes();
    }

    static void SlideIn(FrameworkElement element, Vector from, double delay)
    {
        var shift = new TranslateTransform(from.X, from.Y);
        element.RenderTransform = shift;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var begin = TimeSpan.FromSeconds(delay);
        var time = new Duration(TimeSpan.FromSeconds(0.7));
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from.X, 0, time) { BeginTime = begin, EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from.Y, 0, time) { BeginTime = begin, EasingFunction = ease });
        element.Opacity = 0;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, time) { BeginTime = begin, EasingFunction = ease });
    }

    /// <summary>The picture leans a few pixels away from the mouse, as if seen through a window; back to rest when it leaves.</summary>
    void Follow(Point? mouse)
    {
        double x = 0, y = 0;
        if (mouse is { } at && Page.ActualWidth > 0)
        {
            x = -(at.X / Page.ActualWidth - 0.5) * 16;
            y = -(at.Y / Page.ActualHeight - 0.5) * 9;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var time = TimeSpan.FromSeconds(0.9);
        BgShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, time) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        BgShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, time) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// A few motes of dust in the light round the robot, each drifting up and fading in and out at its own pace, 30 times
    /// a second at most. They sit over the picture, not in it, so the glass need not redraw for them.
    /// </summary>
    void MakeMotes()
    {
        if (Motes.Children.Count > 0) return;
        var random = new Random(7);
        for (int i = 0; i < 18; i++)
        {
            double size = 1.6 + random.NextDouble() * 2.4;
            var mote = new Ellipse
            {
                Width = size,
                Height = size,
                Opacity = 0,
                Fill = new RadialGradientBrush(Color.FromArgb(0xF0, 0xFF, 0xF1, 0xCC), Color.FromArgb(0x00, 0xFF, 0xE0, 0xA0)),
            };
            Canvas.SetLeft(mote, 640 + random.NextDouble() * 560);
            Canvas.SetTop(mote, 150 + random.NextDouble() * 380);
            var drift = new TranslateTransform();
            mote.RenderTransform = drift;
            Motes.Children.Add(mote);

            double seconds = 9 + random.NextDouble() * 8;
            var story = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(random.NextDouble() * 8) };
            Timeline.SetDesiredFrameRate(story, 30);
            var rise = new DoubleAnimation(0, -(50 + random.NextDouble() * 70), TimeSpan.FromSeconds(seconds));
            var sway = new DoubleAnimation(0, (random.NextDouble() - 0.5) * 40, TimeSpan.FromSeconds(seconds));
            var glow = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds) };
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            glow.KeyFrames.Add(new EasingDoubleKeyFrame(0.55 + random.NextDouble() * 0.35, KeyTime.FromPercent(0.35), new SineEase()));
            glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new SineEase()));
            Storyboard.SetTarget(rise, mote);
            Storyboard.SetTargetProperty(rise, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            Storyboard.SetTarget(sway, mote);
            Storyboard.SetTargetProperty(sway, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
            Storyboard.SetTarget(glow, mote);
            Storyboard.SetTargetProperty(glow, new PropertyPath(OpacityProperty));
            story.Children.Add(rise);
            story.Children.Add(sway);
            story.Children.Add(glow);
            story.Begin();
        }
    }
}

/// <summary>Labels in capitals, as HoYoPlay's small headings.</summary>
public sealed class UpperConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string)?.ToUpperInvariant() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Shown when the text is not empty (the big button's icon).</summary>
public sealed class NotEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>A choice among several (the settings' chips): checked when the setting is this one's number.</summary>
public sealed class IndexIsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int index && int.TryParse(parameter as string, out int mine) && index == mine;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && int.TryParse(parameter as string, out int mine) ? mine : Binding.DoNothing;
}
