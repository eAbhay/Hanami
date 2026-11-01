using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public class ModeResolverTests
{
    private static readonly PowerState Plugged = new(false, 80, false);
    private static readonly Coverage Clear = default;

    [Fact]
    public void Defaults_PluggedIn_Uncovered_RunsFull()
    {
        var r = ModeResolver.Resolve(new AppSettings(), Plugged, Clear, sessionLocked: false);
        Assert.Equal(Mode.Full, r.Mode);
        Assert.False(r.Hidden);
    }

    [Fact]
    public void Defaults_OnBattery_Pauses_ButStaysVisible()
    {
        var r = ModeResolver.Resolve(new AppSettings(), new PowerState(true, 70, false), Clear, false);
        Assert.Equal(Mode.Paused, r.Mode);
        Assert.Equal("On battery", r.Reason);
        Assert.False(r.Hidden);   // frozen frame stays on the desktop
    }

    [Fact]
    public void KeepRunningOnBattery_Override()
    {
        var s = new AppSettings();
        s.Power.OnBattery = Mode.Full;
        var r = ModeResolver.Resolve(s, new PowerState(true, 70, false), Clear, false);
        Assert.Equal(Mode.Full, r.Mode);
    }

    [Fact]
    public void LowBattery_UsesLowBatteryRule_AtOrBelowThreshold()
    {
        var s = new AppSettings();
        s.Power.OnBattery = Mode.Full;
        s.Power.LowBattery = Mode.Reduced;
        s.Power.LowBatteryThreshold = 20;
        Assert.Equal(Mode.Reduced, ModeResolver.Resolve(s, new PowerState(true, 20, false), Clear, false).Mode);
        Assert.Equal(Mode.Full, ModeResolver.Resolve(s, new PowerState(true, 21, false), Clear, false).Mode);
    }

    [Fact]
    public void MostRestrictiveRuleWins()
    {
        var s = new AppSettings();
        s.Power.EnergySaver = Mode.Reduced;
        s.Power.WhenMaximized = Mode.Paused;
        var r = ModeResolver.Resolve(s, new PowerState(false, null, true), new Coverage(Maximized: true, Fullscreen: false), false);
        Assert.Equal(Mode.Paused, r.Mode);
        Assert.Equal("Maximized window", r.Reason);
    }

    [Fact]
    public void CoveredAndPaused_StaysVisible()
    {
        var r = ModeResolver.Resolve(new AppSettings(), Plugged, new Coverage(false, true), false);
        Assert.Equal(Mode.Paused, r.Mode);
        Assert.False(r.Hidden);   // hiding would flash the stock wallpaper on Win+D
    }

    [Fact]
    public void IdleFrozen_CapsAtReduced_AndStaysVisible()
    {
        var r = ModeResolver.Resolve(new AppSettings(), Plugged, Clear, false, idleFrozen: true);
        Assert.Equal(Mode.Reduced, r.Mode);
        Assert.Equal("Idle", r.Reason);
        Assert.False(r.Hidden);
    }

    [Fact]
    public void IdleFrozen_DoesNotOverridePaused()
    {
        var r = ModeResolver.Resolve(new AppSettings(), new PowerState(true, 70, false), Clear, false, idleFrozen: true);
        Assert.Equal(Mode.Paused, r.Mode);
        Assert.Equal("On battery", r.Reason);
    }

    [Fact]
    public void ManualPause_Wins_ButNotHiddenWhenVisible()
    {
        var s = new AppSettings { Paused = true };
        var r = ModeResolver.Resolve(s, Plugged, Clear, false);
        Assert.Equal(Mode.Paused, r.Mode);
        Assert.False(r.Hidden);
    }

    [Fact]
    public void Locked_Pauses()
    {
        var r = ModeResolver.Resolve(new AppSettings(), Plugged, Clear, sessionLocked: true);
        Assert.Equal(Mode.Paused, r.Mode);
        Assert.Equal("Screen locked", r.Reason);
    }

    [Fact]
    public void ScreensaverShowing_HidesDesktopWallpaper()
    {
        var s = new AppSettings();
        s.Power.WhenFullscreen = Mode.Full;
        var r = ModeResolver.Resolve(s, Plugged, Clear, false, screensaverShowing: true);
        Assert.Equal(Mode.Paused, r.Mode);
        Assert.True(r.Hidden);
    }

    [Fact]
    public void FpsCap_DependsOnPowerSource()
    {
        var s = new AppSettings();
        Assert.Equal(60, ModeResolver.FpsCap(s, Plugged));
        Assert.Equal(30, ModeResolver.FpsCap(s, new PowerState(true, 50, false)));
    }
}
