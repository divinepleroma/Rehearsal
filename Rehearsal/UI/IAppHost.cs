using System;
using System.Collections.Generic;
using Rehearsal.Data;
using Rehearsal.Training;
using Rehearsal.UI.Timeline;

namespace Rehearsal.UI;

/// <summary>One finished attempt, kept for the results view.</summary>
public sealed record RunRecord(
    DateTime When,
    string Title,
    Jobs Job,
    FinishReason Reason,
    double Duration,
    IReadOnlyList<int> Opener,
    IReadOnlyList<StepResult> Steps,
    IReadOnlyList<Mistake> Mistakes)
{
    public int Total => Opener.Count;

    public int Clean
    {
        get
        {
            var clean = 0;
            foreach (var step in Steps)
                if (step.IsClean)
                    clean++;
            return clean;
        }
    }

    public float Accuracy => Total == 0 ? 0f : Clean / (float)Total;
    public bool IsClean => Reason == FinishReason.Completed && Mistakes.Count == 0;
}

/// <summary>What the views need from the live trainer. The game implements it; so does the preview tool.</summary>
public interface ITrainer
{
    /// <summary>The library entry being practised, or null (nothing selected, or an unsaved opener).</summary>
    OpenerRef? Active { get; }

    string ActiveTitle { get; }
    Jobs ActiveJob { get; }

    /// <summary>The opener being practised, as it is shown; empty when there is none.</summary>
    IReadOnlyList<int> ActiveActions { get; }

    /// <summary>The level the player is playing at right now (after level sync).</summary>
    int Level { get; }

    /// <summary>What fitting the opener to <see cref="Level"/> changed, if it had to be fitted.</summary>
    AdaptedOpener? Adjustment { get; }

    /// <summary>The player's global cooldown relative to the base 2.5s, for time estimates.</summary>
    float Speed { get; }

    TrainerSession? Session { get; }

    /// <summary>Incremented for every new attempt.</summary>
    int RunId { get; }

    /// <summary>Finished attempts, most recent first.</summary>
    IReadOnlyList<RunRecord> History { get; }

    /// <summary>Seconds left on the practice countdown, negative when it is not running.</summary>
    float CountdownRemaining { get; }

    /// <summary>0..1 while casting, negative otherwise.</summary>
    float CastProgress { get; }

    bool CastMatches { get; }

    /// <summary>Whether this opener is the one chosen for its job (which need not be the current job).</summary>
    bool IsSelected(OpenerRef opener);

    /// <summary>Makes a library opener the one to practise for its job.</summary>
    void Select(OpenerRef opener);

    /// <summary>Practise nothing on that job.</summary>
    void Deselect(Jobs job);

    /// <summary>Practise an opener that is not (yet) in the library.</summary>
    void Practice(string title, Jobs job, IReadOnlyList<int> actions);

    /// <summary>Abandons the current attempt and waits for a new one.</summary>
    void Reset();

    void StartCountdown();
    void StopCountdown();

    KeyHint HintFor(int stepId, bool isCurrent);
}

/// <summary>Everything the plugin's windows need from the outside world.</summary>
public interface IAppHost : ITimelineHost
{
    /// <summary>Dalamud's global UI scale.</summary>
    float Scale { get; }

    Settings Settings { get; }
    ActionCatalog Catalog { get; }
    OpenerLibrary Library { get; }
    ITrainer Trainer { get; }

    /// <summary>The job the player is on right now; ANY when it has no openers.</summary>
    Jobs CurrentJob { get; }

    string Version { get; }

    void SaveSettings();
    void OpenLink(string url);

    /// <summary>Plays one of the game's chat sound effects (the N of &lt;se.N&gt;, 1 to 16).</summary>
    void PlaySound(int soundEffect);
}
