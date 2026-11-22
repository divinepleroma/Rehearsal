using OpenerTimeline.Training;

namespace OpenerTimeline.Tests;

public class OpenerTimingTests
{
    private static StepSpec Gcd(float cast = 0f, float length = 2.5f) => new(true, length, cast);
    private static StepSpec Weave => new(false, 0f, 0f);

    [Fact]
    public void InstantOpener_StartsAtZeroAndRollsTheGcd()
    {
        var timing = OpenerTiming.Estimate([Gcd(), Gcd(), Gcd()]);

        Assert.Equal([new StepTiming(1, 0f), new StepTiming(2, 2.5f), new StepTiming(3, 5f)], timing);
    }

    [Fact]
    public void Weaves_SitBetweenTheGcdsTheyFollow()
    {
        var timing = OpenerTiming.Estimate([Gcd(), Weave, Weave, Gcd()]);

        Assert.Equal(1, timing[1].GcdNumber);
        Assert.Equal(1, timing[2].GcdNumber);
        Assert.Equal(OpenerTiming.FirstWeaveSeconds, timing[1].Time, 3);
        Assert.Equal(OpenerTiming.FirstWeaveSeconds + OpenerTiming.WeaveSeconds, timing[2].Time, 3);
        Assert.Equal(2.5f, timing[3].Time, 3); // a double weave fits
    }

    [Fact]
    public void Precast_LandsOnThePullAndHoldsUpTheNextGcd()
    {
        // Red Mage: a 5s cast started before the pull, then an instant.
        var timing = OpenerTiming.Estimate([Gcd(5f), Gcd(), Weave, Gcd()]);

        Assert.Equal(-5f, timing[0].Time, 3);
        Assert.Equal(0f, timing[1].Time, 3);
        Assert.Equal(0.6f, timing[2].Time, 3);
        Assert.Equal(2.5f, timing[3].Time, 3);
        Assert.Equal([1, 2, 2, 3], timing.Select(t => t.GcdNumber));
    }

    [Fact]
    public void Precast_CanBeLeftOutOfTheCount()
    {
        var timing = OpenerTiming.Estimate([Gcd(5f), Gcd(), Gcd()], countPrecast: false);

        Assert.Equal([0, 1, 2], timing.Select(t => t.GcdNumber));
    }

    [Fact]
    public void InstantFirstGcd_IsAlwaysCounted()
    {
        var timing = OpenerTiming.Estimate([Gcd(), Gcd()], countPrecast: false);

        Assert.Equal([1, 2], timing.Select(t => t.GcdNumber));
    }

    [Fact]
    public void LaterCasts_DoNotStretchTheRhythm()
    {
        // Dualcast, Triplecast and friends make these instant in practice.
        var timing = OpenerTiming.Estimate([Gcd(), Gcd(5f), Gcd(5f)]);

        Assert.Equal(2.5f, timing[1].Time, 3);
        Assert.Equal(5f, timing[2].Time, 3);
    }

    [Fact]
    public void PrePullAbilities_ComeBeforeTheFirstGcd()
    {
        var timing = OpenerTiming.Estimate([Weave, Gcd(2f), Gcd()]);

        Assert.Equal(0, timing[0].GcdNumber);
        Assert.True(timing[0].Time < timing[1].Time);
        Assert.Equal(-2f, timing[1].Time, 3);
        Assert.Equal(0.5f, timing[2].Time, 3);
    }

    [Fact]
    public void ShortAndLongGcds_UseTheirOwnLength()
    {
        // A 1.5s combo hit followed by a 4s motif.
        var timing = OpenerTiming.Estimate([Gcd(length: 1.5f), Gcd(length: 4f), Gcd()]);

        Assert.Equal(1.5f, timing[1].Time, 3);
        Assert.Equal(5.5f, timing[2].Time, 3);
    }

    [Fact]
    public void Speed_ScalesTheWholePlan()
    {
        var timing = OpenerTiming.Estimate([Gcd(), Gcd(), Gcd()], 0.96f);

        Assert.Equal(2.4f, timing[1].Time, 3);
        Assert.Equal(4.8f, timing[2].Time, 3);
    }

    [Fact]
    public void TooManyWeaves_PushTheNextGcdBack()
    {
        var timing = OpenerTiming.Estimate([Gcd(), Weave, Weave, Weave, Weave, Gcd()]);

        Assert.True(timing[5].Time > 2.5f);
    }

    [Fact]
    public void EmptyOpener_GivesNothing()
    {
        Assert.Empty(OpenerTiming.Estimate([]));
    }
}
