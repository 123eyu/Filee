// The clipboard shortcut (Shift + Cmd + V on macOS, settings schema 55): converts what is on the clipboard and puts the
// result back.

using System.Text.Json.Nodes;
using Filee.Core.Settings;

namespace Filee.Core.Tests;

public class ClipboardShortcutTests
{
    [Fact]
    public void The_clipboard_shortcut_is_shift_cmd_v_everywhere()
    {
        var shortcut = TriggerGesture.ClipboardShortcut();

        Assert.Equal(TriggerKind.KeyChord, shortcut.Kind);
        Assert.Equal(ModifierKeys.Shift | ModifierKeys.Meta, shortcut.Modifiers);
        Assert.Equal("V", shortcut.Key);
        Assert.Equal(TriggerScope.Anywhere, shortcut.Scope);
        Assert.True(shortcut.Clipboard);
        Assert.True(shortcut.Clone().Clipboard);
    }

    [Fact]
    public void Only_macos_gets_it_by_default()
    {
        Assert.Equal(OperatingSystem.IsMacOS(), TriggerGesture.Defaults().Any(g => g.Clipboard));
    }

    [Fact]
    public void An_upgrade_adds_it_once()
    {
        var triggers = TriggerGesture.Defaults().Where(g => !g.Clipboard).ToList();

        Assert.True(SettingsMigrations.AddClipboardShortcut(triggers));
        Assert.False(SettingsMigrations.AddClipboardShortcut(triggers));
        Assert.Single(triggers, g => g.Clipboard);
    }

    [Fact]
    public void A_shortcut_of_the_users_own_on_those_keys_stays_as_it_is()
    {
        var own = new TriggerGesture
        {
            Kind = TriggerKind.KeyChord, Modifiers = ModifierKeys.Shift | ModifierKeys.Meta, Key = "v", Button = TriggerMouseButton.None,
        };
        List<TriggerGesture> triggers = [own];

        Assert.False(SettingsMigrations.AddClipboardShortcut(triggers));
        Assert.Same(own, Assert.Single(triggers));
        Assert.False(own.Clipboard);
    }

    [Fact]
    public void The_option_is_saved_and_read_back()
    {
        using var dir = new TempDir();
        var store = new UserDataStore(dir.Path);
        store.Load();
        store.Settings.Triggers = [TriggerGesture.ClipboardShortcut()];
        store.SaveSettings();

        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(dir.Path, "settings.json")))!;
        Assert.True(json["triggers"]![0]!["clipboard"]!.GetValue<bool>());
        var again = new UserDataStore(dir.Path);
        again.Load();
        Assert.True(Assert.Single(again.Settings.Triggers).Clipboard);
    }
}
