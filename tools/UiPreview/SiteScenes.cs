using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Rehearsal.Data;
using Rehearsal.Training;
using Rehearsal.UI;
using Rehearsal.UI.Timeline;
using Rehearsal.UI.Views;

namespace Rehearsal.Preview;

/// <summary>
/// The pictures on the product page (docs/img): the same drawing code as everywhere else, without
/// captions, over painted scenery instead of a screenshot so nothing of anyone's game is in them.
/// </summary>
internal static class SiteScenes
{
    private const double Start = 100d;

    /// <summary>Which part of the scenery sits behind the timeline shots; the same for all, so the flip-book does not flicker.</summary>
    private static readonly Point TimelineOrigin = new(260, 420);

    private static readonly Point WindowOrigin = new(120, 60);

    public static void Render(PreviewHost host, string outDir)
    {
        Directory.CreateDirectory(outDir);
        using var scenery = PaintScenery(3000, 2000);
        var stage = new Stage(host, scenery);
        var catalog = host.Catalog;

        void Save(Canvas canvas, string name)
        {
            var path = Path.Combine(outDir, name);
            canvas.Save(path);
            Console.WriteLine($"wrote {path}  ({canvas.Width}x{canvas.Height})");
        }

        var full = TimelineStyle.Default with { Scale = stage.Scale };
        var compact = TimelineStyle.DefaultCompact with { Scale = stage.Scale };
        var rdm = Get(host, Jobs.RDM);
        var blm = Get(host, Jobs.BLM);
        var dnc = Get(host, Jobs.DNC);

        // ---- The strip, playing: one frame per press, stacked into a sprite sheet ----
        const int slipAt = 4;
        var frames = new List<Canvas> { Timeline(stage, 440, rdm, "RDM", compact, _ => Start) };
        for (var pressed = 1; pressed <= 12; pressed++)
        {
            var count = pressed;
            frames.Add(Timeline(stage, 440, rdm, "RDM", compact, session => Play(session, rdm, catalog, count, slipAt)));
            if (pressed == slipAt)
                frames.Add(Timeline(stage, 440, rdm, "RDM", compact, session => Play(session, rdm, catalog, count, slipAt, wrongNow: true)));
        }

        Save(Stage.Stack(frames), "strip-run.jpg");
        Console.WriteLine($"strip-run: {frames.Count} frames of {frames[0].Width}x{frames[0].Height}");

        // ---- The strip, still ----
        Save(frames[0], "strip-ready.jpg");
        Save(Timeline(stage, 440, blm, "BLM", compact, session => Play(session, blm, catalog, 8, 5, 6, true)), "strip-slip.jpg");
        Save(Timeline(stage, 380, dnc, "DNC", compact with { ShowHints = false, ShowStatus = false, IconSize = 34f },
                      session => Play(session, dnc, catalog, 3)), "strip-bare.jpg");
        Save(Timeline(stage, 440, rdm, "RDM", compact, _ => Start, frame =>
        {
            frame.Unlocked = true;
            frame.Controls = true;
            frame.HoveredControl = 0;
        }), "strip-buttons.jpg");
        Save(Timeline(stage, 440, rdm, "RDM", compact, _ => Start, frame => frame.Countdown = 4.2f), "strip-countdown.jpg");

        // What a link to the page shows when it is pasted into a chat: the strip, a few presses in.
        Save(Timeline(stage, 700, rdm, "RDM", compact, session => Play(session, rdm, catalog, 3), canvas: new Size(1200, 630)), "og.jpg");

        // ---- The full panel ----
        Save(Timeline(stage, 1120, blm, "BLM", full, session => Play(session, blm, catalog, 8, 5, 6, true), frame =>
        {
            frame.CastProgress = 0.62f;
            frame.CastMatches = true;
        }, "Standard (The Balance)"), "panel-running.jpg");

        // ---- Minimized ----
        Save(Tabs(stage, blm, compact), "tabs.jpg");

        // ---- The window ----
        var view = WindowScenes.Prepare(host, 100);
        var size = new Vector2(MathF.Round(900f * host.Scale), MathF.Round(600f * host.Scale));
        var margin = (int)MathF.Round(26f * host.Scale);

        Canvas Window(MainView content, int count = 6, Func<int, float>? delta = null)
        {
            return stage.Render((int)size.X + margin * 2, (int)size.Y + margin * 2, WindowOrigin,
                                () => WindowScenes.DrawWindow(host, content, new Vector2(margin), size), count, delta: delta);
        }

        for (var step = 0; step < SetupView.StepCount; step++)
        {
            // A window of its own each time, so the example timeline starts from the top for every shot.
            var fresh = new MainView(host);
            fresh.ShowSetup(step);
            Save(Window(fresh, 18, WindowScenes.JumpAhead(step == 2 ? 3.2f : 6.2f)), $"setup-{step + 1}.jpg");
        }

        host.Settings.OverlayLocked = true; // the last step borrowed it

        view.Show(MainView.Section.Openers);
        Save(Window(view), "openers.jpg");
        view.Edit(new OpenerRef(Jobs.RDM, "My opener", false));
        Save(Window(view), "editor.jpg");
        view.Show(MainView.Section.Results);
        Save(Window(view), "results.jpg");
        view.ShowSettings(SettingsView.Page.Timeline);
        Save(Window(view), "settings.jpg");
        view.ShowGuide(3);
        Save(Window(view), "guide.jpg");

        Save(Icon(stage), "icon.png");
    }

    /// <summary>A dusk sky of soft colour: enough going on behind a panel to show that it is translucent.</summary>
    private static Bitmap PaintScenery(int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var sky = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(0x16, 0x22, 0x38), Color.FromArgb(0x2A, 0x1E, 0x3D), 55f))
            g.FillRectangle(sky, 0, 0, width, height);

        void Glow(float x, float y, float radius, Color color, int alpha)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(x - radius, y - radius, radius * 2f, radius * 2f);
            using var brush = new PathGradientBrush(path);
            brush.CenterColor = Color.FromArgb(alpha, color);
            brush.SurroundColors = [Color.FromArgb(0, color)];
            g.FillPath(brush, path);
        }

        // Laid out by hand rather than at random, so every render gives the same picture.
        Glow(width * 0.16f, height * 0.22f, 620f, Color.FromArgb(0x3F, 0xB8, 0xC9), 120);
        Glow(width * 0.52f, height * 0.38f, 760f, Color.FromArgb(0x7A, 0x5C, 0xE0), 110);
        Glow(width * 0.86f, height * 0.20f, 560f, Color.FromArgb(0xE8, 0x7C, 0xA8), 95);
        Glow(width * 0.30f, height * 0.78f, 700f, Color.FromArgb(0xF0, 0xA8, 0x5C), 80);
        Glow(width * 0.74f, height * 0.80f, 680f, Color.FromArgb(0x4C, 0xC9, 0x9A), 85);
        Glow(width * 0.06f, height * 0.58f, 420f, Color.FromArgb(0x5B, 0x8F, 0xF0), 90);

        // Small bright points: these are what the blur visibly softens.
        Span<(float X, float Y, float R)> lights =
        [
            (0.10f, 0.26f, 26f), (0.14f, 0.31f, 14f), (0.19f, 0.24f, 34f), (0.24f, 0.29f, 18f), (0.30f, 0.23f, 22f), (0.36f, 0.30f, 30f),
            (0.12f, 0.40f, 20f), (0.22f, 0.44f, 28f), (0.31f, 0.41f, 16f), (0.40f, 0.45f, 24f), (0.47f, 0.27f, 18f), (0.52f, 0.42f, 32f),
            (0.09f, 0.52f, 30f), (0.17f, 0.57f, 16f), (0.27f, 0.54f, 24f), (0.35f, 0.58f, 20f), (0.44f, 0.55f, 28f), (0.58f, 0.31f, 22f)
        ];
        foreach (var (x, y, r) in lights)
        {
            Glow(width * x, height * y, r * 3.2f, Color.FromArgb(0xFF, 0xE9, 0xC2), 70);
            Glow(width * x, height * y, r, Color.FromArgb(0xFF, 0xF6, 0xE6), 150);
        }

        return bitmap;
    }

    private static List<int> Get(PreviewHost host, Jobs job)
    {
        return host.Library.TryGet(new OpenerRef(job, "Standard (The Balance)", true), out var actions)
                   ? actions
                   : throw new InvalidOperationException($"Opener not found: {job}");
    }

    /// <summary>One timeline in a given state, with a margin of scenery around it.</summary>
    private static Canvas Timeline(
        Stage stage, int width, List<int> actions, string job, TimelineStyle style, Func<TrainerSession, double> play,
        Action<TimelineFrame>? tweak = null, string title = "Standard", Size? canvas = null)
    {
        var catalog = stage.Host.Catalog;
        var session = new TrainerSession(actions, new ActionMatcher(catalog.GetName));
        var now = play(session);

        var frame = new TimelineFrame { Title = title, JobLabel = job, RunId = 1 };
        var hints = new FakeHints(actions, false);
        new TimelineFrameBuilder(catalog).Build(frame, actions, session, hints.For, now);
        tweak?.Invoke(frame);

        var renderer = new TimelineRenderer();
        var height = renderer.MeasureHeight(style);
        var margin = (int)MathF.Round(30f * stage.Scale);
        var overlay = (int)MathF.Round(width * stage.Scale);

        // By default the picture is the timeline plus a margin; a fixed canvas gets it in the middle.
        var size = canvas ?? new Size(overlay + margin * 2, (int)height + margin * 2);
        var pos = new Vector2(MathF.Round((size.Width - overlay) * 0.5f), MathF.Round((size.Height - height) * 0.5f));
        return stage.Render(size.Width, size.Height, TimelineOrigin, () =>
        {
            renderer.Render(ImGui.GetBackgroundDrawList(), pos, new Vector2(overlay, height), frame, style, stage.Host, 1f / 60f, 0.4);
        });
    }

    /// <summary>
    /// Presses the first buttons of an opener in order.
    /// </summary>
    /// <param name="count">How many steps have been dealt with.</param>
    /// <param name="slipAt">The step at which something else was pressed first; negative for none.</param>
    /// <param name="skipAt">A step that was forgotten; negative for none.</param>
    /// <param name="wrongNow">Something wrong was pressed a moment ago and has not been corrected yet.</param>
    private static double Play(
        TrainerSession session, List<int> actions, ActionCatalog catalog, int count, int slipAt = -1, int skipAt = -1, bool wrongNow = false)
    {
        var t = Start;
        for (var i = 0; i < count && i < actions.Count; i++)
        {
            if (i == skipAt)
                continue;

            if (i == slipAt)
                session.OnAction(Wrong(actions, catalog, i), false, false, t - 0.4);

            Press(session, actions[i], t);
            t += actions[i] > 0 && !catalog.IsGcd(actions[i]) ? 0.7 : 2.4;
        }

        if (wrongNow && count < actions.Count)
            session.OnAction(Wrong(actions, catalog, count), false, false, t - 0.25);
        return t;
    }

    private static void Press(TrainerSession session, int id, double when)
    {
        if (id == SpecialActions.Potion)
            session.OnAction(1u, true, false, when);
        else if (id > 0)
            session.OnAction((uint)id, false, false, when);
        else
            session.OnAction(GroupOfActions.TryGetDefault(id, out var group) && group.Actions.Count > 0 ? group.Actions[0] : 1u, false, false, when);
    }

    /// <summary>Something from later in the opener that cannot be mistaken for a skipped step.</summary>
    private static uint Wrong(List<int> actions, ActionCatalog catalog, int index)
    {
        var matcher = new ActionMatcher(catalog.GetName);
        for (var j = actions.Count - 1; j > index + 2; j--)
        {
            if (actions[j] <= 0)
                continue;

            var clashes = false;
            for (var k = index; k <= Math.Min(actions.Count - 1, index + 2); k++)
                clashes |= matcher.Matches(actions[k], (uint)actions[j]);
            if (!clashes)
                return (uint)actions[j];
        }

        return 7u;
    }

    /// <summary>The tab the timeline minimizes to, in the states it goes through.</summary>
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

        (TimelineFrame Frame, bool Hovered)[] tabs =
        [
            (Frame(_ => Start), false),
            (Frame(_ => Start, frame => frame.Countdown = 4.2f), false),
            (Frame(session => Play(session, actions, catalog, 9)), true),
            (Frame(session => Play(session, actions, catalog, actions.Count)), false)
        ];

        var margin = (int)MathF.Round(30f * stage.Scale);
        var gap = MathF.Round(18f * stage.Scale);
        var sizes = new Vector2[tabs.Length];
        Canvas? canvas = null;

        // Two passes: text can only be measured inside a frame, and the canvas has to be sized before it.
        for (var pass = 0; pass < 2; pass++)
        {
            var total = 0f;
            foreach (var size in sizes)
                total += size.X + gap;

            var width = pass == 0 ? 800 : (int)MathF.Round(total - gap) + margin * 2;
            var height = (int)MathF.Round(24f * stage.Scale) + margin * 2;
            canvas = stage.Render(width, height, TimelineOrigin, () =>
            {
                var dl = ImGui.GetBackgroundDrawList();
                var x = (float)margin;
                for (var i = 0; i < tabs.Length; i++)
                {
                    sizes[i] = TimelineTab.Measure(tabs[i].Frame, style, stage.Host);
                    TimelineTab.Render(dl, new Vector2(x, margin), sizes[i], tabs[i].Frame, style, stage.Host, tabs[i].Hovered, 0.4);
                    x += sizes[i].X + gap;
                }
            });
        }

        return canvas!;
    }

    /// <summary>The plugin's icon: the strip reduced to its idea. Three steps on a line, one woven in, the first one lit.</summary>
    private static Canvas Icon(Stage stage)
    {
        const int size = 512;
        return stage.Render(size, size, new Point(0, 0), () =>
        {
            var dl = ImGui.GetBackgroundDrawList();
            var accent = Palette.DefaultAccent;
            dl.AddRectFilledMultiColor(Vector2.Zero, new Vector2(size), Palette.Pack(Palette.Rgb(0x1A, 0x22, 0x33)), Palette.Pack(Palette.Rgb(0x14, 0x1A, 0x28)),
                                       Palette.Pack(Palette.Rgb(0x0E, 0x12, 0x1B)), Palette.Pack(Palette.Rgb(0x12, 0x17, 0x24)));

            const float gcd = 124f;
            const float off = 88f;
            const float gap = 26f;
            const float railY = 284f;
            var left = (size - (gcd * 3f + gap * 2f)) * 0.5f;

            dl.AddLine(new Vector2(44f, railY), new Vector2(size - 44f, railY), Palette.Pack(Palette.White, 0.14f), 6f);

            for (var i = 0; i < 3; i++)
            {
                var min = new Vector2(left + i * (gcd + gap), railY - gcd * 0.5f);
                var max = min + new Vector2(gcd);
                if (i == 0)
                {
                    for (var ring = 5; ring >= 1; ring--)
                        dl.AddRect(min - new Vector2(ring * 5f), max + new Vector2(ring * 5f), Palette.Pack(accent, 0.07f * (6 - ring)), 30f + ring * 5f, 0, 7f);
                    dl.AddRectFilled(min, max, Palette.Pack(accent), 28f);
                }
                else
                {
                    dl.AddRectFilled(min, max, Palette.Pack(Palette.Rgb(0x2B, 0x35, 0x49)), 28f);
                    dl.AddRect(min, max, Palette.Pack(Palette.White, i == 1 ? 0.42f : 0.22f), 28f, 0, 5f);
                }
            }

            // The woven ability: smaller, raised, between the second and third.
            var weaveCenter = left + gcd * 2f + gap * 1.5f;
            var weaveMin = new Vector2(weaveCenter - off * 0.5f, railY - gcd * 0.5f - off - 18f);
            dl.AddRectFilled(weaveMin, weaveMin + new Vector2(off), Palette.Pack(Palette.TwoMinute), 22f);

            // And the key cap under the action to press.
            var capMin = new Vector2(left + gcd * 0.5f - 34f, railY + gcd * 0.5f + 26f);
            dl.AddRectFilled(capMin, capMin + new Vector2(68f, 44f), Palette.Pack(accent, 0.9f), 13f);
            for (var i = 1; i < 3; i++)
            {
                var min = new Vector2(left + i * (gcd + gap) + gcd * 0.5f - 34f, railY + gcd * 0.5f + 26f);
                dl.AddRectFilled(min, min + new Vector2(68f, 44f), Palette.Pack(Palette.White, 0.12f), 13f);
            }
        });
    }
}
