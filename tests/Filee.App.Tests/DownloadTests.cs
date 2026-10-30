using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Filee.App.Services;
using Filee.Core.Conversion;
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Core.Web;

namespace Filee.App.Tests;

public class DownloadTests
{
    [AvaloniaFact]
    public async Task Download_policy_cancel_and_exact_bytes_are_independent_of_conversion_settings()
    {
        TestServices.EnsureInitialized("en");
        var store = AppHost.Get<UserDataStore>();
        var service = AppHost.Get<ConversionService>();
        var previous = store.Settings.BrowserDrops;
        var folder = Path.Combine(Path.GetTempPath(), "filee-download-test-" + Guid.NewGuid().ToString("N"));
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var preset = new Preset { Id = "browser-download", Kind = PresetKind.Download, TargetFormat = "download" };
        WebDrop Drop() => new([WebDropItem.FromBytes("same.png", bytes)], WebDrop.DefaultWorkRoot);
        try
        {
            store.Settings.BrowserDrops = new BrowserDropSettings { Folder = folder + "-unused", SaveOnlyConverted = true };
            Assert.False(service.DownloadNeedsPicker);
            Assert.Equal(AppHost.Get<IPlatformServices>().DownloadsFolder, service.DownloadFolderInUse);
            store.Settings.BrowserDrops.DownloadLocation = DownloadLocation.Custom;
            Assert.True(service.DownloadNeedsPicker);
            store.Settings.BrowserDrops.DownloadFolder = folder;
            Assert.False(service.DownloadNeedsPicker);
            Assert.Equal(folder, service.DownloadFolderInUse);
            store.Settings.BrowserDrops.DownloadLocation = DownloadLocation.Ask;
            var before = service.Jobs.Count;
            using (var cancel = Drop())
            {
                Assert.Null(await service.ChooseDownloadAsync(cancel.Placeholders, preset, cancel, _ => Task.FromResult<string?>(null)));
                Assert.False(Directory.Exists(cancel.WorkFolder));
            }
            Assert.Equal(before, service.Jobs.Count);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var drop = Drop();
                var vm = await service.ChooseDownloadAsync(drop.Placeholders, preset, drop, _ => Task.FromResult<string?>(folder));
                Assert.NotNull(vm);
                var until = DateTime.UtcNow.AddSeconds(30);
                while (vm.Job.State is JobState.Queued or JobState.Running && DateTime.UtcNow < until)
                { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
                Assert.Equal(JobState.Completed, vm.Job.State);
                var output = Assert.Single(vm.Job.Outputs);
                Assert.Equal(folder, Path.GetDirectoryName(output));
                Assert.Equal(bytes, File.ReadAllBytes(output));
            }
            Assert.Equal(2, Directory.GetFiles(folder).Length);
            Assert.False(Directory.Exists(folder + "-unused"));
        }
        finally
        {
            store.Settings.BrowserDrops = previous;
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
