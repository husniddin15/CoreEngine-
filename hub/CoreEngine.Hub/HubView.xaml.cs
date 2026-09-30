using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace CoreEngine.Hub;

public partial class HubView : UserControl
{
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
    }
}

/// <summary>Labels in capitals, as the game's (and HoYoPlay's) buttons and headings.</summary>
public sealed class UpperConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string)?.ToUpperInvariant() ?? "";

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
