using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Filee.App.Controls;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.Core.Presets;

namespace Filee.App.Views;

/// <summary>
/// The "Pixelate / blur" window: placement, keys (Enter saves, Esc closes, Delete removes the selected frame, Tab and
/// Shift+Tab go from frame to frame, the arrow keys move it (Shift × 10), R clears every frame, P pixelates, B blurs,
/// + and − change the strength, Ctrl+Z / Ctrl+Y undo and redo) and moving, sizing and maximizing the frameless window,
/// as the crop overlay's.
/// </summary>
public partial class HideWindow : Window
{
    private HideWindowViewModel? _vm;

    /// <summary>The focus came from the keyboard (Tab, arrows), not from a click.</summary>
    private bool _focusByKeyboard;

    public HideWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        Canvas.PlayAnimations = Motion.Enabled;
        Motion.Changed += OnMotionChanged;
        DataContextChanged += OnDataContextChanged;
        AddHandler(GotFocusEvent, (_, e) => _focusByKeyboard = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional,
            RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnKeyDownFirst, RoutingStrategies.Tunnel);
        // Enter is the window's own (save), so a button that was clicked can't be pressed by it later; nor by its key-up.
        AddHandler(KeyUpEvent, (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space && !ControlTakes(e))
                e.Handled = true;
        }, RoutingStrategies.Tunnel);
        ResizeGrip.PointerPressed += OnGripPressed;
        Stage.SizeChanged += (_, _) => FitToolbar();
        Closed += (_, _) => Motion.Changed -= OnMotionChanged;
    }

    /// <summary>The toolbar's width with its buttons' names, measured when it last had to give them up.</summary>
    private double _namedWidth;

    /// <summary>
    /// Shows the toolbar's buttons with their icons only (<c>compact</c>) when their names don't fit the window, and with
    /// the names again once there is room.
    /// </summary>
    private void FitToolbar()
    {
        var available = Stage.Bounds.Width - 32; // the pill's margin
        if (available <= 0)
            return;
        if (!Classes.Contains("compact"))
        {
            ToolbarContent.Measure(Size.Infinity);
            var wanted = ToolbarContent.DesiredSize.Width + Toolbar.Padding.Left + Toolbar.Padding.Right + 2;
            if (wanted > available)
            {
                _namedWidth = wanted;
                Classes.Set("compact", true);
            }
        }
        else if (available >= _namedWidth)
        {
            Classes.Set("compact", false);
        }
    }

    /// <summary>The names changed (language, "Hide & Next" → "Hide & Save", Skip shown): measured again with them.</summary>
    private void RefitToolbar()
    {
        Classes.Set("compact", false);
        Dispatcher.UIThread.Post(FitToolbar, DispatcherPriority.Render);
    }

    internal HideCanvas HideCanvas => Canvas;

    /// <summary>
    /// Opens near <paramref name="near"/> (the screen of the donut), else on the primary screen, centred in its working
    /// area and no larger than most of it.
    /// </summary>
    public void PositionOnScreen(PixelPoint? near = null)
    {
        var screen = (near.HasValue ? Screens.ScreenFromPoint(near.Value) : null)
            ?? Screens.ScreenFromPoint(Screens.Primary?.Bounds.Center ?? default)
            ?? Screens.Primary;
        if (screen is null)
            return;
        var scaling = screen.Scaling;
        var area = screen.WorkingArea;
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width / scaling * 0.94));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height / scaling * 0.92));
        var x = area.X + Math.Max(0, (area.Width - (int)(Width * scaling)) / 2);
        var y = area.Y + Math.Max(0, (area.Height - (int)(Height * scaling)) / 2);
        Position = new PixelPoint(x, y);
    }

    /// <summary>Settles every animation and transition (tests capture the end state).</summary>
    internal void SettleForCapture()
    {
        Classes.Set("reduced-motion", true);
        Canvas.PlayAnimations = false;
        Canvas.FinishAnimations();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is HideWindowViewModel vm && !ReferenceEquals(vm, _vm))
        {
            _vm = vm;
            vm.CloseRequested += Close;
            vm.PropertyChanged += OnViewModelChanged;
            Closed += (_, _) => vm.Dispose();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null)
            return;
        if (e.PropertyName == nameof(HideWindowViewModel.CurrentIndex))
            Dispatcher.UIThread.Post(() => Strip.ContainerFromIndex(_vm.CurrentIndex)?.BringIntoView(), DispatcherPriority.Background);
        else if (e.PropertyName is nameof(HideWindowViewModel.SaveText) or nameof(HideWindowViewModel.HasMultipleFiles))
            RefitToolbar();
    }

    private void OnMotionChanged(object? sender, EventArgs e) => Canvas.PlayAnimations = Motion.Enabled;

    // ───────────────────────── Keys ─────────────────────────

    /// <summary>
    /// The control that has the focus, reached with the keyboard, takes <paramref name="e"/>'s key as it would anywhere
    /// else: Space and Enter press a button, the arrows move the strength slider.
    /// </summary>
    private bool ControlTakes(KeyEventArgs e)
    {
        if (!_focusByKeyboard || e.KeyModifiers != KeyModifiers.None)
            return false;
        return FocusManager?.GetFocusedElement() switch
        {
            Button => e.Key is Key.Space or Key.Enter,
            Slider => e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown,
            _ => false,
        };
    }

    /// <summary>Before any focused control: the window's keys, unless a control reached with the keyboard takes it.</summary>
    private void OnKeyDownFirst(object? sender, KeyEventArgs e)
    {
        if (_vm is not { } vm || ControlTakes(e))
            return;
        var plain = e.KeyModifiers == KeyModifiers.None;
        var command = e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta;
        var commandShift = e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) || e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Shift);
        var editing = vm.IsEditing && !vm.IsBusy;
        switch (e.Key)
        {
            case Key.Escape:
                vm.Cancel();
                e.Handled = true;
                break;
            case Key.Enter:
                if (vm.IsFinished)
                    vm.Cancel();
                else
                    _ = vm.SaveAsync();
                e.Handled = true;
                break;
            case Key.Delete or Key.Back when plain && editing:
                vm.RemoveSelected();
                e.Handled = true;
                break;
            // Tab goes from frame to frame while there are frames; without, it moves the focus as usual.
            case Key.Tab when editing && vm.HasAreas && (plain || e.KeyModifiers == KeyModifiers.Shift):
                vm.Areas.SelectNext(e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
                e.Handled = true;
                break;
            case Key.Z when command && editing:
                vm.Undo();
                e.Handled = true;
                break;
            case Key.Y when command && editing:
            case Key.Z when commandShift && editing:
                vm.Redo();
                e.Handled = true;
                break;
            case Key.R when plain && editing:
                vm.Clear();
                e.Handled = true;
                break;
            case Key.P when plain && editing:
                vm.SetStyle(HideStyle.Pixelate);
                e.Handled = true;
                break;
            case Key.B when plain && editing:
                vm.SetStyle(HideStyle.Blur);
                e.Handled = true;
                break;
            case Key.OemPlus or Key.Add when (plain || e.KeyModifiers == KeyModifiers.Shift) && editing:
                vm.StepStrength(1);
                e.Handled = true;
                break;
            case Key.OemMinus or Key.Subtract when plain && editing:
                vm.StepStrength(-1);
                e.Handled = true;
                break;
            // The picture is never mirrored, also in right-to-left languages: Left is always left.
            case Key.Left or Key.Right or Key.Up or Key.Down when editing && vm.HasSelection
                && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None:
                var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
                vm.Areas.Nudge(e.Key switch { Key.Left => -step, Key.Right => step, _ => 0 },
                    e.Key switch { Key.Up => -step, Key.Down => step, _ => 0 }, vm.OriginalWidth, vm.OriginalHeight);
                e.Handled = true;
                break;
        }
    }

    // ───────────────────────── Moving and sizing the window ─────────────────────────

    /// <summary>The stage beside the picture moves the window; a double click maximizes it.</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.ClickCount == 2)
            ToggleMaximized();
        else if (WindowState == WindowState.Normal)
            BeginMoveDrag(e);
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Normal && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginResizeDrag(WindowEdge.SouthEast, e);
        e.Handled = true;
    }

    private void OnMaximize(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            var maximized = WindowState == WindowState.Maximized;
            Classes.Set("maximized", maximized);
            ResizeGrip.IsVisible = !maximized;
            var key = maximized ? "crop.restore" : "crop.maximize";
            MaximizeButton.Bind(ToolTip.TipProperty, MaximizeButton.GetResourceObservable(key));
            MaximizeButton.Bind(Avalonia.Automation.AutomationProperties.NameProperty, MaximizeButton.GetResourceObservable(key));
        }
    }
}
