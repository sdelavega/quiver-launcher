using System.Diagnostics;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class MacWindowsRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-mac-runner", Guid.NewGuid().ToString("N"));
    private string GamePath => Path.Combine(_root, "game");

    public MacWindowsRunnerTests() => Directory.CreateDirectory(GamePath);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Mac_offers_the_windows_build_only_as_a_runner_fallback()
    {
        var windowsOnly = DownloadAssetPolicyTests.Release("game-windows.zip", "game-linux.AppImage");
        DownloadAssetPolicy.Select(windowsOnly, "macOS").Eligible.Should().BeEmpty();
        DownloadAssetPolicy.Select(windowsOnly, "macOS", windowsBuildFallback: true).Eligible
            .Select(a => a.name).Should().Equal("game-windows.zip");

        var both = DownloadAssetPolicyTests.Release("game-windows.zip", "game-macos.zip");
        DownloadAssetPolicy.Select(both, "macOS", windowsBuildFallback: true).Eligible
            .Select(a => a.name).Should().Equal(["game-macos.zip"], "a Mac build never needs a runner");
    }

    [Fact]
    public void Linux_keeps_offering_windows_builds_after_native_ones()
    {
        var both = DownloadAssetPolicyTests.Release("game-windows.zip", "game-linux.AppImage");
        DownloadAssetPolicy.Select(both, "Linux-X64").Eligible
            .Select(a => a.name).Should().Equal("game-linux.AppImage", "game-windows.zip");
    }

    [Fact]
    public async Task Windows_exe_launches_through_the_runner_on_macos()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Skip("Requires macOS.");

        var runner = Path.Combine(_root, "fake-wine");
        File.WriteAllText(runner, "#!/bin/sh\nprintf '%s\\n' \"$PWD\" \"$@\" > \"$(dirname \"$0\")/ran.txt\"\n");
        File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var exe = Path.Combine(GamePath, "Game.exe");
        File.WriteAllBytes(exe, [0x4d, 0x5a]);
        var game = new GameInfo
        {
            Name = "Windows Game", FolderName = "game", Status = GameStatus.Installed,
            LinuxRunner = "custom", LinuxCustomLaunchCommand = $"'{runner}' {{exe}}",
        };
        Process? launched = null;
        game.GameProcessStarted += process => launched = process;

        var result = await GameLaunchService.LaunchAsync(game, _root);

        result.Should().BeTrue();
        using (launched)
        {
            await launched!.WaitForExitAsync(TestContext.Current.CancellationToken);
            launched.ExitCode.Should().Be(0);
        }
        File.ReadAllLines(Path.Combine(_root, "ran.txt")).Should().Equal(GamePath, exe);

        var target = await GameShortcutLaunch.PrepareAsync(game, _root, new());
        target!.FileName.Should().Be(runner, "shortcuts start the game through the same runner");
        target.Arguments.Should().Equal(exe);
    }
}
