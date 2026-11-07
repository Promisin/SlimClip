using System;
using System.Threading.Tasks;
using SlimClip.Core.Helpers;
using SlimClip.Core.Services;
using SlimClip.UI.ViewModels;
using Xunit;

namespace SlimClip.Tests;

public class SetupViewModelTests
{
    private sealed class FakeWindowsSetupService : IWindowsSetupService
    {
        public string? ExistingInstallPath { get; set; }
        public InstallOptions? LastInstallOptions { get; private set; }
        public string? LaunchedInstallDir { get; private set; }

        public string? DetectExistingInstallPath() => ExistingInstallPath;

        public Task InstallAsync(InstallOptions options, IProgress<SetupProgress>? progress = null)
        {
            LastInstallOptions = options;
            progress?.Report(new SetupProgress(50, "Installing..."));
            progress?.Report(new SetupProgress(100, "Done"));
            return Task.CompletedTask;
        }

        public Task UninstallAsync(string installDirectory, IProgress<SetupProgress>? progress = null)
            => Task.CompletedTask;

        public void LaunchInstalledApp(string installDirectory)
        {
            LaunchedInstallDir = installDirectory;
        }
    }

    [Theory]
    [InlineData(@"D:\", @"D:\SlimClip")]
    [InlineData(@"D:", @"D:\SlimClip")]
    [InlineData(@"D:\Apps", @"D:\Apps\SlimClip")]
    [InlineData(@"D:\Apps\", @"D:\Apps\SlimClip")]
    [InlineData(@"D:\Apps\SlimClip", @"D:\Apps\SlimClip")]
    [InlineData(@"D:\Apps\slimclip\", @"D:\Apps\SlimClip")]
    [InlineData(@"C:\Program Files", @"C:\Program Files\SlimClip")]
    [InlineData(@"C:\Program Files\SlimClip", @"C:\Program Files\SlimClip")]
    public void NormalizeInstallPath_AlwaysEnsuresSlimClipFolderSuffix(string input, string expected)
    {
        var actual = InstallerPathHelper.NormalizeInstallPath(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FreshInstall_AllowsPathEdit_ShowsAutoStartAndDesktopOptions_AndNormalizesSelectedPath()
    {
        var fakeService = new FakeWindowsSetupService { ExistingInstallPath = null };
        var vm = new SetupViewModel(fakeService, @"C:\Temp\Source");

        Assert.False(vm.IsUpgradeMode);
        Assert.True(vm.IsPathEditable);
        Assert.True(vm.ShowInstallOptions);
        Assert.True(vm.EnableAutoStart);
        Assert.True(vm.CreateDesktopShortcut);
        Assert.True(vm.IsConfigurationStage);

        // Selecting drive root automatically appends \SlimClip and fills the input property
        vm.ApplySelectedPath(@"D:\");
        Assert.Equal(@"D:\SlimClip", vm.InstallPath);

        // Selecting a custom folder without SlimClip suffix appends \SlimClip
        vm.ApplySelectedPath(@"E:\MyTools");
        Assert.Equal(@"E:\MyTools\SlimClip", vm.InstallPath);
    }

    [Fact]
    public async Task UpgradeMode_LocksPreviousPath_HidesCheckboxes_AndInstallsDirectlyOnConfirm()
    {
        var fakeService = new FakeWindowsSetupService
        {
            ExistingInstallPath = @"D:\CustomApps\SlimClip"
        };
        var vm = new SetupViewModel(fakeService, @"C:\Temp\Source");

        Assert.True(vm.IsUpgradeMode);
        Assert.False(vm.IsPathEditable);
        Assert.False(vm.ShowInstallOptions);
        Assert.Equal(@"D:\CustomApps\SlimClip", vm.InstallPath);

        // Attempting to change path while locked must be ignored
        vm.ApplySelectedPath(@"C:\AnotherPath");
        Assert.Equal(@"D:\CustomApps\SlimClip", vm.InstallPath);

        // Clicking confirm installs directly into the locked path and transitions to Completed
        await vm.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.NotNull(fakeService.LastInstallOptions);
        Assert.True(fakeService.LastInstallOptions!.IsUpgradeMode);
        Assert.Equal(@"D:\CustomApps\SlimClip", fakeService.LastInstallOptions.TargetDirectory);
        Assert.True(vm.IsCompletedStage);

        // Clicking Launch runs the installed app and requests window close
        bool closeRequested = false;
        vm.RequestClose += (_, _) => closeRequested = true;
        vm.LaunchAndExitCommand.Execute(null);

        Assert.Equal(@"D:\CustomApps\SlimClip", fakeService.LaunchedInstallDir);
        Assert.True(closeRequested);
    }
}
