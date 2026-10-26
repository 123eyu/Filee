// The "Edit pictures" window (#584): it opens with the tool's last choices, shows the first picture's size before and
// after, starts one conversion per format the pictures are written in, and keeps the choices in the tool.

using System.IO;
using Avalonia.Headless.XUnit;
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
    [AvaloniaFact]
    public void The_window_shows_the_size_a_picture_gets_and_converts_with_the_choices()
    {
        WithWindow(["icon.png", "scan.dcm"], (window, vm, starts) =>
        {
            Assert.Equal(AppHost.Get<ILocalizer>().Format("image_edit.count", 2), vm.Heading);
            Assert.Equal(BuiltInData.SameAsSource, vm.Target.Value);
            Assert.Equal("512 × 256 → 512 × 256", vm.SizeChange);

            vm.Resize = vm.ResizeModes.Single(r => r.Value == ResizeMode.LongEdge);
            vm.LongEdge = 32;
            Assert.True(vm.ShowLongEdge);
            Assert.Equal("512 × 256 → 32 × 16", vm.SizeChange);
            vm.Rotation = vm.Rotations.Single(r => r.Value == ImageRotation.Right90);
            Assert.Equal("512 × 256 → 16 × 32", vm.SizeChange);
            vm.Grayscale = true;
            Pump();
            Save(window, "window-image-edit-en.png");

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
    public void The_quality_is_offered_for_lossy_formats_only()
    {
        WithWindow(["icon.png"], (_, vm, _) =>
        {
            Assert.Equal("icon.png", vm.Heading);
            vm.Target = vm.Targets.Single(t => t.Value == "jpg");
            Assert.True(vm.ShowQuality);
            vm.Target = vm.Targets.Single(t => t.Value == "ico");
            Assert.False(vm.ShowQuality);
        });
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

    private static void WithWindow(string[] names, Action<ImageEditWindow, ImageEditViewModel, List<(IReadOnlyList<string> Paths, Preset Preset)>> act,
        Preset? tool = null)
    {
        TestServices.EnsureInitialized("en");
        var folder = Directory.CreateTempSubdirectory("filee-image-edit-").FullName;
        ImageEditWindow? window = null;
        try
        {
            var files = names.Select(name => Path.Combine(folder, name)).ToList();
            foreach (var file in files)
                File.WriteAllBytes(file, [0]);
            using (var picture = new MagickImage(MagickColors.SkyBlue, 512, 256))
                picture.Write(files[0], MagickFormat.Png);
            var starts = new List<(IReadOnlyList<string>, Preset)>();
            tool ??= BuiltInData.ImageEditPresets().Single();
            var vm = new ImageEditViewModel(files, tool, AppHost.Get<ILocalizer>(), AppHost.Get<UserDataStore>(),
                (path, chosen) => chosen == BuiltInData.SameAsSource && path.EndsWith(".dcm", StringComparison.Ordinal) ? "png" : chosen,
                (paths, preset) => starts.Add((paths, preset)),
                path =>
                {
                    try
                    {
                        var info = new MagickImageInfo(path);
                        return ((int)info.Width, (int)info.Height);
                    }
                    catch (MagickException)
                    {
                        return null;
                    }
                });
            window = new ImageEditWindow { DataContext = vm };
            window.Show();
            act(window, vm, starts);
        }
        finally
        {
            if (window?.IsVisible == true)
                window.Close();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
