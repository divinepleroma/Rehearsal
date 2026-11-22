using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Rehearsal.Data;
using Rehearsal.Training;
using Rehearsal.UI.Timeline;

namespace Rehearsal.UI.Views;

/// <summary>
/// A small timeline that plays itself in a loop - including a slip and a skipped weave - so the
/// guide and the setup can show the behaviour instead of describing it.
/// </summary>
public sealed class DemoTimeline(IAppHost host)
{
    private const double StepSeconds = 0.85d;
    private const double PauseSeconds = 3.2d;

    private readonly TimelineFrame frame = new();
    private readonly TimelineRenderer compactRenderer = new();
    private readonly TimelineRenderer fullRenderer = new();
    private List<int> actions = [];
    private TimelineFrameBuilder? builder;
    private int played;
    private int runId;
    private TimelineStep[] sample = [];
    private int sampleHash;
    private TrainerSession? session;
    private double startedAt;

    /// <summary>The steps of the opener the demo uses, for static illustrations.</summary>
    public TimelineStep[] SampleSteps()
    {
        EnsureOpener();
        return sample;
    }

    /// <summary>How much room <see cref="Draw"/> will take with the same arguments.</summary>
    public Vector2 Measure(bool compact, float scale, bool asConfigured = false)
    {
        var style = StyleFor(compact, scale, asConfigured);
        var renderer = compact ? compactRenderer : fullRenderer;
        return new Vector2(WidthFor(compact, scale), renderer.MeasureHeight(style));
    }

    /// <param name="compact">The compact strip rather than the full panel.</param>
    /// <param name="mistakes">Play it with a wrong press and a forgotten weave.</param>
    /// <param name="scale">The UI scale.</param>
    /// <param name="asConfigured">
    /// Draw it the way the player has set their timeline up - parts, key caps and all - instead of
    /// the pared-down look used next to an explanation.
    /// </param>
    public void Draw(bool compact, bool mistakes, float scale, bool asConfigured = false)
    {
        EnsureOpener();
        if (actions.Count == 0)
            return;

        var now = ImGui.GetTime();
        Advance(now, mistakes);

        var style = StyleFor(compact, scale, asConfigured);
        var renderer = compact ? compactRenderer : fullRenderer;
        var height = renderer.MeasureHeight(style);
        var width = WidthFor(compact, scale);
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0f, 4f * scale);

        builder ??= new TimelineFrameBuilder(host.Catalog);
        frame.RunId = runId;
        frame.Title = "Example";
        frame.JobLabel = string.Empty;
        frame.Opacity = 1f;
        frame.Unlocked = false;
        frame.Countdown = -1f;
        frame.CastProgress = -1f;

        // Keys belong to the real timeline; next to an explanation they would only be noise.
        Func<int, bool, KeyHint>? hints = asConfigured ? host.Trainer.HintFor : null;
        builder.Build(frame, actions, session, hints, now, host.Trainer.Speed, host.Settings.CountPrecast);

        renderer.Render(ImGui.GetWindowDrawList(), pos, new Vector2(width, height), frame, style, host, ImGui.GetIO().DeltaTime, now);
        ImGui.Dummy(new Vector2(width, height + 12f * scale));
    }

    private TimelineStyle StyleFor(bool compact, float scale, bool asConfigured)
    {
        var settings = host.Settings;
        var style = settings.ToStyle(scale * (compact ? 1f : 0.82f), compact);
        style.Blur = 0f; // it sits inside a window, there is nothing to blur
        if (asConfigured)
            return style;

        style.PanelOpacity = 0.55f;
        style.ShowLaneLabels = false;
        style.ShowHints = false;
        style.ShowRuler = !compact;
        style.Feedback = settings.MistakeFeedback == FeedbackStyle.Off ? FeedbackStyle.Gentle : settings.MistakeFeedback;
        return style;
    }

    private static float WidthFor(bool compact, float scale)
    {
        return MathF.Min(ImGui.GetContentRegionAvail().X - 4f * scale, (compact ? 420f : 760f) * scale);
    }

    private void EnsureOpener()
    {
        // Use what is on the player's own timeline when there is something; otherwise any opener.
        IReadOnlyList<int> source = host.Trainer.ActiveActions;
        if (source.Count == 0)
        {
            var first = host.Library.All.FirstOrDefault();
            if (first.Name is { Length: > 0 } && host.Library.TryGet(first, out var loaded))
                source = loaded;
        }

        var hash = source.Count;
        foreach (var id in source)
            hash = HashCode.Combine(hash, id);
        if (hash == sampleHash && actions.Count > 0)
            return;

        sampleHash = hash;
        actions = source.Take(14).ToList();
        sample = TimelineFrameBuilder.Describe(host.Catalog, actions);
        session = null;
    }

    private void Advance(double now, bool mistakes)
    {
        if (session == null || now - startedAt > actions.Count * StepSeconds + PauseSeconds)
        {
            session = new TrainerSession(actions, new ActionMatcher(host.Catalog.GetName));
            startedAt = now;
            played = 0;
            runId++;
        }

        // Press the next button whenever its moment has come.
        while (played < actions.Count && now - startedAt >= (played + 1) * StepSeconds)
        {
            var index = played++;
            var when = startedAt + (index + 1) * StepSeconds;

            if (mistakes && index == 6 && actions.Count > 9)
                continue; // "forget" this one; the next press shows it as skipped

            if (mistakes && index == 3)
            {
                var wrong = WrongActionFor(index);
                if (wrong != 0)
                    session.OnAction(wrong, false, false, when - StepSeconds * 0.55d);
            }

            // A potion step is satisfied by an item, everything else by an action.
            var isItem = actions[index] == SpecialActions.Potion;
            session.OnAction(isItem ? 1u : StepAction(index), isItem, false, when);
        }
    }

    /// <summary>An action that satisfies a step (groups are satisfied by their first member).</summary>
    private uint StepAction(int index)
    {
        var id = actions[index];
        if (id > 0)
            return (uint)id;
        return GroupOfActions.TryGetDefault(id, out var group) && group.Actions.Count > 0 ? group.Actions[0] : 1u;
    }

    /// <summary>Something from later in the opener that the trainer will not mistake for a skip.</summary>
    private uint WrongActionFor(int index)
    {
        var matcher = new ActionMatcher(host.Catalog.GetName);
        for (var j = actions.Count - 1; j > index + 2; j--)
        {
            var candidate = StepAction(j);
            var clashes = false;
            for (var k = index; k <= Math.Min(actions.Count - 1, index + 2); k++)
                clashes |= matcher.Matches(actions[k], candidate);
            if (!clashes)
                return candidate;
        }

        return 0;
    }
}
