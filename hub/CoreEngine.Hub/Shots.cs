using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CoreEngine.Hub;

/// <summary>
/// Pictures of the Hub's window, made without showing it (nothing appears on the desktop while they are made): the
/// real window's page, driven through a real install from a release (--source) into a test folder (--home), drawn at
/// each step into a 1920 × 1080 picture, with a line per step in shots.log. And the Hub's icon (--icon).
/// </summary>
static class Shots
{
    public static async Task<bool> RunAsync(Arguments args)
    {
        var log = new StringBuilder();
        bool good = true;
        try
        {
            if (args.Icon != null)
            {
                WriteIcon(args.Icon);
                log.AppendLine("icon: " + args.Icon);
            }
            if (args.Shots != null) await Walk(args, log);
        }
        catch (Exception e)
        {
            log.AppendLine("FAILED: " + e);
            good = false;
        }
        string where = args.Shots ?? Path.GetDirectoryName(Path.GetFullPath(args.Icon!))!;
        Directory.CreateDirectory(where);
        File.WriteAllText(Path.Combine(where, "shots.log"), log.ToString());
        return good;
    }

    static async Task Walk(Arguments args, StringBuilder log)
    {
        string dir = Path.GetFullPath(args.Shots!);
        Directory.CreateDirectory(dir);
        if (args.Source == null && HubModel.LocalRelease() == null) throw new ArgumentException("--shots needs --source (a release) or a release folder beside the Hub");
        if (!HubHome.IsSandbox) throw new ArgumentException("--shots needs --home: it installs the game there, not in the player's folders");

        var settings = HubSettings.Load(); // the test folder's: a game installed by an earlier run is updated
        settings.Language = 0;
        settings.SpeedLimitMBps = 10;
        var model = new HubModel(settings, args.Source);
        var view = new HubView { DataContext = model };
        Render(view, null); // the pictures in it are decoded on the first drawing
        async Task Shot(string name)
        {
            await Settle();
            Render(view, Path.Combine(dir, name + ".png"));
            log.AppendLine($"{name}: {model.State}; button \"{model.MainText}\" ({(model.MainEnabled ? "on" : "off")}); " +
                           $"progress \"{model.ProgressLeft}\" \"{model.ProgressRight}\"; status \"{model.Status}\"");
        }

        await model.CheckAsync();
        if (model.State == HubState.UpdateAvailable)
        {
            // An update: what is new, how much to download, then only the files that changed.
            string before = InstallState.Load(settings.InstallDir)?.Version ?? "";
            await Shot("u1-update");
            model.SpeedIndex = 0;
            model.MainCommand.Execute(null); // Update
            await WaitFor(() => model.State is HubState.Ready or HubState.Error, TimeSpan.FromMinutes(20));
            await WaitFor(() => !model.Preparing, TimeSpan.FromMinutes(7));
            await Shot("u2-updated");
            log.AppendLine($"updated from {before} to {InstallState.Load(settings.InstallDir)?.Version}");
            return;
        }
        await Shot("01-download");
        model.MainCommand.Execute(null); // Download: where to install
        await Shot("02-install");
        log.AppendLine($"   install to {model.InstallDir}: {model.InstallSpace}");
        model.InstallCommand.Execute(null);
        await WaitFor(() => model.State == HubState.Downloading && model.Progress > 0.3 || model.State is HubState.Ready or HubState.Error, TimeSpan.FromMinutes(5));
        await Shot("03-downloading");
        model.MainCommand.Execute(null); // Pause
        await WaitFor(() => model.State == HubState.Paused, TimeSpan.FromSeconds(30));
        await Shot("04-paused");
        model.SpeedIndex = 0; // no limit for the rest
        model.MainCommand.Execute(null); // Resume
        await WaitFor(() => model.State is HubState.Ready or HubState.Error, TimeSpan.FromMinutes(20));
        await Shot("05-ready-preparing");
        var warm = System.Diagnostics.Stopwatch.StartNew();
        await WaitFor(() => !model.Preparing, TimeSpan.FromMinutes(7));
        log.AppendLine($"   the Arduino compiler was prepared in {warm.Elapsed.TotalSeconds:F0} s");
        await Shot("05-ready");
        if (model.MessageOpen) model.CloseDialogCommand.Execute(null);

        model.MenuCommand.Execute(null);
        await Shot("06-menu");
        model.CloseDialogCommand.Execute(null);
        model.SettingsCommand.Execute(null);
        await Shot("07-settings");
        model.CloseSettingsCommand.Execute(null);
        model.UninstallCommand.Execute(null);
        await Shot("08-uninstall");
        model.CloseDialogCommand.Execute(null);
        model.LanguageIndex = 1;
        await Shot("09-ready-uz");
        model.LanguageIndex = 2;
        await Shot("10-ready-ru");
        model.LanguageIndex = 0;

        model.RepairCommand.Execute(null);
        await WaitFor(() => model.MessageOpen || model.State == HubState.Error, TimeSpan.FromMinutes(10));
        await Shot("11-repaired");
        model.CloseDialogCommand.Execute(null);

        // The same page for a player whose internet is down, before anything is installed.
        var offline = new HubModel(new HubSettings { Language = 0 }, "http://127.0.0.1:9/stable/");
        view.DataContext = offline;
        await offline.CheckAsync();
        await Settle();
        Render(view, Path.Combine(dir, "12-offline.png"));
        log.AppendLine($"12-offline: {offline.State}; button \"{offline.MainText}\"; status \"{offline.Status}\"");
        view.DataContext = model;

        var state = InstallState.Load(settings.InstallDir);
        log.AppendLine($"installed: {state?.Version} in {settings.InstallDir}: {state?.Files.Count} files, program {state?.Exe} " +
                       $"(there: {File.Exists(Path.Combine(settings.InstallDir, state?.Exe ?? "-"))}); desktop shortcut made: " +
                       $"{File.Exists(Path.Combine(HubHome.Desktop, "CoreEngine.lnk"))}");
    }

    static async Task WaitFor(Func<bool> done, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (!done())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("the Hub did not get there in time");
            await Task.Delay(50);
        }
    }

    static async Task Settle()
    {
        await Task.Delay(60);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    static void Render(FrameworkElement view, string? path)
    {
        view.Measure(new Size(1280, 720));
        view.Arrange(new Rect(0, 0, 1280, 720));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1920, 1080, 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(view);
        if (path == null) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    // ------------------------------------------------------------------ the icon

    /// <summary>The Hub's icon: the game's chip mark (its top bar's logo) in blue on the game's ink, in six sizes.</summary>
    static void WriteIcon(string path)
    {
        int[] sizes = { 256, 64, 48, 32, 24, 16 };
        var images = sizes.Select(Logo).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) writer.Write(image);
    }

    static byte[] Logo(int size)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x0C, 0x0E, 0x13));
        var blue = new SolidColorBrush(Color.FromRgb(0x82, 0xD8, 0xFF));
        var pen = new Pen(blue, 1.9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double k = size / 24.0;
            dc.PushTransform(new ScaleTransform(k, k));
            dc.DrawRoundedRectangle(ink, null, new Rect(0, 0, 24, 24), 5, 5);
            dc.DrawRoundedRectangle(null, pen, new Rect(5.5, 5.5, 13, 13), 2.2, 2.2);
            dc.DrawRoundedRectangle(blue, null, new Rect(9.25, 9.25, 5.5, 5.5), 0.8, 0.8);
            foreach (var (x1, y1, x2, y2) in new[]
                     {
                         (9.0, 2.2, 9.0, 5.2), (15.0, 2.2, 15.0, 5.2), (9.0, 18.8, 9.0, 21.8), (15.0, 18.8, 15.0, 21.8),
                         (2.2, 9.0, 5.2, 9.0), (2.2, 15.0, 5.2, 15.0), (18.8, 9.0, 21.8, 9.0), (18.8, 15.0, 21.8, 15.0),
                     })
                dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }
}
