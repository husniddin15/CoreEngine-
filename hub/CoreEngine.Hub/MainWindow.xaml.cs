using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CoreEngine.Hub;

public partial class MainWindow : Window
{
    public MainWindow(HubModel model)
    {
        InitializeComponent();
        DataContext = model;
        model.WindowRequest += OnRequest;
        SourceInitialized += (_, _) => RoundCorners();
        FitToScreen();
    }

    void OnRequest(string what)
    {
        switch (what)
        {
            case "minimize":
                WindowState = WindowState.Minimized;
                break;
            case "restore":
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                break;
            case "close":
                Close();
                break;
        }
    }

    /// <summary>1280 × 720 where it fits; smaller, the same page scaled, on a small laptop screen.</summary>
    void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        double scale = Math.Min(1, Math.Min((area.Width - 24) / 1280, (area.Height - 24) / 720));
        Width = Math.Round(1280 * scale);
        Height = Math.Round(720 * scale);
    }

    /// <summary>Round corners on Windows 11, as its own windows have (Windows 10 keeps square ones).</summary>
    void RoundCorners()
    {
        const int DwmwaWindowCornerPreference = 33, DwmwcpRound = 2;
        int preference = DwmwcpRound;
        try
        {
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
