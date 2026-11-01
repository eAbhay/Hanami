using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public class ScreensaverPolicyTests
{
    private static ScreensaverInputs Idle(double minutes, bool battery = false, bool locked = false,
        bool fullscreen = false, bool otherApp = false) =>
        new(battery, locked, fullscreen, otherApp, TimeSpan.FromMinutes(minutes));

    [Fact]
    public void Defaults_StartAfterTwoMinutesIdle_PluggedIn()
    {
        var s = new ScreensaverSettings();
        Assert.Equal(2, s.IdleMinutes);
        Assert.False(s.RequireSignIn);
        Assert.False(ScreensaverPolicy.Evaluate(s, Idle(1.9)).Start);
        Assert.Equal("Starts after 2 min idle", ScreensaverPolicy.Evaluate(s, Idle(1.9)).Status);
        Assert.True(ScreensaverPolicy.Evaluate(s, Idle(2)).Start);
    }

    [Fact]
    public void NeverOnBattery()
    {
        var (start, status) = ScreensaverPolicy.Evaluate(new ScreensaverSettings(), Idle(60, battery: true));
        Assert.False(start);
        Assert.StartsWith("Off on battery", status);
    }

    [Theory]
    [InlineData(true, false, false, "Windows is locked")]
    [InlineData(false, true, false, "fullscreen app")]
    [InlineData(false, false, true, "another app is keeping the screen on")]
    public void WaitsWhenSomethingElseOwnsTheScreen(bool locked, bool fullscreen, bool otherApp, string expected)
    {
        var (start, status) = ScreensaverPolicy.Evaluate(new ScreensaverSettings(), Idle(60, false, locked, fullscreen, otherApp));
        Assert.False(start);
        Assert.Contains(expected, status);
    }

    [Fact]
    public void Disabled_NeverStarts()
    {
        Assert.False(ScreensaverPolicy.Evaluate(new ScreensaverSettings { Enabled = false }, Idle(60)).Start);
    }

    [Fact]
    public void EndsOnUnplugLockAndStopAfterLimit()
    {
        var s = new ScreensaverSettings { StopAfterHours = 2 };
        Assert.Null(ScreensaverPolicy.EndReason(s, onBattery: false, sessionLocked: false, TimeSpan.FromHours(1), preview: false));
        Assert.Equal("Unplugged", ScreensaverPolicy.EndReason(s, true, false, TimeSpan.Zero, false));
        Assert.Equal("Windows locked", ScreensaverPolicy.EndReason(s, false, true, TimeSpan.Zero, false));
        Assert.NotNull(ScreensaverPolicy.EndReason(s, false, false, TimeSpan.FromHours(2), false));
        // 0 = run until input
        Assert.Null(ScreensaverPolicy.EndReason(new ScreensaverSettings(), false, false, TimeSpan.FromHours(100), false));
    }

    [Fact]
    public void Preview_IgnoresBatteryAndLimits_ButNotLock()
    {
        var s = new ScreensaverSettings { Enabled = false, StopAfterHours = 1 };
        Assert.Null(ScreensaverPolicy.EndReason(s, true, false, TimeSpan.FromHours(5), preview: true));
        Assert.NotNull(ScreensaverPolicy.EndReason(s, false, true, TimeSpan.Zero, preview: true));
    }

    [Fact]
    public void Settings_ClampIdleMinutes()
    {
        var s = new AppSettings();
        s.Screensaver.IdleMinutes = 0;
        Assert.Equal(1, SettingsStore.Normalize(s).Screensaver.IdleMinutes);
    }
}
