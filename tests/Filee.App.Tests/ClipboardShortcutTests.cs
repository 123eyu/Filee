// The clipboard switch of a keyboard shortcut on the Shortcuts page, and Shift + Cmd + V reaching the detector.

using Filee.App.Services.Triggers;
using Filee.App.ViewModels.Pages;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public class ClipboardShortcutTests
{
    [Fact]
    public void Only_a_keyboard_shortcut_where_the_clipboard_can_be_read_offers_the_switch()
    {
        var shortcut = new TriggerGestureViewModel(TriggerGesture.ClipboardShortcut(), new KeyLocalizer(), () => { }, clipboardAvailable: true);
        Assert.True(shortcut.UsesClipboard);
        Assert.True(shortcut.Clipboard);

        var drag = new TriggerGestureViewModel(new TriggerGesture { Kind = TriggerKind.Drag }, new KeyLocalizer(), () => { }, clipboardAvailable: true);
        Assert.False(drag.UsesClipboard);

        var elsewhere = new TriggerGestureViewModel(TriggerGesture.ClipboardShortcut(), new KeyLocalizer(), () => { });
        Assert.False(elsewhere.UsesClipboard);
    }

    [Fact]
    public void Switching_it_saves_the_shortcut()
    {
        var gesture = new TriggerGesture { Kind = TriggerKind.KeyChord, Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt, Key = "Space" };
        var saves = 0;
        var vm = new TriggerGestureViewModel(gesture, new KeyLocalizer(), () => saves++, clipboardAvailable: true);

        vm.Clipboard = true;

        Assert.True(gesture.Clipboard);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void Shift_cmd_v_fires_the_clipboard_shortcut()
    {
        var detector = new GestureDetector();
        detector.Configure([TriggerGesture.ClipboardShortcut()]);
        TriggerGesture? fired = null;
        detector.KeyChordTriggered += (g, _, _) => fired = g;

        detector.KeyDown("V", ModifierKeys.Meta);
        Assert.Null(fired);
        detector.KeyDown("V", ModifierKeys.Shift | ModifierKeys.Meta);

        Assert.NotNull(fired);
        Assert.True(fired.Clipboard);
    }
}
