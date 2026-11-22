using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Rehearsal.Data;
using Rehearsal.Training;
using Rehearsal.UI;
using Rehearsal.UI.Timeline;
using Rehearsal.UI.Views;

namespace Rehearsal.Preview;

/// <summary>The live timeline in its different states and configurations.</summary>
internal static class TimelineScenes
{
    private const double Start = 100d;

    public static void Render(Stage stage, string path)
    {
        var rows = new List<Canvas>();
        var scale = stage.Scale;
        var catalog = stage.Host.Catalog;
        var full = TimelineStyle.Default with { Scale = scale };
        var compact = TimelineStyle.DefaultCompact with { Scale = scale };

        var blm = Get(stage, Jobs.BLM, "Standard (The Balance)");
        var dnc = Get(stage, Jobs.DNC, "Standard (The Balance)");
        var mch = Get(stage, Jobs.MCH, "Standard (The Balance)");
        var rdm = Get(stage, Jobs.RDM, "Standard (The Balance)");
        var rdm90 = catalog.Adapt(rdm, 90).Actions;

        // 1. Waiting for the pull: ruler under the GCDs, "GCD n" above the two minute cooldowns.
        rows.Add(Scene(stage, "Full panel, armed: GCD numbers + estimated time, burst notes", new Point(250, 560), 1180, rdm90, "RDM",
                       "Standard (The Balance)  -  Lv 90", full, _ => Start, null));

        // 2. Gentle feedback: a slip that was corrected, a skipped weave, a cast, and a slip just now.
        rows.Add(Scene(stage, "Running, gentle cue: wrong press 0.25s ago, earlier slip + skipped weave marked quietly", new Point(250, 560),
                       1180, blm, "BLM", "Standard (The Balance)", full, session => PlayMessy(session, blm, catalog), frame =>
                       {
                           frame.CastProgress = 0.62f;
                           frame.CastMatches = true;
                       }));

        // 3. The same moment with the loud style, for comparison.
        rows.Add(Scene(stage, "Same moment, strong cue", new Point(250, 560), 1180, blm, "BLM", "Standard (The Balance)",
                       full with { Feedback = FeedbackStyle.Strong }, session => PlayMessy(session, blm, catalog), frame =>
                       {
                           frame.CastProgress = 0.62f;
                           frame.CastMatches = true;
                       }));

        // 4. Finished with mistakes, gentle style: back to full brightness for review.
        rows.Add(Scene(stage, "Finished with mistakes (gentle): review", new Point(250, 560), 1180, blm, "BLM", "Standard (The Balance)", full,
                       session =>
                       {
                           var t = Start;
                           for (var i = 0; i < blm.Count; i++)
                           {
                               if (i == 3)
                                   session.OnAction(154, false, false, t - 0.4);
                               if (i != 7)
                                   session.OnAction((uint)blm[i], false, false, t);
                               t += catalog.IsGcd(blm[i]) ? 2.4 : 0.7;
                           }

                           return t + 2;
                       }, null));

        // 5. Compact strip, waiting.
        rows.Add(Scene(stage, "Compact strip, armed (the action to press always sits in the same spot)", new Point(520, 610), 430, rdm90, "RDM",
                       "Standard", compact, _ => Start, null));

        // 6. Compact strip mid-run with a fresh slip.
        rows.Add(Scene(stage, "Compact strip, running, wrong press just now", new Point(520, 610), 430, blm, "BLM", "Standard", compact,
                       session => PlayMessy(session, blm, catalog), null));

        // 7. Compact strip without key caps or status line: the smallest it gets.
        rows.Add(Scene(stage, "Compact strip, no key caps, no status line", new Point(520, 300), 360, mch, "MCH", "Standard",
                       compact with { ShowHints = false, ShowStatus = false, IconSize = 34f }, session =>
                       {
                           var t = Start;
                           for (var i = 0; i < 5; i++)
                           {
                               session.OnAction((uint)mch[i], false, false, t);
                               t += 1.2;
                           }

                           return t;
                       }, null));

        // 8. Flat full layout, countdown running.
        rows.Add(Scene(stage, "Full panel, flat row, countdown running", new Point(560, 60), 1180, dnc, "DNC", "Standard (The Balance)",
                       full with { Layout = TimelineLayoutMode.Inline }, _ => Start, frame => frame.Countdown = 4.2f));

        // 9. Unlocked, narrow, gamepad hints: the minimize and hide buttons sit at the end of the header.
        rows.Add(Scene(stage, "Unlocked, narrow, gamepad hints, with the minimize / hide buttons", new Point(700, 560), 760, dnc, "DNC",
                       "Standard (The Balance)", full with { HintStyle = KeybindStyle.Compact }, _ => Start, frame =>
                       {
                           frame.Unlocked = true;
                           frame.Controls = true;
                       }, true));

        // 10. Locked but reached with Ctrl+Shift, mouse over "hide"; no header, so the buttons take the corner.
        rows.Add(Scene(stage, "Locked, reached with Ctrl+Shift, no header: buttons in the corner (hide hovered)", new Point(250, 560), 900,
                       rdm90, "RDM", "Standard", full with { ShowHeader = false }, _ => Start, frame =>
                       {
                           frame.Controls = true;
                           frame.HoveredControl = 1;
                       }));

        // 11. The compact strip with its buttons, mouse over "minimize".
        rows.Add(Scene(stage, "Compact strip, unlocked: buttons on the status line (minimize hovered)", new Point(520, 610), 430, rdm90, "RDM",
                       "Standard", compact, _ => Start, frame =>
                       {
                           frame.Unlocked = true;
                           frame.Controls = true;
                           frame.HoveredControl = 0;
                       }));

        // 12. Minimized: the tab in every state it can be in.
        rows.Add(Tabs(stage, blm, compact));

        Stage.Stack(rows).Save(path);
        Console.WriteLine($"wrote {path}");
    }

    /// <summary>The minimized tab, once per state, side by side.</summary>
    private static Canvas Tabs(Stage stage, List<int> actions, TimelineStyle style)
    {
        var catalog = stage.Host.Catalog;

        TimelineFrame Frame(Func<TrainerSession, double> play, Action<TimelineFrame>? tweak = null)
        {
            var session = new TrainerSession(actions, new ActionMatcher(catalog.GetName));
            var now = play(session);
            var frame = new TimelineFrame { JobLabel = "BLM", RunId = 1 };
            new TimelineFrameBuilder(catalog).Build(frame, actions, session, null, now);
            tweak?.Invoke(frame);
            return frame;
        }

        double PlayAll(TrainerSession session)
        {
            var t = Start;
            foreach (var action in actions)
            {
                session.OnAction((uint)action, false, false, t);
                t += 1.2;
            }

            return t;
        }

        (string Label, TimelineFrame Frame, bool Hovered)[] tabs =
        [
            ("waiting", Frame(_ => Start), false),
            ("countdown", Frame(_ => Start, frame => frame.Countdown = 4.2f), false),
            ("running", Frame(session => PlayMessy(session, actions, catalog)), false),
            ("clean run", Frame(PlayAll), false),
            ("unlocked, hovered", Frame(_ => Start, frame => frame.Unlocked = true), true),
            ("no opener", Frame(_ => Start, frame =>
            {
                frame.Steps.Clear();
                frame.JobLabel = string.Empty;
            }), false)
        ];

        var margin = (int)MathF.Round(34f * stage.Scale);
        var height = (int)MathF.Round(24f * stage.Scale) + margin * 2 + (int)MathF.Round(18f * stage.Scale);
        return stage.Render(1280, height, new Point(520, 610), () =>
        {
            var dl = ImGui.GetBackgroundDrawList();
            dl.AddText(new Vector2(14f, 6f), Palette.Pack(Palette.White, 0.85f), "Minimized: the tab that stays behind (click to bring the timeline back)");

            var x = 40f;
            foreach (var (label, frame, hovered) in tabs)
            {
                var size = TimelineTab.Measure(frame, style, stage.Host);
                var pos = new Vector2(x, margin);
                TimelineTab.Render(dl, pos, size, frame, style, stage.Host, hovered, 0.4);
                dl.AddText(new Vector2(x, margin + size.Y + 6f), Palette.Pack(Palette.White, 0.6f), label);
                x += MathF.Max(size.X, ImGui.CalcTextSize(label).X) + 28f * stage.Scale;
            }
        });
    }

    /// <summary>Nine steps in: one slip that was corrected, one weave forgotten, and a wrong press a moment ago.</summary>
    private static double PlayMessy(TrainerSession session, List<int> actions, ActionCatalog catalog)
    {
        var t = Start;
        for (var i = 0; i < 9; i++)
        {
            if (i == 7)
                session.OnAction(154, false, false, t - 0.4); // Blizzard III first, then the right button
            if (i == 6)
                continue; // forgot this one
            session.OnAction((uint)actions[i], false, false, t);
            t += catalog.IsGcd(actions[i]) ? 2.4 : 0.7;
        }

        session.OnAction(3576, false, false, t - 0.25); // Blizzard IV, just now
        return t;
    }

    private static List<int> Get(Stage stage, Jobs job, string name)
    {
        return stage.Host.Library.TryGet(new OpenerRef(job, name, true), out var actions)
                   ? actions
                   : throw new InvalidOperationException($"Opener not found: {job} {name}");
    }

    private static Canvas Scene(
        Stage stage, string caption, Point backgroundOrigin, int overlayWidth, List<int> actions, string job, string title,
        TimelineStyle style, Func<TrainerSession, double> play, Action<TimelineFrame>? tweak, bool gamepad = false)
    {
        var catalog = stage.Host.Catalog;
        var session = new TrainerSession(actions, new ActionMatcher(catalog.GetName));
        var now = play(session);

        var frame = new TimelineFrame { Title = title, JobLabel = job, RunId = 1 };
        var hints = new FakeHints(actions, gamepad);
        new TimelineFrameBuilder(catalog).Build(frame, actions, session, hints.For, now);
        tweak?.Invoke(frame);

        var renderer = new TimelineRenderer();
        var height = renderer.MeasureHeight(style);
        var margin = (int)MathF.Round(34f * stage.Scale);
        const int canvasWidth = 1280;
        var canvasHeight = (int)height + margin * 2;

        return stage.Render(canvasWidth, canvasHeight, backgroundOrigin, () =>
        {
            var dl = ImGui.GetBackgroundDrawList();
            dl.AddText(new Vector2(14f, 6f), Palette.Pack(Palette.White, 0.85f), caption);
            var pos = new Vector2(MathF.Round((canvasWidth - overlayWidth) * 0.5f), margin);
            renderer.Render(dl, pos, new Vector2(overlayWidth, height), frame, style, stage.Host, 1f / 60f, 0.4);
        });
    }
}

/// <summary>The main window, one image per page, plus the timeline's menu.</summary>
internal static class WindowScenes
{
    /// <summary>A few frames to let the layout settle, one long one, then enough short ones for animations to come to rest.</summary>
    public static Func<int, float> JumpAhead(float seconds)
    {
        return frame => frame == 3 ? seconds : 1f / 60f;
    }

    /// <summary>A main window with something in every tab: a red mage with a saved opener and a few attempts behind them.</summary>
    /// <param name="host">The application.</param>
    /// <param name="level">The level they are playing at; below the cap the opener shows fitted to it.</param>
    public static MainView Prepare(PreviewHost host, int level)
    {
        var catalog = host.Catalog;

        var trainer = new FakeTrainer(catalog, host.Library) { Level = level };
        host.FakeTrainer = trainer;
        host.CurrentJob = Jobs.RDM;

        // One opener of the player's own, to show how saved entries look (a potion and a wildcard included).
        host.Library.Save(level < catalog.MaxLevel ? "Level 90 opener (mine)" : "My opener", Jobs.RDM,
                          [25856, 25855, 7561, 25855, 7517, -100, 7518, 0, 25855, 7520, 7521, 7527, 7528]);
        trainer.Select(new OpenerRef(Jobs.RDM, "Standard (The Balance)", true));

        // A few attempts of varying quality for the results view.
        trainer.Simulate(26, (session, actions) => Play(session, actions, catalog, actions.Count, null));
        trainer.Simulate(19, (session, actions) => Play(session, actions, catalog, 11, null), FinishReason.TimedOut);
        trainer.Simulate(11, (session, actions) => Play(session, actions, catalog, actions.Count, (i, t) =>
        {
            if (i == 4)
                session.OnAction(7510, false, false, t - 0.3);
            return i != 8;
        }));
        trainer.Simulate(3, (session, actions) => Play(session, actions, catalog, actions.Count, (i, t) =>
        {
            if (i == 12)
                session.OnAction(7511, false, false, t - 0.3);
            if (i == 16)
                session.OnAction(7510, false, false, t - 0.3);
            return i != 7 && i != 13;
        }));

        return new MainView(host);
    }

    public static void Render(Stage stage, string outDir)
    {
        var host = stage.Host;
        var scale = host.Scale;
        var view = Prepare(host, 90);
        var size = new Vector2(MathF.Round(940f * scale), MathF.Round(640f * scale));
        const int margin = 28;
        var width = (int)size.X + margin * 2;
        var height = (int)size.Y + margin * 2;

        void Page(string name, Action prepare, int frames = 4, MainView? window = null, Func<int, float>? delta = null)
        {
            prepare();
            var content = window ?? view;
            var canvas = stage.Render(width, height, new Point(330, 150), () => DrawWindow(host, content, new Vector2(margin), size), frames,
                                      delta: delta);
            var path = Path.Combine(outDir, $"window-{name}.png");
            canvas.Save(path);
            Console.WriteLine($"wrote {path}");
        }

        // The first-run setup, step by step, with its example timeline a few presses in. Each gets
        // a window of its own so the example starts from the top every time.
        string[] steps = ["view", "when", "mistakes", "look", "place"];
        for (var i = 0; i < steps.Length; i++)
        {
            var fresh = new MainView(host);
            var step = i;
            Page($"setup-{i + 1}-{steps[i]}", () => fresh.ShowSetup(step), 18, fresh, JumpAhead(i == 2 ? 3.2f : 6.2f));
        }

        // The same first step with the other two views chosen.
        var other = new MainView(host);
        host.Settings.View = TimelineView.Expanded;
        Page("setup-1-view-full", () => other.ShowSetup(), 18, other, JumpAhead(6.2f));
        other = new MainView(host);
        host.Settings.View = TimelineView.Automatic;
        Page("setup-1-view-automatic", () => other.ShowSetup(), 18, other, JumpAhead(6.2f));
        host.Settings.View = TimelineView.Compact;
        host.Settings.OverlayLocked = true; // the last step borrowed it


        Page("openers", () => view.Show(MainView.Section.Openers));
        Page("editor", () => view.Edit(new OpenerRef(Jobs.RDM, "Level 90 opener (mine)", false)));
        Page("results", () => view.Show(MainView.Section.Results));
        // Each settings page twice: as it opens, and with what is folded away on it opened up.
        Page("settings-timeline", () => view.ShowSettings(SettingsView.Page.Timeline));
        Page("settings-behaviour", () => view.ShowSettings(SettingsView.Page.Behaviour));
        Page("settings-appearance", () => view.ShowSettings(SettingsView.Page.Appearance));
        Page("settings-timeline-unfolded", () => view.ShowSettings(SettingsView.Page.Timeline, true));
        Page("settings-behaviour-unfolded", () => view.ShowSettings(SettingsView.Page.Behaviour, true));
        Page("settings-appearance-unfolded", () => view.ShowSettings(SettingsView.Page.Appearance, true));
        Page("guide-1-idea", () => view.ShowGuide(0), 12);
        Page("guide-3-placing", () => view.ShowGuide(2), 12);
        Page("guide-4-reading", () => view.ShowGuide(3), 6);
        Page("guide-5-practising", () => view.ShowGuide(4), 12);
        Page("guide-7-levels", () => view.ShowGuide(6), 6);
        Page("guide-8-commands", () => view.ShowGuide(7), 6);

        // The top bar with the timeline minimized, to see the switch in another position.
        host.Settings.Presence = TimelinePresence.Minimized;
        Page("openers-minimized", () => view.Show(MainView.Section.Openers));
        host.Settings.Presence = TimelinePresence.Shown;

        RenderMenu(stage, host, outDir, OverlayMenu.Origin.FullPanel, "menu");
        RenderMenu(stage, host, outDir, OverlayMenu.Origin.CompactStrip, "menu-compact");
        RenderMenu(stage, host, outDir, OverlayMenu.Origin.Tab, "menu-tab");
    }

    /// <summary>The right-click menu, as it opens from the full panel, the compact strip or the minimized tab.</summary>
    public static void RenderMenu(Stage stage, PreviewHost host, string outDir, OverlayMenu.Origin origin, string name)
    {
        var frameIndex = 0;
        var canvas = stage.Render(520, 520, new Point(600, 400), () =>
        {
            using var theme = Theme.Push(host.Settings.Accent, 0.96f, host.Scale);
            ImGui.SetNextWindowPos(new Vector2(20f, 20f));
            ImGui.SetNextWindowSize(new Vector2(200f, 60f));
            if (ImGui.Begin("##menuAnchor", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings))
            {
                if (frameIndex++ == 0)
                    ImGui.OpenPopup("##overlayMenu");

                ImGui.SetNextWindowPos(new Vector2(24f, 24f));
                if (ImGui.BeginPopup("##overlayMenu"))
                {
                    OverlayMenu.Draw(host, origin, _ => { }, _ => { });
                    ImGui.EndPopup();
                }
            }

            ImGui.End();
        }, 4);

        var path = Path.Combine(outDir, $"{name}.png");
        canvas.Save(path);
        Console.WriteLine($"wrote {path}");
    }

    public static void DrawWindow(PreviewHost host, MainView view, Vector2 pos, Vector2 size)
    {
        using var theme = Theme.Push(host.Settings.Accent, host.Settings.WindowOpacity, host.Scale);
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse
                                       | ImGuiWindowFlags.NoSavedSettings;
        if (ImGui.Begin(Branding.Name + "###main", flags))
        {
            var min = ImGui.GetWindowPos();
            host.DrawBackdrop(ImGui.GetWindowDrawList(), min, min + ImGui.GetWindowSize(), ImGui.GetStyle().WindowRounding, host.Settings.Blur,
                              1f);
            view.Draw();
        }

        ImGui.End();
    }

    /// <summary>Presses the opener's buttons in order. The callback can add wrong presses and skip steps.</summary>
    private static double Play(
        TrainerSession session, IReadOnlyList<int> actions, ActionCatalog catalog, int count, Func<int, double, bool>? before)
    {
        var t = 100d;
        for (var i = 0; i < count && i < actions.Count; i++)
        {
            var press = before?.Invoke(i, t) ?? true;
            if (press)
                session.OnAction((uint)actions[i], false, false, t);
            t += catalog.IsGcd(actions[i]) ? 2.4 : 0.7;
        }

        return t;
    }
}
