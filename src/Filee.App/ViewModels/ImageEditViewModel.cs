// "Edit pictures" (#584): the size, format, turn and colour of the dropped pictures, chosen right before they are
// converted instead of in a preset made for each size. The window opens with the tool's last choices; Convert keeps them
// in the tool and starts ordinary conversions (ImageEditTool), so the toast, cancelling and the output rules work as usual.

using CommunityToolkit.Mvvm.ComponentModel;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines.Magick;
using Microsoft.Extensions.Logging;

namespace Filee.App.ViewModels;

public sealed partial class ImageEditViewModel : ObservableObject
{
    private readonly Preset _tool;
    private readonly ILocalizer _loc;
    private readonly UserDataStore _store;
    private readonly Func<string, string, string> _targetFor;
    private readonly Action<IReadOnlyList<string>, Preset> _start;
    private readonly ILogger? _log;
    private readonly (int Width, int Height)? _firstSize;

    /// <param name="pictures">The pictures to convert (other files are left out by the caller).</param>
    /// <param name="tool">The "Edit pictures" tool: its last choices, name and output rule.</param>
    /// <param name="targetFor">The format a picture (path) is written in for the chosen one (<see cref="ImageEditTool.TargetFor"/>).</param>
    /// <param name="start">Starts the conversion of some of the pictures.</param>
    /// <param name="sizeOf">The pixel size of a picture, null when it can't be read; for the size shown in the window.</param>
    public ImageEditViewModel(IReadOnlyList<string> pictures, Preset tool, ILocalizer loc, UserDataStore store,
        Func<string, string, string> targetFor, Action<IReadOnlyList<string>, Preset> start,
        Func<string, (int Width, int Height)?>? sizeOf = null, ILogger? log = null)
    {
        Pictures = pictures;
        _tool = tool;
        _loc = loc;
        _store = store;
        _targetFor = targetFor;
        _start = start;
        _log = log;
        _firstSize = pictures.Count > 0 ? sizeOf?.Invoke(pictures[0]) : null;

        Targets =
        [
            new Choice<string>(BuiltInData.SameAsSource, loc["presets.same_as_source"]),
            .. ImageEditTool.Targets.Select(id => new Choice<string>(id, FormatRegistry.Get(id).DisplayName)),
        ];
        ResizeModes = Enum.GetValues<ResizeMode>().Select(v => new Choice<ResizeMode>(v, loc[$"presets.resize.{v}"])).ToList();
        Rotations = Enum.GetValues<ImageRotation>().Select(v => new Choice<ImageRotation>(v, loc[$"presets.rotation.{v}"])).ToList();
        Flips = Enum.GetValues<ImageFlip>().Select(v => new Choice<ImageFlip>(v, loc[$"presets.flip.{v}"])).ToList();

        var image = tool.Image;
        _target = Targets.FirstOrDefault(t => t.Value == tool.TargetFormat) ?? Targets[0];
        _resize = ResizeModes.FirstOrDefault(r => r.Value == image.Resize) ?? ResizeModes[0];
        _rotation = Rotations.FirstOrDefault(r => r.Value == image.Rotation) ?? Rotations[0];
        _flip = Flips.FirstOrDefault(f => f.Value == image.Flip) ?? Flips[0];
        _resizePercent = image.ResizePercent;
        _longEdge = image.LongEdge;
        _width = image.Width;
        _grayscale = image.Grayscale;
        _quality = image.Quality;
    }

    public IReadOnlyList<string> Pictures { get; }

    public IReadOnlyList<Choice<string>> Targets { get; }
    public IReadOnlyList<Choice<ResizeMode>> ResizeModes { get; }
    public IReadOnlyList<Choice<ImageRotation>> Rotations { get; }
    public IReadOnlyList<Choice<ImageFlip>> Flips { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuality))]
    private Choice<string> _target;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPercent), nameof(ShowLongEdge), nameof(ShowWidth), nameof(SizeChange))]
    private Choice<ResizeMode> _resize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeChange))]
    private Choice<ImageRotation> _rotation;

    [ObservableProperty] private Choice<ImageFlip> _flip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeChange))]
    private int _resizePercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeChange))]
    private int _longEdge;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeChange))]
    private int _width;

    [ObservableProperty] private bool _grayscale;
    [ObservableProperty] private int _quality;

    partial void OnTargetChanged(Choice<string>? oldValue, Choice<string> newValue) => Choices.Restore(oldValue, newValue, v => Target = v);
    partial void OnResizeChanged(Choice<ResizeMode>? oldValue, Choice<ResizeMode> newValue) => Choices.Restore(oldValue, newValue, v => Resize = v);
    partial void OnRotationChanged(Choice<ImageRotation>? oldValue, Choice<ImageRotation> newValue) => Choices.Restore(oldValue, newValue, v => Rotation = v);
    partial void OnFlipChanged(Choice<ImageFlip>? oldValue, Choice<ImageFlip> newValue) => Choices.Restore(oldValue, newValue, v => Flip = v);

    public bool ShowPercent => Resize.Value == ResizeMode.Percent;
    public bool ShowLongEdge => Resize.Value == ResizeMode.LongEdge;
    public bool ShowWidth => Resize.Value == ResizeMode.Width;

    /// <summary>JPG, WEBP, AVIF and HEIC, or the source's own format (which may be one of them).</summary>
    public bool ShowQuality => Target.Value == BuiltInData.SameAsSource || ImageEditTool.LossyTargets.Contains(Target.Value);

    /// <summary>The first picture's name, or how many pictures there are.</summary>
    public string Heading => Pictures.Count == 1 ? Path.GetFileName(Pictures[0]) : _loc.Format("image_edit.count", Pictures.Count);

    /// <summary>"512 × 512 → 32 × 32" for the first picture; empty when its size can't be read.</summary>
    public string SizeChange
    {
        get
        {
            if (_firstSize is not { } size || size.Width <= 0 || size.Height <= 0)
                return "";
            var (width, height) = size;
            var options = Options();
            var scale = ImageEncoder.ResizeScale(width, height, options);
            var turned = options.Rotation is ImageRotation.Left90 or ImageRotation.Right90;
            var (outWidth, outHeight) = (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
            if (turned)
                (outWidth, outHeight) = (outHeight, outWidth);
            return $"{width} × {height} → {outWidth} × {outHeight}";
        }
    }

    /// <summary>The tool's picture options with the window's choices.</summary>
    internal ImageOptions Options()
    {
        var image = _tool.Image.Clone();
        image.Resize = Resize.Value;
        image.ResizePercent = Math.Clamp(ResizePercent, 1, 1000);
        image.LongEdge = Math.Clamp(LongEdge, 16, 20000);
        image.Width = Math.Clamp(Width, 16, 20000);
        image.Rotation = Rotation.Value;
        image.Flip = Flip.Value;
        image.Grayscale = Grayscale;
        image.Quality = Math.Clamp(Quality, 1, 100);
        return image;
    }

    /// <summary>
    /// Keeps the choices in the tool for the next time and starts the conversions: one for each format the pictures are
    /// written in (a camera RAW kept in "its own format" becomes PNG).
    /// </summary>
    public void Convert()
    {
        var image = Options();
        Remember(image);
        foreach (var group in Pictures.GroupBy(path => _targetFor(path, Target.Value)))
            _start([.. group], ImageEditTool.ConversionOf(_tool, group.Key, image));
    }

    private void Remember(ImageOptions image)
    {
        // In the tool as saved, and in the one given to this window (maybe the same).
        ImageEditTool.Remember(_tool, Target.Value, image);
        if (_store.FindPreset(_tool.Id) is not { IsImageEditTool: true } saved)
            return;
        ImageEditTool.Remember(saved, Target.Value, image);
        try
        {
            _store.SaveLibrary();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.LogWarning(ex, "Could not remember the picture edits");
        }
    }
}
