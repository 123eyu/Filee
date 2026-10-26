// "Edit pictures" (#584): the conversion its window starts, the format a picture keeps, the choices kept in the tool, and
// the tool's place in a new library and in one from before settings schema 49.

using System.Text.Json;
using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Core.Profiles;
using Filee.Core.Settings;

namespace Filee.Core.Tests;

public class ImageEditToolTests
{
    private static Preset Tool() => BuiltInData.ImageEditPresets().Single();

    [Fact]
    public void The_tool_is_a_tool_of_its_own_and_survives_saving()
    {
        var tool = Tool();
        Assert.True(tool.IsImageEditTool);
        Assert.False(tool.IsCropTool);
        Assert.False(tool.IsClipTool);

        var json = JsonSerializer.Serialize(new List<Preset> { tool }, FileeJsonContext.Default.ListPreset);
        Assert.Contains("\"tool\": \"image-edit\"", json);
        var read = JsonSerializer.Deserialize(json, FileeJsonContext.Default.ListPreset)!.Single();
        Assert.True(read.IsImageEditTool);
    }

    [Fact]
    public void The_conversion_has_the_chosen_format_and_options_and_leaves_the_tool_as_it_is()
    {
        var tool = Tool();
        tool.Name = "Icons";
        tool.Image.KeepMetadata = false; // set in the preset editor, not in the window
        var image = tool.Image.Clone();
        image.Resize = ResizeMode.LongEdge;
        image.LongEdge = 32;
        image.Rotation = ImageRotation.Right90;
        image.Grayscale = true;

        var conversion = ImageEditTool.ConversionOf(tool, "jpg", image);

        Assert.Equal(PresetKind.Convert, conversion.Kind);
        Assert.Null(conversion.Tool);
        Assert.Equal("jpg", conversion.TargetFormat);
        Assert.Equal((ResizeMode.LongEdge, 32, ImageRotation.Right90, true, false),
            (conversion.Image.Resize, conversion.Image.LongEdge, conversion.Image.Rotation, conversion.Image.Grayscale, conversion.Image.KeepMetadata));
        Assert.Equal(("tool-image-edit", "Icons", "{name}_edited"), (conversion.Id, conversion.Name, conversion.Output.FileNamePattern));
        Assert.NotSame(image, conversion.Image);

        Assert.True(tool.IsImageEditTool);
        Assert.Equal((BuiltInData.SameAsSource, ResizeMode.None), (tool.TargetFormat, tool.Image.Resize));
    }

    [Fact]
    public void The_choices_are_kept_in_the_tool_and_the_rest_of_it_stays()
    {
        var tool = Tool();
        tool.Image.IcoSizes = [32, 16];
        var image = new ImageOptions { Resize = ResizeMode.Width, Width = 640, Flip = ImageFlip.Horizontal, Quality = 70, IcoSizes = [256] };

        ImageEditTool.Remember(tool, "webp", image);

        Assert.Equal("webp", tool.TargetFormat);
        Assert.Equal((ResizeMode.Width, 640, ImageFlip.Horizontal, 70), (tool.Image.Resize, tool.Image.Width, tool.Image.Flip, tool.Image.Quality));
        Assert.Equal([32, 16], tool.Image.IcoSizes);
        Assert.True(tool.IsImageEditTool);
    }

    [Fact]
    public void A_picture_keeps_its_format_where_it_can_be_written_else_becomes_png()
    {
        var magick = new FakeConverter("magick", new ConversionEdge("png", "png"), new ConversionEdge("jpg", "jpg"),
            new ConversionEdge("dcm", "png"), new ConversionEdge("png", "webp"));
        var planner = new ConverterCatalog([magick]).CreatePlanner();

        Assert.Equal(BuiltInData.SameAsSource, ImageEditTool.TargetFor("png", BuiltInData.SameAsSource, planner));
        Assert.Equal(BuiltInData.SameAsSource, ImageEditTool.TargetFor("jpg", BuiltInData.SameAsSource, planner));
        Assert.Equal("png", ImageEditTool.TargetFor("dcm", BuiltInData.SameAsSource, planner)); // DICOM is only read
        Assert.Equal("webp", ImageEditTool.TargetFor("png", "webp", planner));
        Assert.True(ImageEditTool.CanEdit("png"));
        Assert.False(ImageEditTool.CanEdit("mp4"));
        Assert.False(ImageEditTool.CanEdit("pdf"));
    }

    // ───────────────────────── Settings schema 49 ─────────────────────────

    [Fact]
    public void A_new_library_has_the_tool_after_crop_and_on_the_images_donut_after_half_size()
    {
        var presets = BuiltInData.CreatePresets();
        var edit = presets.Single(p => p.Id == "tool-image-edit");
        Assert.Equal("tool-crop", presets[presets.IndexOf(edit) - 1].Id);
        var images = BuiltInData.CreateProfiles().Single(p => p.Id == "images");
        Assert.Equal("tool-image-edit", images.PresetIds[images.PresetIds.IndexOf("half-size") + 1]);
        Assert.DoesNotContain("tool-image-edit", images.BackPresetIds!);
        Assert.InRange(images.PresetIds.Count, 1, ToolbarProfile.MaxSlices);
    }

    [Fact]
    public void Version_48_libraries_get_the_tool_on_the_images_donut()
    {
        var presets = BuiltInData.CreatePresets();
        presets.RemoveAll(p => p.Id == "tool-image-edit");
        var profiles = BuiltInData.CreateProfiles();
        var images = profiles.Single(p => p.Id == "images");
        images.PresetIds.Remove("tool-image-edit");

        Assert.True(SettingsMigrations.ApplyToLibrary(48, presets, profiles));

        Assert.Equal(BuiltInData.CreatePresets().Select(p => p.Id), presets.Select(p => p.Id));
        Assert.True(presets.Single(p => p.Id == "tool-image-edit").IsImageEditTool);
        Assert.Equal(BuiltInData.CreateProfiles().Single(p => p.Id == "images").PresetIds, images.PresetIds);
        Assert.False(SettingsMigrations.ApplyToLibrary(48, presets, profiles)); // once only

        // A front laid out by the user (without "Half size"), a full one, or the tool already on the back, stays as it is.
        var own = BuiltInData.CreateProfiles();
        var ownFront = own.Single(p => p.Id == "images").PresetIds;
        ownFront.Clear();
        ownFront.AddRange(["to-webp", "to-png"]);
        SettingsMigrations.ApplyToLibrary(48, BuiltInData.CreatePresets(), own);
        Assert.Equal(["to-webp", "to-png"], ownFront);

        var full = BuiltInData.CreateProfiles();
        var fullFront = full.Single(p => p.Id == "images").PresetIds;
        fullFront.Remove("tool-image-edit");
        while (fullFront.Count < ToolbarProfile.MaxSlices)
            fullFront.Add("to-gif");
        var before = fullFront.ToList();
        SettingsMigrations.ApplyToLibrary(48, BuiltInData.CreatePresets(), full);
        Assert.Equal(before, fullFront);

        var back = BuiltInData.CreateProfiles();
        var backImages = back.Single(p => p.Id == "images");
        backImages.PresetIds.Remove("tool-image-edit");
        backImages.BackPresetIds![^1] = "tool-image-edit";
        SettingsMigrations.ApplyToLibrary(48, BuiltInData.CreatePresets(), back);
        Assert.DoesNotContain("tool-image-edit", backImages.PresetIds);
    }

    [Fact]
    public void Back_from_an_older_Filee_the_tool_edits_pictures_again_and_keeps_the_users_changes()
    {
        // A Filee before 49 reads the unknown "tool" as nothing (a crop) and saves it so.
        var presets = BuiltInData.CreatePresets();
        var edit = presets.Single(p => p.Id == "tool-image-edit");
        edit.Tool = null;
        edit.Name = "Icons";
        edit.TargetFormat = "ico";
        Assert.True(edit.IsCropTool);

        Assert.True(SettingsMigrations.ApplyToLibrary(48, presets, BuiltInData.CreateProfiles()));

        Assert.True(edit.IsImageEditTool);
        Assert.Equal(("Icons", "ico"), (edit.Name, edit.TargetFormat));
        Assert.Single(presets, p => p.Id == "tool-image-edit");
    }
}
