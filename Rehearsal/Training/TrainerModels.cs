namespace Rehearsal.Training;

public enum RunState
{
    /// <summary>Waiting for the first opener action.</summary>
    Armed,

    /// <summary>The opener is in progress.</summary>
    Running,

    /// <summary>The run ended; results are available.</summary>
    Finished
}

public enum FinishReason
{
    None,

    /// <summary>Every step was resolved.</summary>
    Completed,

    /// <summary>"Stop at first mistake" ended the run.</summary>
    StoppedOnMistake,

    /// <summary>Combat ended before the opener was finished.</summary>
    CombatEnded,

    /// <summary>No action for too long.</summary>
    TimedOut,

    /// <summary>Reset by the player.</summary>
    Aborted
}

public enum StepStatus
{
    Pending,
    Hit,
    Skipped
}

public enum ActionOutcome
{
    /// <summary>The action had no effect on the run.</summary>
    Ignored,

    /// <summary>The expected step was executed.</summary>
    Hit,

    /// <summary>A later step was executed; the steps in between were skipped.</summary>
    HitAfterSkip,

    /// <summary>The action is not what the opener expects next.</summary>
    Wrong
}

public enum MistakeKind
{
    WrongAction,
    Skipped
}

public sealed class StepResult
{
    public StepStatus Status;

    /// <summary>Seconds since the run started when the step was resolved.</summary>
    public double Time;

    /// <summary>What was actually used to satisfy the step (differs from the opener id for groups).</summary>
    public uint ActualId;

    /// <summary>Wrong presses made while this step was the expected one.</summary>
    public int WrongPresses;

    public bool IsClean => Status == StepStatus.Hit && WrongPresses == 0;
}

/// <param name="StepIndex">The step that was expected when the mistake happened.</param>
/// <param name="PressedId">For <see cref="MistakeKind.WrongAction"/>: the action that was used instead.</param>
/// <param name="Time">Seconds since the run started.</param>
public readonly record struct Mistake(int StepIndex, MistakeKind Kind, uint PressedId, double Time);

public sealed class TrainerOptions
{
    /// <summary>End the run as soon as anything goes wrong.</summary>
    public bool StopAtFirstMistake;

    /// <summary>
    /// How many steps ahead a wrong press is compared against. If it matches one of them the steps
    /// in between count as skipped and the run carries on from there instead of getting stuck.
    /// </summary>
    public int SkipLookahead = 2;
}
