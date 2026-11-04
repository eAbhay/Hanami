using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public class UpdateAttentionTests
{
    [Theory]
    [InlineData("idle")]
    [InlineData("upToDate")]
    [InlineData("available")]
    [InlineData("failed")]   // a failed check with nothing offered
    public void Nothing_offered_shows_nothing(string status) =>
        Assert.Null(UpdateAttention.For(status, null, 0));

    [Theory]
    [InlineData("idle")]
    [InlineData("upToDate")]
    public void A_status_without_an_offer_shows_nothing_even_with_a_stale_version(string status) =>
        Assert.Null(UpdateAttention.For(status, "0.1.1", 0));

    [Theory]
    [InlineData("available")]
    [InlineData("checking")]
    [InlineData("downloading")]
    [InlineData("failed")]
    public void The_prompt_stays_through_every_status_of_an_offered_version(string status) =>
        Assert.NotNull(UpdateAttention.For(status, "0.1.1", 0.5));

    [Fact]
    public void Available_offers_the_one_click_install()
    {
        var p = UpdateAttention.For("available", "0.1.1", 0)!;
        Assert.Equal("Update available: install 0.1.1", p.MenuText);
        Assert.True(p.CanInstall);
        Assert.Equal("Update 0.1.1 available", p.TipLine);
    }

    [Fact]
    public void A_click_is_only_possible_when_the_install_would_start()
    {
        Assert.False(UpdateAttention.For("checking", "0.1.1", 0)!.CanInstall);
        Assert.False(UpdateAttention.For("downloading", "0.1.1", 0.2)!.CanInstall);
        Assert.True(UpdateAttention.For("failed", "0.1.1", 0)!.CanInstall);
    }

    [Theory]
    [InlineData(0, "Downloading update 0.1.1: 0%")]
    [InlineData(0.456, "Downloading update 0.1.1: 45%")]
    [InlineData(0.999, "Downloading update 0.1.1: 99%")]
    [InlineData(1, "Installing update 0.1.1. Hanami will restart.")]
    public void Download_progress_is_in_the_tooltip(double progress, string tip) =>
        Assert.Equal(tip, UpdateAttention.For("downloading", "0.1.1", progress)!.TipLine);

    [Fact]
    public void Failure_is_in_the_tooltip_and_the_menu()
    {
        var p = UpdateAttention.For("failed", "0.1.1", 0)!;
        Assert.Contains("failed", p.TipLine);
        Assert.Contains("0.1.1", p.MenuText);
    }

    [Fact]
    public void Tooltip_without_an_offer_is_the_status_alone() =>
        Assert.Equal("Hanami — Running", UpdateAttention.Tooltip("Running", null));

    [Fact]
    public void Tooltip_keeps_the_update_line_when_a_long_status_is_cut()
    {
        var tip = UpdateAttention.Tooltip("Paused — a very long reason that would never fit in a tray tooltip", UpdateAttention.For("available", "0.3.0-beta.1", 0));
        Assert.Equal(UpdateAttention.MaxTipChars, tip.Length);
        Assert.StartsWith("Update 0.3.0-beta.1 available\nHanami", tip);
    }

    [Theory]
    [InlineData("available", 0)]
    [InlineData("downloading", 0.5)]
    [InlineData("downloading", 1)]
    [InlineData("failed", 0)]
    public void The_update_line_alone_always_fits_a_tooltip(string status, double progress) =>
        Assert.True(UpdateAttention.For(status, "10.20.30-beta.100", progress)!.TipLine.Length <= UpdateAttention.MaxTipChars);

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void The_dot_sits_in_the_top_right_corner_and_is_small(int size)
    {
        var (x, y, d) = UpdateAttention.DotBounds(size);
        Assert.Equal(size, x + d);
        Assert.Equal(0, y);
        Assert.InRange(d, 7, size * 0.5);
    }
}
