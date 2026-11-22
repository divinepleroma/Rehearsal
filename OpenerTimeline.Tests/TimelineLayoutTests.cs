using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline.Tests;

public class TimelineLayoutTests
{
    private static TimelineStep Gcd => new() { IsGcd = true };
    private static TimelineStep Off => new() { IsGcd = false };

    [Fact]
    public void Arrange_GivesEveryStepItsOwnColumn()
    {
        var style = TimelineStyle.Default with { IconSize = 50f, OffGcdScale = 0.8f, Gap = 10f };
        var metrics = TimelineLayout.Measure(style);
        TimelineStep[] steps = [Gcd, Off, Off, Gcd];
        var left = new float[steps.Length];

        var width = TimelineLayout.Arrange(steps, metrics, left);

        Assert.Equal([0f, 60f, 110f, 160f], left);
        Assert.Equal(210f, width); // no trailing gap
    }

    [Fact]
    public void Arrange_EmptyOpenerHasNoWidth()
    {
        var metrics = TimelineLayout.Measure(TimelineStyle.Default);

        Assert.Equal(0f, TimelineLayout.Arrange([], metrics, []));
    }

    [Fact]
    public void Measure_ScalesEverythingWithTheUiScale()
    {
        var small = TimelineLayout.Measure(TimelineStyle.Default with { Scale = 1f });
        var large = TimelineLayout.Measure(TimelineStyle.Default with { Scale = 2f });

        Assert.Equal(small.GcdSize * 2f, large.GcdSize);
        Assert.Equal(small.TotalHeight * 2f, large.TotalHeight, 0);
    }

    [Fact]
    public void Staggered_RaisesAbilitiesAboveGlobalCooldowns()
    {
        var m = TimelineLayout.Measure(TimelineStyle.Default with { Layout = TimelineLayoutMode.Staggered });

        Assert.True(m.OffIconTop < m.GcdIconTop);
        Assert.True(m.OffSize < m.GcdSize);
        // The lanes overlap vertically; that overlap is what makes it one line rather than two rows.
        Assert.True(m.OffIconTop + m.OffSize > m.GcdIconTop);
        Assert.InRange(m.RailY, m.GcdIconTop, m.OffIconTop + m.OffSize);
        // Ability key caps above their icons, global cooldown key caps below theirs.
        Assert.True(m.OffChipTop + m.ChipHeight <= m.OffIconTop);
        Assert.True(m.GcdChipTop >= m.GcdIconTop + m.GcdSize);
    }

    [Fact]
    public void Inline_CentresAbilitiesOnTheSameRow()
    {
        var m = TimelineLayout.Measure(TimelineStyle.Default with { Layout = TimelineLayoutMode.Inline });

        Assert.Equal(m.GcdIconTop + m.GcdSize / 2f, m.OffIconTop + m.OffSize / 2f, 0);
        Assert.Equal(m.GcdChipTop, m.OffChipTop);
        Assert.Equal(0f, m.Gutter);
    }

    [Fact]
    public void HidingParts_MakesTheOverlayShorter()
    {
        var full = TimelineLayout.Measure(TimelineStyle.Default);
        var noHeader = TimelineLayout.Measure(TimelineStyle.Default with { ShowHeader = false });
        var noHints = TimelineLayout.Measure(TimelineStyle.Default with { ShowHints = false });
        var inline = TimelineLayout.Measure(TimelineStyle.Default with { Layout = TimelineLayoutMode.Inline });

        Assert.True(noHeader.TotalHeight < full.TotalHeight);
        Assert.True(noHints.TotalHeight < full.TotalHeight);
        Assert.True(inline.TotalHeight < full.TotalHeight);
    }

    [Fact]
    public void Compact_IsOneShortRowUnderAStatusLine()
    {
        var full = TimelineLayout.Measure(TimelineStyle.Default);
        var compact = TimelineLayout.Measure(TimelineStyle.DefaultCompact);
        var bare = TimelineLayout.Measure(TimelineStyle.DefaultCompact with { ShowStatus = false, ShowHints = false });

        Assert.True(compact.TotalHeight < full.TotalHeight * 0.6f);
        Assert.Equal(compact.StatusHeight, compact.PanelTop);
        Assert.Equal(0f, compact.Gutter);
        Assert.Equal(0f, compact.RulerHeight);
        Assert.Equal(0f, bare.PanelTop);
        Assert.True(bare.TotalHeight < compact.TotalHeight);
    }

    [Fact]
    public void RulerAndBurstNotes_TakeTheirOwnRows()
    {
        var plain = TimelineLayout.Measure(TimelineStyle.Default with { ShowRuler = false, Callouts = BurstCallouts.Off });
        var ruler = TimelineLayout.Measure(TimelineStyle.Default with { Callouts = BurstCallouts.Off });
        var both = TimelineLayout.Measure(TimelineStyle.Default);

        Assert.True(ruler.TotalHeight > plain.TotalHeight);
        Assert.True(both.TotalHeight > ruler.TotalHeight);
        Assert.True(both.RulerTop >= both.GcdChipTop + both.ChipHeight);
        Assert.True(both.CalloutTop + both.CalloutHeight <= both.OffChipTop);
    }

    [Fact]
    public void PinnedScroll_KeepsTheFocusInOnePlace()
    {
        const float view = 400f;

        // Even at the very start, where that leaves empty space before the first step.
        Assert.Equal(20f - view * TimelineLayout.CompactAnchor, TimelineLayout.PinnedScrollTarget(view, 20f));
        Assert.Equal(900f - view * TimelineLayout.CompactAnchor, TimelineLayout.PinnedScrollTarget(view, 900f));
    }

    [Fact]
    public void Scroll_StaysPutWhenEverythingFits()
    {
        Assert.Equal(0f, TimelineLayout.ScrollTarget(500f, 800f, 450f));
        Assert.Equal(0f, TimelineLayout.ScrollTarget(800f, 800f, 700f));
    }

    [Fact]
    public void Scroll_KeepsTheFocusAtTheAnchor()
    {
        const float view = 1000f;

        var scroll = TimelineLayout.ScrollTarget(3000f, view, 1500f);

        Assert.Equal(1500f - view * TimelineLayout.Anchor, scroll);
    }

    [Fact]
    public void Scroll_NeverRunsPastEitherEnd()
    {
        Assert.Equal(0f, TimelineLayout.ScrollTarget(3000f, 1000f, 50f));
        Assert.Equal(2000f, TimelineLayout.ScrollTarget(3000f, 1000f, 2990f));
    }
}
