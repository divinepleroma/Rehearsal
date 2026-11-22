using Rehearsal.Data;
using Rehearsal.Training;

namespace Rehearsal.Tests;

public class TrainerSessionTests
{
    /// <summary>Names are just the id, so two different ids never match by name.</summary>
    private static readonly ActionMatcher Matcher = new(id => id.ToString());

    private static TrainerSession Session(int[] opener, bool stopAtFirstMistake = false, int lookahead = 2)
    {
        return new TrainerSession(opener, Matcher,
                                  new TrainerOptions { StopAtFirstMistake = stopAtFirstMistake, SkipLookahead = lookahead });
    }

    private static void Play(TrainerSession session, params uint[] actions)
    {
        var now = 10d;
        foreach (var action in actions)
        {
            session.OnAction(action, false, false, now);
            now += 1d;
        }
    }

    [Fact]
    public void PerfectRun_CompletesCleanly()
    {
        var session = Session([1, 2, 3, 1, 2]);

        Play(session, 1, 2, 3, 1, 2);

        Assert.Equal(RunState.Finished, session.State);
        Assert.Equal(FinishReason.Completed, session.FinishReason);
        Assert.True(session.IsClean);
        Assert.Empty(session.Mistakes);
        Assert.Equal(5, session.HitCount);
        Assert.Equal(1f, session.Accuracy);
        Assert.Equal(4d, session.Elapsed(999d), 3);
    }

    [Fact]
    public void CatchAllStep_AcceptsAnything()
    {
        var session = Session([1, 2, 0, 1, 2]);

        Play(session, 1, 2, 77, 1, 2);

        Assert.True(session.IsClean);
    }

    [Fact]
    public void NothingCounts_UntilTheOpenerStarts()
    {
        var session = Session([1, 2, 3]);

        var outcome = session.OnAction(99, false, false, 5d);

        Assert.Equal(ActionOutcome.Ignored, outcome);
        Assert.Equal(RunState.Armed, session.State);
        Assert.Empty(session.Mistakes);
        Assert.Equal(0d, session.Elapsed(50d));
    }

    [Fact]
    public void FirstMatchingAction_StartsTheClock()
    {
        var session = Session([1, 2, 3]);

        session.OnAction(1, false, false, 42d);

        Assert.Equal(RunState.Running, session.State);
        Assert.Equal(42d, session.StartTime);
        Assert.Equal(1, session.CurrentIndex);
        Assert.Equal(3d, session.Elapsed(45d), 3);
    }

    [Fact]
    public void WrongPress_IsFlaggedButDoesNotAdvance()
    {
        var session = Session([1, 2, 3]);

        session.OnAction(1, false, false, 10d);
        var outcome = session.OnAction(9, false, false, 11d);

        Assert.Equal(ActionOutcome.Wrong, outcome);
        Assert.Equal(1, session.CurrentIndex);
        Assert.Equal(1, session.Steps[1].WrongPresses);
        var mistake = Assert.Single(session.Mistakes);
        Assert.Equal(new Mistake(1, MistakeKind.WrongAction, 9, 1d), mistake);
        Assert.Equal(mistake, session.LastWrong);

        // The right button afterwards still completes the step, but it no longer counts as clean.
        session.OnAction(2, false, false, 12d);
        session.OnAction(3, false, false, 13d);

        Assert.Equal(FinishReason.Completed, session.FinishReason);
        Assert.False(session.IsClean);
        Assert.Equal(2, session.CleanCount);
        Assert.False(session.Steps[1].IsClean);
    }

    [Fact]
    public void SkippedStep_IsMarkedAndTheRunCarriesOn()
    {
        // The player forgets the weave at index 1 and goes straight to the next GCD.
        var session = Session([1, 2, 3, 4]);

        session.OnAction(1, false, false, 10d);
        var outcome = session.OnAction(3, false, false, 11d);

        Assert.Equal(ActionOutcome.HitAfterSkip, outcome);
        Assert.Equal(StepStatus.Skipped, session.Steps[1].Status);
        Assert.Equal(StepStatus.Hit, session.Steps[2].Status);
        Assert.Equal(3, session.CurrentIndex);
        Assert.Equal(new Mistake(1, MistakeKind.Skipped, 0, 1d), Assert.Single(session.Mistakes));

        session.OnAction(4, false, false, 12d);

        Assert.Equal(FinishReason.Completed, session.FinishReason);
        Assert.Equal(1, session.SkippedCount);
        Assert.Equal(3, session.HitCount);
    }

    [Fact]
    public void Skip_OnlyLooksAsFarAsTheLookahead()
    {
        var session = Session([1, 2, 3, 4, 5], lookahead: 2);

        session.OnAction(1, false, false, 10d);
        var outcome = session.OnAction(5, false, false, 11d); // three steps ahead

        Assert.Equal(ActionOutcome.Wrong, outcome);
        Assert.Equal(1, session.CurrentIndex);
    }

    [Fact]
    public void Skip_NeverJumpsOntoACatchAll()
    {
        // Step 2 would accept anything; reaching it must not be read as "skipped step 1".
        var session = Session([1, 2, 0, 4]);

        session.OnAction(1, false, false, 10d);
        var outcome = session.OnAction(9, false, false, 11d);

        Assert.Equal(ActionOutcome.Wrong, outcome);
        Assert.Equal(1, session.CurrentIndex);
        Assert.Equal(StepStatus.Pending, session.Steps[1].Status);
    }

    [Fact]
    public void ArmedRun_CanStartOnASlightlyLaterStep()
    {
        // Pre-pull action forgotten: the run still starts, with the first step marked as skipped.
        var session = Session([1, 2, 3]);

        var outcome = session.OnAction(2, false, false, 10d);

        Assert.Equal(ActionOutcome.HitAfterSkip, outcome);
        Assert.Equal(RunState.Running, session.State);
        Assert.Equal(StepStatus.Skipped, session.Steps[0].Status);
        Assert.Equal(2, session.CurrentIndex);
    }

    [Fact]
    public void StopAtFirstMistake_EndsTheRunOnAWrongPress()
    {
        var session = Session([1, 2, 3], stopAtFirstMistake: true);

        session.OnAction(1, false, false, 10d);
        session.OnAction(9, false, false, 11d);

        Assert.Equal(RunState.Finished, session.State);
        Assert.Equal(FinishReason.StoppedOnMistake, session.FinishReason);
        Assert.Equal(1, session.CurrentIndex);

        // Further input is ignored.
        Assert.Equal(ActionOutcome.Ignored, session.OnAction(2, false, false, 12d));
        Assert.Equal(1, session.HitCount);
    }

    [Fact]
    public void StopAtFirstMistake_EndsTheRunOnASkip()
    {
        var session = Session([1, 2, 3, 4], stopAtFirstMistake: true);

        session.OnAction(1, false, false, 10d);
        session.OnAction(3, false, false, 11d);

        Assert.Equal(FinishReason.StoppedOnMistake, session.FinishReason);
    }

    [Fact]
    public void StopAtFirstMistake_StillReportsACompletedRunAsCompleted()
    {
        // Skipping onto the very last step finishes the opener; that takes precedence.
        var session = Session([1, 2, 3], stopAtFirstMistake: true);

        session.OnAction(1, false, false, 10d);
        session.OnAction(3, false, false, 11d);

        Assert.Equal(FinishReason.Completed, session.FinishReason);
        Assert.False(session.IsClean);
    }

    [Fact]
    public void UtilityActions_AreIgnoredWhenOffScript()
    {
        var session = Session([1, 2, 3]);

        session.OnAction(1, false, false, 10d);
        var outcome = session.OnAction(50, false, true, 11d); // e.g. Sprint

        Assert.Equal(ActionOutcome.Ignored, outcome);
        Assert.Empty(session.Mistakes);
        Assert.Equal(10d, session.LastActionTime);
    }

    [Fact]
    public void UtilityActions_StillCountWhenTheOpenerAsksForThem()
    {
        var session = Session([1, 50, 3]);

        session.OnAction(1, false, false, 10d);
        var outcome = session.OnAction(50, false, true, 11d);

        Assert.Equal(ActionOutcome.Hit, outcome);
        Assert.Equal(2, session.CurrentIndex);
    }

    [Fact]
    public void PotionStep_IsSatisfiedByAnyItem()
    {
        var session = Session([1, SpecialActions.Potion, 3]);

        session.OnAction(1, false, false, 10d);
        var outcome = session.OnAction(1_044_162, true, false, 11d);
        session.OnAction(3, false, false, 12d);

        Assert.Equal(ActionOutcome.Hit, outcome);
        Assert.True(session.IsClean);
    }

    [Fact]
    public void PotionStep_IsNotSatisfiedByAnActionWithTheSameNumber()
    {
        var session = Session([1, SpecialActions.Potion, 3], lookahead: 0);

        session.OnAction(1, false, false, 10d);
        var outcome = session.OnAction(44162, false, false, 11d);

        Assert.Equal(ActionOutcome.Wrong, outcome);
    }

    [Fact]
    public void OffScriptItems_AreNeverMistakes()
    {
        var session = Session([1, 2, 3]);

        session.OnAction(1, false, false, 10d);
        var outcome = session.OnAction(44162, true, false, 11d);

        Assert.Equal(ActionOutcome.Ignored, outcome);
        Assert.Empty(session.Mistakes);
    }

    [Fact]
    public void GroupSteps_AcceptAnyMember()
    {
        // -1 is "Dancer Step".
        var session = Session([2, -1, 1, -1, -1]);

        Play(session, 2, 15999, 1, 16000, 16002);

        Assert.True(session.IsClean);
        Assert.Equal(16000u, session.Steps[3].ActualId);
    }

    [Fact]
    public void GroupSteps_RejectNonMembers()
    {
        var session = Session([2, -1, 1, -1, -1], lookahead: 0);

        Play(session, 2, 15999, 1, 1, 15999);

        Assert.Equal(1, session.WrongCount);
        Assert.Equal(3, session.Mistakes[0].StepIndex);
    }

    [Fact]
    public void Finish_EndsAnUnfinishedRunAtItsLastAction()
    {
        var session = Session([1, 2, 3]);
        session.OnAction(1, false, false, 10d);
        session.OnAction(2, false, false, 12.5d);

        // Noticed 15 seconds later that the player walked away.
        session.Finish(FinishReason.TimedOut, 27.5d);

        Assert.Equal(RunState.Finished, session.State);
        Assert.Equal(FinishReason.TimedOut, session.FinishReason);
        Assert.Equal(2.5d, session.Elapsed(100d), 3);
        Assert.False(session.IsComplete);
    }

    [Fact]
    public void Finish_IsIdempotent()
    {
        var session = Session([1, 2]);
        Play(session, 1, 2);

        session.Finish(FinishReason.Aborted, 500d);

        Assert.Equal(FinishReason.Completed, session.FinishReason);
        Assert.Equal(1d, session.Elapsed(999d), 3);
    }

    [Fact]
    public void WouldAdvance_ReflectsTheLookahead()
    {
        var session = Session([1, 2, 3, 4]);

        Assert.True(session.WouldAdvance(1, false));
        Assert.True(session.WouldAdvance(3, false));
        Assert.False(session.WouldAdvance(4, false));
        Assert.False(session.WouldAdvance(1, true));
    }

    [Fact]
    public void EmptyOpener_NeverStarts()
    {
        var session = Session([]);

        Assert.Equal(ActionOutcome.Ignored, session.OnAction(1, false, false, 1d));
        Assert.Equal(RunState.Armed, session.State);
        Assert.Equal(0f, session.Accuracy);
    }
}
