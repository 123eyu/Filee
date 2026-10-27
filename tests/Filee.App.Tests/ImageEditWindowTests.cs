// The "Edit pictures" window (#584): it opens with the tool's last choices, shows the first picture's size before and
// after, starts one conversion per format the pictures are written in, and keeps the choices in the tool. Enter
// converts and Escape closes; screenshots in light and dark, short and long languages, right to left.

using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using ImageMagick;
using static Filee.App.Tests.RenderTests;

namespace Filee.App.Tests;

public class ImageEditWindowTests
{
    public static TheoryData<string, bool> Looks => new()
    {
        { "en", false }, { "en", true }, { "ko", false }, { "ko", true }, { "ar", false }, { "ar", true },
        // Long names of the resizes and flips: "Ограничить длинную сторону", "लामो छेउ सीमित गर्नुहोस्", "แนวนอน (ซ้าย-ขวา)".
        { "de", false }, { "ru", false }, { "ru", true }, { "ne", false }, { "hi", true }, { "th", false },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public void The_window_renders(string language, bool dark)
    {
        WithWindow(["holiday-photo-from-the-harbour.jpg", "b.png", "c.png"], (window, vm, _) =>
        {
            vm.Resize = vm.ResizeModes.Single(r => r.Value == ResizeMode.LongEdge);
            vm.LongEdge = 128;
            vm.Rotation = vm.Rotations.Single(r => r.Value == ImageRotation.Right90);
            Pump();
            Assert.True(vm.ShowQuality); // the JPG keeps its format
            Save(window, $"window-image-edit-{language}-{(dark ? "dark" : "light")}.png");
        }, language: language, dark: dark);
    }

    [AvaloniaFact]
    public void The_window_shows_the_size_a_picture_gets_and_converts_with_the_choices()
    {
        WithWindow(["icon.png", "scan.dcm"], (_, vm, starts) =>
        {
            Assert.Equal(AppHost.Get<ILocalizer>().Format("image_edit.count", 2), vm.Heading);
            Assert.Equal(BuiltInData.SameAsSource, vm.Target.Value);
            Assert.True(vm.ShowSize);
            Assert.Equal(("512 × 256", "512 × 256"), (vm.SizeBefore, vm.SizeAfter));

            vm.Resize = vm.ResizeModes.Single(r => r.Value == ResizeMode.LongEdge);
            vm.LongEdge = 32;
            Assert.True(vm.ShowLongEdge);
            Assert.Equal("32 × 16", vm.SizeAfter);
            vm.Rotation = vm.Rotations.Single(r => r.Value == ImageRotation.Right90);
            Assert.Equal("16 × 32", vm.SizeAfter);
            vm.Grayscale = true;

            vm.Convert();

            // The PNG stays a PNG; the DICOM, which can't be written, becomes one.
            Assert.Equal(2, starts.Count);
            var same = starts.Single(s => s.Preset.TargetFormat == BuiltInData.SameAsSource);
            Assert.Equal(["icon.png"], same.Paths.Select(Path.GetFileName));
            Assert.Equal(["scan.dcm"], starts.Single(s => s.Preset.TargetFormat == "png").Paths.Select(Path.GetFileName));
            Assert.All(starts, s =>
            {
                Assert.Equal(PresetKind.Convert, s.Preset.Kind);
                Assert.Equal((ResizeMode.LongEdge, 32, ImageRotation.Right90, true),
                    (s.Preset.Image.Resize, s.Preset.Image.LongEdge, s.Preset.Image.Rotation, s.Preset.Image.Grayscale));
            });
        });
    }

    [AvaloniaFact]
    public void The_size_after_is_worked_out_as_the_encoder_does()
    {
        WithWindow(["icon.png"], (_, vm, _) =>
        {
            // Turned first, then resized: 512 × 256 turned is 256 × 512, and 128 wide is 128 × 256 (not 64 × 128).
            vm.Rotation = vm.Rotations.Single(r => r.Value == ImageRotation.Left90);
            vm.Resize = vm.ResizeModes.Single(r => r.Value == ResizeMode.Width);
            vm.Width = 128;
            Assert.Equal("128 × 256", vm.SizeAfter);

            // An icon is square and at most 256 px.
            vm.Rotation = vm.Rotations.Single(r => r.Value == ImageRotation.None);
            vm.Target = vm.Targets.Single(t => t.Value == "ico");
            vm.Resize = vm.ResizeModes.Single(r => r.Value == ResizeMode.LongEdge);
            vm.LongEdge = 32;
            Assert.Equal("32 × 32", vm.SizeAfter);
            vm.Resize = vm.ResizeModes.Single(r => r.Value == ResizeMode.None);
            Assert.Equal("256 × 256", vm.SizeAfter);
        });
    }

    [AvaloniaFact]
    public void The_window_offers_the_resizes_it_has_fields_for()
    {
        // A resize mode added later (#593's free width × height) needs its fields here before it is offered.
        WithWindow(["icon.png"], (_, vm, _) =>
            Assert.Equal([ResizeMode.None, ResizeMode.Percent, ResizeMode.LongEdge, ResizeMode.Width], vm.ResizeModes.Select(r => r.Value)));
    }

    [AvaloniaFact]
    public void The_quality_is_offered_where_a_picture_is_written_lossy()
    {
        WithWindow(["icon.png"], (_, vm, _) =>
        {
            Assert.Equal("icon.png", vm.Heading);
            Assert.Equal(BuiltInData.SameAsSource, vm.Target.Value);
            Assert.False(vm.ShowQuality); // a PNG kept a PNG has no quality
            vm.Target = vm.Targets.Single(t => t.Value == "jpg");
            Assert.True(vm.ShowQuality);
            vm.Target = vm.Targets.Single(t => t.Value == "ico");
            Assert.False(vm.ShowQuality);
        });
        WithWindow(["icon.png", "photo.jpg"], (_, vm, _) => Assert.True(vm.ShowQuality));
    }

    [AvaloniaFact]
    public void Enter_converts_from_a_list_and_escape_closes()
    {
        WithWindow(["icon.png"], (window, _, starts) =>
        {
            // The format list has the focus when the window opens; Enter there converts instead of opening it.
            Assert.IsType<ComboBox>(window.FocusManager?.GetFocusedElement());
            Press(window, Key.Enter);
            Assert.Single(starts);
            Assert.False(window.IsVisible);
        });
        WithWindow(["icon.png"], (window, _, starts) =>
        {
            var grayscale = window.GetVisualDescendants().OfType<CheckBox>().Single();
            grayscale.Focus();
            Press(window, Key.Enter);
            Assert.Single(starts);
            Assert.False(grayscale.IsChecked);
        });
        WithWindow(["icon.png"], (window, _, starts) =>
        {
            Press(window, Key.Escape);
            Assert.Empty(starts);
            Assert.False(window.IsVisible);
        });
    }

    [AvaloniaFact]
    public void Arabic_keeps_each_size_left_to_right_and_points_the_arrow_its_way()
    {
        WithWindow(["icon.png"], (window, vm, _) =>
        {
            vm.Resize = vm.ResizeModes.Single(r => r.Value == ResizeMode.LongEdge);
            vm.LongEdge = 32;
            Pump();
            var line = window.FindControl<StackPanel>("SizeChange")!;
            var texts = line.Children.OfType<TextBlock>().ToList();
            Assert.Equal(["512 × 256", "←", "32 × 16"], texts.Select(t => t.Text));
            Assert.Equal(Avalonia.Media.FlowDirection.RightToLeft, line.FlowDirection);
            Assert.All([texts[0], texts[2]], t => Assert.Equal(Avalonia.Media.FlowDirection.LeftToRight, t.FlowDirection));
        }, language: "ar");
    }

    [AvaloniaFact]
    public void The_choices_are_remembered_in_the_tool()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var saved = store.FindPreset("tool-image-edit")!;
        var before = saved.Clone();
        try
        {
            WithWindow(["icon.png"], (_, vm, _) =>
            {
                vm.Target = vm.Targets.Single(t => t.Value == "webp");
                vm.Resize = vm.ResizeModes.Single(r => r.Value == ResizeMode.Width);
                vm.Width = 640;
                vm.Convert();
            }, saved);
            Assert.Equal(("webp", ResizeMode.Width, 640), (saved.TargetFormat, saved.Image.Resize, saved.Image.Width));

            // Opened again: as left.
            WithWindow(["icon.png"], (_, vm, _) =>
            {
                Assert.Equal("webp", vm.Target.Value);
                Assert.Equal((ResizeMode.Width, 640), (vm.Resize.Value, vm.Width));
                Assert.True(vm.ShowWidth);
            }, saved);
        }
        finally
        {
            saved.TargetFormat = before.TargetFormat;
            saved.Image = before.Image;
            store.SaveLibrary();
        }
    }

    [AvaloniaFact]
    public void The_size_read_is_the_one_seen_and_an_icons_largest()
    {
        var folder = Directory.CreateTempSubdirectory("filee-image-edit-size-").FullName;
        try
        {
            // A phone photo stored on its side, upright by its EXIF orientation.
            var photo = Path.Combine(folder, "phone.jpg");
            using (var image = new MagickImage(MagickColors.SkyBlue, 400, 300))
            {
                var exif = new ExifProfile();
                exif.SetValue(ExifTag.Orientation, (ushort)6); // "turn right to view"
                image.SetProfile(exif);
                image.Orientation = OrientationType.RightTop;
                image.Write(photo, MagickFormat.Jpg);
            }
            Assert.Equal((300, 400), WindowService.PictureSize(photo));

            var icon = Path.Combine(folder, "app.ico");
            using (var image = new MagickImage(MagickColors.Coral, 64, 64))
            {
                image.Settings.SetDefine(MagickFormat.Icon, "auto-resize", "16,64,32");
                image.Write(icon, MagickFormat.Icon);
            }
            Assert.Equal((64, 64), WindowService.PictureSize(icon));

            File.WriteAllBytes(Path.Combine(folder, "broken.png"), [0]);
            Assert.Null(WindowService.PictureSize(Path.Combine(folder, "broken.png")));
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    private static void WithWindow(string[] names, Action<ImageEditWindow, ImageEditViewModel, List<(IReadOnlyList<string> Paths, Preset Preset)>> act,
        Preset? tool = null, string language = "en", bool dark = false)
    {
        TestServices.EnsureInitialized(language);
        var theme = AppHost.Get<ThemeService>();
        if (dark)
            theme.Apply(new ThemeSettings { Mode = ThemeMode.Dark }, language);
        var folder = Directory.CreateTempSubdirectory("filee-image-edit-").FullName;
        ImageEditWindow? window = null;
        try
        {
            var files = names.Select(name => Path.Combine(folder, name)).ToList();
            foreach (var file in files)
                File.WriteAllBytes(file, [0]);
            foreach (var picture in files.Where(f => Path.GetExtension(f) is ".png" or ".jpg").Take(2))
            {
                using var image = new MagickImage(MagickColors.SkyBlue, 512, 256);
                image.Write(picture);
            }
            var starts = new List<(IReadOnlyList<string>, Preset)>();
            tool ??= BuiltInData.ImageEditPresets().Single();
            var vm = new ImageEditViewModel(files, tool, AppHost.Get<ILocalizer>(), AppHost.Get<UserDataStore>(),
                (path, chosen) => chosen == BuiltInData.SameAsSource && path.EndsWith(".dcm", StringComparison.Ordinal) ? "png" : chosen,
                (paths, preset) => starts.Add((paths, preset)), WindowService.PictureSize);
            window = new ImageEditWindow { DataContext = vm };
            window.Show();
            Pump();
            act(window, vm, starts);
        }
        finally
        {
            if (window?.IsVisible == true)
                window.Close();
            if (dark)
                theme.Apply(new ThemeSettings(), language);
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    private static void Press(ImageEditWindow window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }
}
