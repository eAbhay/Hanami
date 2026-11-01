using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public class IdleFreezePolicyTests
{
    private static ScreensaverSettings Saver(int minutes, bool enabled = true) =>
        new() { Enabled = enabled, IdleMinutes = minutes };

    [Fact]
    public void PluggedIn_FreezesFiveSecondsBeforeScreensaver()
    {
        Assert.Equal(TimeSpan.FromSeconds(55), IdleFreezePolicy.FreezeAfter(Saver(1), false, null));
    }

    [Fact]
    public void PluggedIn_EarlierWindowsTimeoutWins()
    {
        Assert.Equal(TimeSpan.FromSeconds(55), IdleFreezePolicy.FreezeAfter(Saver(5), false, 1));
        Assert.Equal(TimeSpan.FromMinutes(5) - IdleFreezePolicy.Lead, IdleFreezePolicy.FreezeAfter(Saver(5), false, 10));
    }

    [Fact]
    public void OnBattery_UsesOnlyTheWindowsTimeout()
    {
        Assert.Equal(TimeSpan.FromMinutes(2) - IdleFreezePolicy.Lead, IdleFreezePolicy.FreezeAfter(Saver(1), true, 2));
        Assert.Null(IdleFreezePolicy.FreezeAfter(Saver(1), true, null));
    }

    [Fact]
    public void DisabledScreensaver_FallsBackToWindowsTimeout_OrNever()
    {
        Assert.Equal(TimeSpan.FromMinutes(3) - IdleFreezePolicy.Lead, IdleFreezePolicy.FreezeAfter(Saver(1, enabled: false), false, 3));
        Assert.Null(IdleFreezePolicy.FreezeAfter(Saver(1, enabled: false), false, null));
    }

    [Fact]
    public void NeverNegative()
    {
        Assert.Equal(TimeSpan.Zero, IdleFreezePolicy.FreezeAfter(Saver(0), false, null));
        Assert.Equal(TimeSpan.Zero, IdleFreezePolicy.FreezeAfter(Saver(5), false, 0.05));
    }
}
