using System;
using System.Collections.Generic;
using System.Linq;
using OpenerTimeline.Data;
using OpenerTimeline.Training;

namespace OpenerTimeline.UI.Timeline;

/// <summary>
/// Turns an opener plus the state of the current attempt into what the renderer draws.
/// The per-step facts that never change (icon, name, cooldown, timing) are cached per opener.
/// </summary>
public sealed class TimelineFrameBuilder(ActionCatalog catalog)
{
    private bool cachedCountPrecast;
    private int cachedHash;
    private float cachedSpeed = -1f;
    private TimelineStep[] template = [];

    /// <param name="frame">Frame to fill; its step list is reused.</param>
    /// <param name="actions">The opener, in the stored id format.</param>
    /// <param name="session">The attempt in progress, if any.</param>
    /// <param name="hintFor">Resolves the keybind hint for a step id; the flag says whether it is the current step.</param>
    /// <param name="now">Monotonic clock in seconds.</param>
    /// <param name="speed">The player's global cooldown relative to the base 2.5s.</param>
    /// <param name="countPrecast">Whether an opening cast before the pull counts as GCD 1.</param>
    public void Build(
        TimelineFrame frame, IReadOnlyList<int> actions, TrainerSession? session, Func<int, bool, KeyHint>? hintFor, double now,
        float speed = 1f, bool countPrecast = true)
    {
        EnsureTemplate(actions, speed, countPrecast);

        frame.Steps.Clear();
        var running = session != null && session.State != RunState.Finished;
        var current = session?.CurrentIndex ?? 0;

        for (var i = 0; i < template.Length; i++)
        {
            var step = template[i];
            var isCurrent = i == current && (session == null || running);

            if (session != null && i < session.Steps.Count)
            {
                var result = session.Steps[i];
                step.WrongPresses = result.WrongPresses;
                step.Visual = result.Status switch
                {
                    StepStatus.Hit => result.WrongPresses > 0 ? StepVisual.DoneAfterMistake : StepVisual.Done,
                    StepStatus.Skipped => StepVisual.Skipped,
                    _ => isCurrent ? StepVisual.Current : StepVisual.Upcoming
                };
            }
            else
            {
                step.Visual = isCurrent ? StepVisual.Current : StepVisual.Upcoming;
            }

            if (hintFor != null)
                step.Hint = hintFor(actions[i], isCurrent);

            frame.Steps.Add(step);
        }

        frame.State = session?.State ?? RunState.Armed;
        frame.FinishReason = session?.FinishReason ?? FinishReason.None;
        frame.CurrentIndex = current;
        frame.Elapsed = session?.Elapsed(now) ?? 0d;
        frame.Hits = session?.HitCount ?? 0;
        frame.Wrong = session?.WrongCount ?? 0;
        frame.Skipped = session?.SkippedCount ?? 0;

        frame.WrongAge = -1f;
        frame.WrongIconId = 0;
        if (session?.LastWrong is { } wrong && wrong.StepIndex == current)
        {
            frame.WrongAge = (float)(now - session.LastWrongTime);
            frame.WrongIconId = catalog.GetIcon((int)wrong.PressedId);
        }
    }

    /// <summary>Builds the static description of a list of steps, e.g. for previews in the library.</summary>
    public static TimelineStep[] Describe(ActionCatalog catalog, IReadOnlyList<int> actions, float speed = 1f, bool countPrecast = true)
    {
        var steps = new TimelineStep[actions.Count];
        var specs = new StepSpec[actions.Count];
        for (var i = 0; i < steps.Length; i++)
        {
            steps[i] = Describe(catalog, actions[i]);
            specs[i] = new StepSpec(steps[i].IsGcd, catalog.GetGcdSeconds(actions[i]), catalog.GetCastSeconds(actions[i]));
        }

        var timing = OpenerTiming.Estimate(specs, speed, countPrecast);
        for (var i = 0; i < steps.Length; i++)
        {
            steps[i].GcdNumber = timing[i].GcdNumber;
            steps[i].Time = timing[i].Time;
        }

        return steps;
    }

    public static TimelineStep Describe(ActionCatalog catalog, int id)
    {
        var recast = catalog.GetRecast(id);
        var step = new TimelineStep
        {
            IconId = catalog.GetIcon(id),
            Name = catalog.GetName(id),
            IsGcd = catalog.IsGcd(id),
            Recast = recast,
            Cooldown = ActionCatalog.Classify(recast),
            IsWildcard = id == SpecialActions.CatchAll,
            Visual = StepVisual.Upcoming
        };

        if (id < 0 && id != SpecialActions.Potion && GroupOfActions.TryGetDefault(id, out var group))
        {
            // Several ids often share one icon (the mudra variants); show each picture once.
            step.GroupIcons = group.Actions.Select(a => catalog.GetIcon((int)a)).Distinct().Take(4).ToArray();
        }

        return step;
    }

    private void EnsureTemplate(IReadOnlyList<int> actions, float speed, bool countPrecast)
    {
        var hash = new HashCode();
        hash.Add(actions.Count);
        for (var i = 0; i < actions.Count; i++)
            hash.Add(actions[i]);

        // Speed only matters to a hundredth: gear changes move it, buffs flickering should not.
        speed = MathF.Round(speed * 100f) / 100f;

        var value = hash.ToHashCode();
        if (value == cachedHash && template.Length == actions.Count && speed == cachedSpeed && countPrecast == cachedCountPrecast)
            return;

        cachedHash = value;
        cachedSpeed = speed;
        cachedCountPrecast = countPrecast;
        template = Describe(catalog, actions, speed, countPrecast);
    }
}
