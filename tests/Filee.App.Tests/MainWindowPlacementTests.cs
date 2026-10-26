// The main window opens where it was, also after Filee restarts (#586), and always fits the screen it opens on: a
// small or scaled screen gets a window that fits instead of one that runs off its bottom.

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Filee.App.Services;
using Filee.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Placement = Filee.App.Services.WindowService.MainWindowPlacement;

namespace Filee.App.Tests;

[Collection("MainWindow")]
public class MainWindowPlacementTests
{
    private static readonly PixelRect FullHd = new(0, 0, 1920, 1040); // 1920 × 1080 less the taskbar
    private static readonly Size Designed = new(1080, 760);
    private static readonly Size DesignedMin = new(820, 560);

    [Fact]
    public void A_window_that_fits_its_screen_keeps_its_size_and_place()
    {
        var (size, min, position) = Placement.Fit(FullHd, 1, new PixelPoint(200, 100), Designed, DesignedMin);

        Assert.Equal(Designed, size);
        Assert.Equal(DesignedMin, min);
        Assert.Equal(new PixelPoint(200, 100), position);
    }

    [Fact]
    public void On_a_small_scaled_screen_the_window_and_its_minimum_fit_the_working_area()
    {
        // 1366 × 768 at 125 %: 1093 × 584 device-independent pixels of working area (the taskbar takes 38).
        var area = new PixelRect(0, 0, 1366, 730);

        var (size, min, position) = Placement.Fit(area, 1.25, null, Designed, DesignedMin);

        var room = new Size(1366 / 1.25 - 2 * Placement.Margin, 730 / 1.25 - 2 * Placement.Margin - Placement.Frame);
        Assert.Equal(room, size); // 1076.8 × 528: a little narrower, and as tall as there is room
        Assert.Equal(new Size(DesignedMin.Width, room.Height), min); // the minimum gives way only where it must: height
        Assert.Null(position); // the first start: centred by the system
    }

    [Fact]
    public void A_window_that_would_stick_out_moves_back_inside_its_screen()
    {
        // Saved at the right edge of a screen that is smaller now.
        var (size, _, position) = Placement.Fit(FullHd, 1, new PixelPoint(1500, 900), Designed, DesignedMin);

        var at = Assert.IsType<PixelPoint>(position);
        Assert.Equal(1920 - (int)Designed.Width, at.X);
        Assert.Equal(1040 - (int)(Designed.Height + Placement.Frame), at.Y);
        Assert.Equal(Designed, size);

        // A second monitor on the left: its coordinates are negative.
        var left = new PixelRect(-1280, 0, 1280, 984);
        var (_, _, onLeft) = Placement.Fit(left, 1, new PixelPoint(-1300, 20), new Size(900, 600), DesignedMin);
        Assert.Equal(new PixelPoint(-1280, 20), onLeft);
    }

    [Fact]
    public void Settings_json_gives_back_only_what_it_can_have_written()
    {
        var empty = Placement.From(null);
        Assert.Equal((null, null, false, ""), (empty.Position, empty.Size, empty.Maximized, empty.Page));

        var kept = Placement.From(new WindowBounds { X = -1200, Y = 40, Width = 1000, Height = 700, Maximized = true });
        Assert.Equal(new PixelPoint(-1200, 40), kept.Position);
        Assert.Equal(new Size(1000, 700), kept.Size);
        Assert.True(kept.Maximized);
        Assert.Equal("", kept.Page); // the page stays within one run

        // Hand-edited or damaged: the size is left out, the window takes its designed one.
        Assert.Null(Placement.From(new WindowBounds { Width = 0, Height = 700 }).Size);
        Assert.Null(Placement.From(new WindowBounds { Width = double.NaN, Height = 700 }).Size);
        Assert.Null(Placement.From(new WindowBounds { Width = 1000 }).Size);
        Assert.Null(Placement.From(new WindowBounds { X = 10 }).Position);

        var bounds = kept.ToBounds();
        Assert.Equal((-1200, 40, 1000.0, 700.0, true), (bounds.X, bounds.Y, bounds.Width, bounds.Height, bounds.Maximized));
    }

    [AvaloniaFact]
    public void The_main_window_opens_where_it_was_after_filee_restarts()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var before = store.Settings.MainWindow;
        var first = ActivatorUtilities.CreateInstance<WindowService>(AppHost.Services);
        var second = ActivatorUtilities.CreateInstance<WindowService>(AppHost.Services); // the next start
        try
        {
            store.Settings.MainWindow = null;
            first.ShowMain();
            var main = first.Main!;
            main.Width = 900;
            main.Height = 640;
            Pump();
            main.Close(); // to the tray: closing while still on screen is when it's remembered
            Pump();

            var saved = Assert.IsType<WindowBounds>(store.Settings.MainWindow);
            Assert.Equal((900.0, 640.0, false), (saved.Width, saved.Height, saved.Maximized));
            var reloaded = new UserDataStore(store.Directory);
            reloaded.Load();
            Assert.Equal(900, reloaded.Settings.MainWindow?.Width);

            second.ShowMain();
            Pump();
            Assert.Equal((900.0, 640.0), (second.Main!.Width, second.Main.Height));
            second.Main.Close();
            Pump();
        }
        finally
        {
            store.Settings.MainWindow = before;
            store.SaveSettings();
        }
    }

    private static void Pump()
    {
        for (var i = 0; i < 10; i++)
            Dispatcher.UIThread.RunJobs();
    }
}
