using System;
using System.Collections.Generic;

namespace Rehearsal.Training;

/// <summary>
/// One attempt at an opener, fed live with the actions the player uses.
/// Unlike the upstream recorder (which compared two lists after the fact) this resolves every
/// press immediately so the timeline can react to it.
/// </summary>
public sealed class TrainerSession
{
    private readonly ActionMatcher matcher;
    private readonly List<Mistake> mistakes = [];
    private readonly TrainerOptions options;
    private readonly StepResult[] steps;

    public TrainerSession(IReadOnlyList<int> opener, ActionMatcher matcher, TrainerOptions? options = null)
    {
        Opener = opener;
        this.matcher = matcher;
        this.options = options ?? new TrainerOptions();

        steps = new StepResult[opener.Count];
        for (var i = 0; i < steps.Length; i++)
            steps[i] = new StepResult();
    }

    public IReadOnlyList<int> Opener { get; }
    public IReadOnlyList<StepResult> Steps => steps;
    public IReadOnlyList<Mistake> Mistakes => mistakes;

    public RunState State { get; private set; } = RunState.Armed;
    public FinishReason FinishReason { get; private set; } = FinishReason.None;

    /// <summary>Index of the step expected next. Equals the step count once everything is resolved.</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>Clock value (seconds) of the first opener action.</summary>
    public double StartTime { get; private set; }

    /// <summary>Clock value (seconds) at which the run finished.</summary>
    public double EndTime { get; private set; }

    /// <summary>Clock value of the last action that affected the run.</summary>
    public double LastActionTime { get; private set; }

    /// <summary>The most recent wrong press, for transient feedback.</summary>
    public Mistake? LastWrong { get; private set; }

    /// <summary>Clock value of <see cref="LastWrong"/>.</summary>
    public double LastWrongTime { get; private set; }

    public int HitCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int WrongCount { get; private set; }

    public bool IsComplete => FinishReason == FinishReason.Completed;
    public bool IsClean => IsComplete && mistakes.Count == 0;

    /// <summary>Steps executed in order without a wrong press first.</summary>
    public int CleanCount
    {
        get
        {
            var clean = 0;
            foreach (var step in steps)
                if (step.IsClean)
                    clean++;
            return clean;
        }
    }

    /// <summary>Share of the opener that was executed cleanly, 0..1.</summary>
    public float Accuracy => steps.Length == 0 ? 0f : CleanCount / (float)steps.Length;

    public double Elapsed(double now)
    {
        return State switch
        {
            RunState.Armed => 0,
            RunState.Running => Math.Max(0, now - StartTime),
            _ => Math.Max(0, EndTime - StartTime)
        };
    }

    /// <summary>Whether <paramref name="actionId"/> would advance the run right now.</summary>
    public bool WouldAdvance(uint actionId, bool isItem)
    {
        return State != RunState.Finished && FindMatch(actionId, isItem) >= 0;
    }

    /// <summary>Feeds an action the player just used.</summary>
    /// <param name="actionId">Action row id, or item id when <paramref name="isItem"/> is set.</param>
    /// <param name="isItem">The action was an item use.</param>
    /// <param name="isUtility">
    /// The action is harmless filler (Sprint, role actions...). It still counts when the opener asks
    /// for it, but is never reported as a mistake.
    /// </param>
    /// <param name="now">Monotonic clock in seconds.</param>
    public ActionOutcome OnAction(uint actionId, bool isItem, bool isUtility, double now)
    {
        if (State == RunState.Finished || steps.Length == 0)
            return ActionOutcome.Ignored;

        var match = FindMatch(actionId, isItem);

        if (State == RunState.Armed)
        {
            // Nothing is a mistake until the opener has actually begun.
            if (match < 0)
                return ActionOutcome.Ignored;

            State = RunState.Running;
            StartTime = now;
        }

        if (match >= 0)
        {
            var skipped = match > CurrentIndex;
            for (var i = CurrentIndex; i < match; i++)
            {
                steps[i].Status = StepStatus.Skipped;
                steps[i].Time = now - StartTime;
                SkippedCount++;
                mistakes.Add(new Mistake(i, MistakeKind.Skipped, 0, now - StartTime));
            }

            steps[match].Status = StepStatus.Hit;
            steps[match].Time = now - StartTime;
            steps[match].ActualId = actionId;
            HitCount++;
            CurrentIndex = match + 1;
            LastActionTime = now;

            if (CurrentIndex >= steps.Length)
                Finish(FinishReason.Completed, now);
            else if (skipped && options.StopAtFirstMistake)
                Finish(FinishReason.StoppedOnMistake, now);

            return skipped ? ActionOutcome.HitAfterSkip : ActionOutcome.Hit;
        }

        // Off-script potions and utility actions are never held against the player.
        if (isItem || isUtility)
            return ActionOutcome.Ignored;

        steps[CurrentIndex].WrongPresses++;
        WrongCount++;
        var mistake = new Mistake(CurrentIndex, MistakeKind.WrongAction, actionId, now - StartTime);
        mistakes.Add(mistake);
        LastWrong = mistake;
        LastWrongTime = now;
        LastActionTime = now;

        if (options.StopAtFirstMistake)
            Finish(FinishReason.StoppedOnMistake, now);

        return ActionOutcome.Wrong;
    }

    /// <summary>Ends the run early. Does nothing if it already finished.</summary>
    public void Finish(FinishReason reason, double now)
    {
        if (State == RunState.Finished)
            return;

        if (State == RunState.Armed)
            StartTime = now;

        State = RunState.Finished;
        FinishReason = reason;

        // A run that simply ran out is timed up to its last action, not up to the moment we noticed.
        var ranOut = reason is FinishReason.CombatEnded or FinishReason.TimedOut;
        EndTime = ranOut && LastActionTime > StartTime ? LastActionTime : now;
    }

    /// <summary>
    /// Returns the index of the step this action resolves: the current one, or one within the skip
    /// lookahead. -1 when it matches neither.
    /// </summary>
    private int FindMatch(uint actionId, bool isItem)
    {
        if (CurrentIndex >= steps.Length)
            return -1;

        if (matcher.Matches(Opener[CurrentIndex], actionId, isItem))
            return CurrentIndex;

        var last = Math.Min(steps.Length - 1, CurrentIndex + Math.Max(0, options.SkipLookahead));
        for (var i = CurrentIndex + 1; i <= last; i++)
        {
            // A wildcard matches everything, so reaching it proves nothing about what was skipped.
            if (ActionMatcher.IsWildcard(Opener[i]))
                continue;

            if (matcher.Matches(Opener[i], actionId, isItem))
                return i;
        }

        return -1;
    }
}
