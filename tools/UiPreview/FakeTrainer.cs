using System;
using System.Collections.Generic;
using System.Linq;
using Rehearsal.Data;
using Rehearsal.Training;
using Rehearsal.UI;
using Rehearsal.UI.Timeline;

namespace Rehearsal.Preview;

/// <summary>A scripted stand-in for the live trainer: real sessions, driven by made-up button presses.</summary>
internal sealed class FakeTrainer(ActionCatalog catalog, OpenerLibrary library) : ITrainer
{
    private readonly ActionMatcher matcher = new(catalog.GetName);
    private readonly List<RunRecord> runs = [];
    private List<int> actions = [];
    private FakeHints? hints;

    public OpenerRef? Active { get; private set; }
    public string ActiveTitle { get; private set; } = string.Empty;
    public Jobs ActiveJob { get; private set; } = Jobs.ANY;
    public IReadOnlyList<int> ActiveActions => actions;
    public TrainerSession? Session { get; private set; }
    public int RunId { get; private set; } = 1;
    public IReadOnlyList<RunRecord> History => runs;
    public float CountdownRemaining { get; set; } = -1f;
    public float CastProgress { get; set; } = -1f;
    public bool CastMatches { get; set; }
    public int Level { get; set; } = 100;
    public AdaptedOpener? Adjustment { get; private set; }
    public float Speed => 1f;

    public bool IsSelected(OpenerRef opener)
    {
        return Active == opener;
    }

    public void Select(OpenerRef opener)
    {
        if (!library.TryGet(opener, out var loaded))
            return;

        Active = opener;
        Practice(opener.Name, opener.Job, loaded);
        Active = opener;
    }

    public void Deselect(Jobs job)
    {
        Active = null;
        ActiveTitle = string.Empty;
        actions = [];
        Session = null;
    }

    public void Practice(string title, Jobs job, IReadOnlyList<int> opener)
    {
        Active = null;
        ActiveJob = job;

        // Same rule as the real trainer: below the level an opener needs, show it fitted.
        var adapted = catalog.RequiredLevel(opener) > Level ? catalog.Adapt(opener, Level) : (AdaptedOpener?)null;
        Adjustment = adapted is { Changed: true } ? adapted : null;
        actions = Adjustment?.Actions ?? [..opener];
        ActiveTitle = Adjustment != null ? $"{title}  -  Lv {Level}" : title;
        hints = new FakeHints(actions, false);
        Reset();
    }

    public void Reset()
    {
        Session = new TrainerSession(actions, matcher);
        RunId++;
    }

    public void StartCountdown()
    {
        CountdownRemaining = 7f;
    }

    public void StopCountdown()
    {
        CountdownRemaining = -1f;
    }

    public KeyHint HintFor(int stepId, bool isCurrent)
    {
        return hints?.For(stepId, isCurrent) ?? default;
    }

    /// <summary>Plays a scripted attempt at the active opener and files it in the history.</summary>
    /// <param name="minutesAgo">How long ago it pretends to have happened.</param>
    /// <param name="play">Presses the buttons; returns the clock value at the end.</param>
    public void Simulate(int minutesAgo, Func<TrainerSession, IReadOnlyList<int>, double> play, FinishReason? endEarly = null)
    {
        var session = new TrainerSession(actions, matcher);
        var end = play(session, actions);
        if (endEarly is { } reason)
            session.Finish(reason, end + 15d);

        runs.Insert(0, new RunRecord(DateTime.Today.AddHours(20).AddMinutes(30 - minutesAgo), ActiveTitle, ActiveJob, session.FinishReason,
                                     Math.Max(0d, session.EndTime - session.StartTime), session.Opener, session.Steps,
                                     session.Mistakes.ToArray()));
    }
}

/// <summary>A believable set of bindings so the key caps can be judged without a real hotbar.</summary>
internal sealed class FakeHints
{
    private static readonly string[] Keys = ["1", "2", "3", "4", "Q", "E", "R", "F", "T", "G", "Z", "X", "C", "V", "F1", "M4"];
    private readonly bool gamepad;
    private readonly Dictionary<int, KeyHint> map = new();

    public FakeHints(IEnumerable<int> actions, bool gamepad)
    {
        this.gamepad = gamepad;
        var n = 0;
        foreach (var id in actions.Distinct())
        {
            if (id == SpecialActions.CatchAll)
                continue;

            map[id] = Make(n++, id);
        }
    }

    public KeyHint For(int id, bool isCurrent)
    {
        return map.TryGetValue(id, out var hint) ? hint : default;
    }

    private KeyHint Make(int n, int id)
    {
        if (id < 0 && id != SpecialActions.Potion)
            return KeyHint.Varies;

        if (gamepad)
        {
            return new KeyHint
            {
                Kind = HintKind.Pad, PadRightTrigger = n % 2 == 1, PadFace = n / 2 % 2 == 1, PadDirection = (byte)(n % 4), Bar = 1,
                Slot = (byte)(n + 1)
            };
        }

        if (n == 7)
            return KeyHint.Missing;
        if (n == 9)
            return new KeyHint { Kind = HintKind.Slot, Bar = 3, Slot = 7 };

        var layer = n / 6;
        return new KeyHint
        {
            Kind = HintKind.Key, Key = Keys[n % Keys.Length], Shift = layer == 1, Ctrl = layer == 2, Alt = layer == 3
        };
    }
}
