using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Rehearsal.Data;
using Rehearsal.UI.Timeline;

namespace Rehearsal.UI.Views;

/// <summary>
/// The walkthrough: a handful of short pages, each with the real thing drawn next to the words and
/// buttons that do what the page talks about.
/// </summary>
public sealed class GuideView(IAppHost host, Action<MainView.Section> open, Action<SettingsView.Page> openSettings, Action setup)
{
    private static readonly string[] Titles =
    [
        "The idea", "Pick an opener", "Place the timeline", "Reading it", "Practising", "When it shows", "Levels and old content",
        "Commands and credits"
    ];

    private readonly DemoTimeline demo = new(host);
    private int page;

    /// <summary>Jumps to a page of the walkthrough (zero-based).</summary>
    public void Show(int target)
    {
        page = Math.Clamp(target, 0, Titles.Length - 1);
    }

    public void Draw()
    {
        var scale = host.Scale;
        var accent = host.Settings.Accent;
        var navWidth = MathF.Round(210f * scale);

        // ---- Steps on the left ----
        if (ImGui.BeginChild("##guideNav", new Vector2(navWidth, 0f), false))
        {
            var dl = ImGui.GetWindowDrawList();
            for (var i = 0; i < Titles.Length; i++)
            {
                var min = ImGui.GetCursorScreenPos();
                var size = new Vector2(ImGui.GetContentRegionAvail().X, MathF.Round(36f * scale));
                if (ImGui.InvisibleButton($"##guide{i}", size))
                    page = i;

                var hovered = ImGui.IsItemHovered();
                var selected = i == page;
                if (selected || hovered)
                    dl.AddRectFilled(min, min + size, selected ? Palette.Pack(accent, 0.18f) : Palette.Pack(Palette.White, 0.06f), 7f * scale);

                var badge = min + new Vector2(18f * scale, size.Y * 0.5f);
                dl.AddCircleFilled(badge, 10f * scale, selected ? Palette.Pack(accent, 0.95f) : Palette.Pack(Palette.White, 0.10f), 20);
                var number = (i + 1).ToString();
                var numberSize = ImGui.CalcTextSize(number);
                dl.AddText(badge - numberSize * 0.5f, selected ? Palette.Pack(Palette.OnAccent) : Palette.Pack(Palette.White, 0.7f), number);

                var titleSize = ImGui.CalcTextSize(Titles[i]);
                dl.AddText(new Vector2(min.X + 38f * scale, min.Y + (size.Y - titleSize.Y) * 0.5f),
                           Palette.Pack(Palette.White, selected ? Palette.TextAlpha : Palette.TextDimAlpha), Titles[i]);
            }

            // The choices the first-run setup walked through can be gone through again.
            ImGui.Dummy(new Vector2(0f, 10f * scale));
            if (ImGui.Button("Run the setup again", new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
                setup();
            Widgets.Tooltip("The five first-run choices: view, when it appears, mistakes, look, and where it sits. "
                            + "Nothing is reset; it starts from what you have now.", scale);
        }

        ImGui.EndChild();
        ImGui.SameLine();

        // ---- The page ----
        if (ImGui.BeginChild("##guidePage", Vector2.Zero, false))
        {
            Widgets.SectionTitle($"{page + 1}.  {Titles[page]}", accent, scale);

            switch (page)
            {
                case 0:
                    DrawIdea(scale, accent);
                    break;
                case 1:
                    DrawPicking(scale, accent);
                    break;
                case 2:
                    DrawPlacing(scale, accent);
                    break;
                case 3:
                    DrawReading(scale, accent);
                    break;
                case 4:
                    DrawPractising(scale, accent);
                    break;
                case 5:
                    DrawVisibility(scale, accent);
                    break;
                case 6:
                    DrawLevels(scale, accent);
                    break;
                default:
                    DrawCommands(scale, accent);
                    break;
            }

            ImGui.Dummy(new Vector2(0f, 10f * scale));
            ImGui.BeginDisabled(page == 0);
            if (ImGui.Button("Back"))
                page--;
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (page < Titles.Length - 1)
            {
                if (Widgets.PrimaryButton("Next", accent))
                    page++;
            }
            else if (Widgets.PrimaryButton("Done - show me the openers", accent))
            {
                open(MainView.Section.Openers);
            }
        }

        ImGui.EndChild();
    }

    // ---- Pages ----

    private void DrawIdea(float scale, Vector3 accent)
    {
        Paragraph(Branding.Name + " puts your opener on screen as a single line and follows along while you play. "
                  + "It is meant to sit on your HUD like the game's own elements: no window, no buttons, nothing to click.");

        demo.Draw(false, true, scale);

        Bullet("The action to press next is ringed, and its key is filled in underneath.", accent, scale);
        Bullet("What you have done fades and slides away; a long opener scrolls to keep your place.", accent, scale);
        Bullet("There is nothing to start or stop. Press the opener's first action and the attempt begins.", accent, scale);
        Bullet("When you stop, the result stays up for a moment and then it gets ready for the next try.", accent, scale);
    }

    private void DrawPicking(float scale, Vector3 accent)
    {
        Paragraph("Each job starts with the opener published by The Balance, so on most jobs there is already something on "
                  + "the timeline. Some jobs have several; pick the one you want on the Openers tab. The choice is remembered "
                  + "per job, and the timeline follows you when you change job.");

        if (Widgets.PrimaryButton("Open the opener list", accent))
            open(MainView.Section.Openers);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        Paragraph("You can build your own in the Editor: search for an action, click it to add it, drag icons to reorder, "
                  + "right-click one to remove it. Two special steps help with openers that are not fully fixed:");
        Bullet("\"Any action\" accepts whatever you press - useful for a filler GCD that depends on procs.", accent, scale);
        Bullet("\"Potion\" is satisfied by using any item, so your tincture can be part of the sequence.", accent, scale);

        if (ImGui.Button("Open the editor"))
            open(MainView.Section.Editor);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        Paragraph("Quickest of all: right-click the timeline itself and use the Opener menu to switch between the openers of your job.");
    }

    private void DrawPlacing(float scale, Vector3 accent)
    {
        var settings = host.Settings;

        Paragraph("Locked, the timeline ignores the mouse completely, so you can click and target straight through it. "
                  + "Unlock it to move it: it gets a coloured outline, drags wherever you like, and its right edge makes it "
                  + "wider or narrower. If the opener does not fit, it scrolls sideways as you go.");

        if (settings.OverlayLocked)
        {
            if (Widgets.PrimaryButton("Unlock the timeline", accent))
                SetLocked(false);
        }
        else if (Widgets.PrimaryButton("Lock it in place", accent))
        {
            SetLocked(true);
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        Widgets.TextDim(settings.OverlayLocked ? "It is locked right now." : "It is unlocked right now - look for the outlined panel.");

        ImGui.Dummy(new Vector2(0f, 4f * scale));
        Bullet("Hold Ctrl+Shift to reach a locked timeline without unlocking it: its buttons appear and a right-click opens its menu.", accent, scale);
        Bullet("The menu switches opener and view, and shows or hides each part of the timeline.", accent, scale);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        Paragraph("Out of the way without losing it: the two small buttons at its top right minimize it to a tab, or hide it. "
                  + "They are there whenever the mouse can reach the timeline. The same choice sits at the top of this window:");

        var presence = settings.Presence;
        if (Widgets.Chip("Shown##presence", presence == TimelinePresence.Shown, accent, scale))
            SetPresence(TimelinePresence.Shown);
        ImGui.SameLine();
        if (Widgets.Chip("Minimized##presence", presence == TimelinePresence.Minimized, accent, scale))
            SetPresence(TimelinePresence.Minimized);
        ImGui.SameLine();
        if (Widgets.Chip("Hidden##presence", presence == TimelinePresence.Hidden, accent, scale))
            SetPresence(TimelinePresence.Hidden);

        ImGui.Dummy(new Vector2(0f, 2f * scale));
        Bullet("Minimized, a small tab stays behind with where the run stands. Click it and the timeline is back; "
               + "it takes clicks even while locked. Your opener is still followed.", accent, scale);
        Bullet("Hidden, nothing is drawn and nothing is followed.", accent, scale);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        Paragraph("There are two views, and each remembers its own position:");

        var view = settings.View;
        if (Widgets.Chip("Compact strip##view", view == TimelineView.Compact, accent, scale))
            SetView(TimelineView.Compact);
        ImGui.SameLine();
        if (Widgets.Chip("Full panel##view", view == TimelineView.Expanded, accent, scale))
            SetView(TimelineView.Expanded);
        ImGui.SameLine();
        if (Widgets.Chip("Automatic##view", view == TimelineView.Automatic, accent, scale))
            SetView(TimelineView.Automatic);

        ImGui.Dummy(new Vector2(0f, 2f * scale));
        Bullet("The compact strip is a small floating row with only what is coming up. The action to press always sits "
               + "in the same spot, so you can park it near your hotbars or cast bar and never look for it.", accent, scale);
        Bullet("The full panel shows the whole plan: both lanes, keys spelled out, GCD numbers and times.", accent, scale);
        Bullet("Automatic gives you the full panel to study before the pull and swaps to the strip once you start.", accent, scale);

        demo.Draw(true, false, scale);
    }

    private void DrawReading(float scale, Vector3 accent)
    {
        Paragraph("Everything on the timeline means something. From top to bottom:");

        var style = TimelineStyle.Default with { Scale = scale, Accent = accent };
        var sample = demo.SampleSteps();

        Legend("Two lanes on one line", "GCDs sit on the line. oGCDs are smaller and raised, placed between the GCDs you weave them into. "
                                         + "Read left to right; the order is exactly the order to press.", scale, (dl, origin) =>
        {
            var gcd = MathF.Round(34f * scale);
            var off = MathF.Round(gcd * 0.76f);
            var x = origin.X;
            for (var i = 0; i < Math.Min(4, sample.Length); i++)
            {
                var size = sample[i].IsGcd ? gcd : off;
                var top = origin.Y + (sample[i].IsGcd ? MathF.Round(off * 0.5f) : 0f);
                IconPainter.Draw(dl, host, sample[i], new Vector2(x, top), new Vector2(x + size, top + size), 1f, size * IconPainter.RoundingRatio);
                x += size + 4f * scale;
            }
        });

        Legend("Key caps", "What to press, read from your own hotbars and keybinds. The filled one is the action to press now. "
                           + "An amber dash means the action is not on any hotbar; \"3-7\" means it is on hotbar 3, slot 7, with no key bound.",
               scale, (dl, origin) =>
               {
                   using var font = host.PushFont(TimelineFont.Small);
                   var height = MathF.Round(17f * scale);
                   var y = origin.Y + 12f * scale;
                   Gfx.KeyChip(dl, origin.X + 22f * scale, y, height, 200f, new KeyHint { Kind = HintKind.Key, Key = "1", Ctrl = true }, style, true, 1f);
                   Gfx.KeyChip(dl, origin.X + 74f * scale, y, height, 200f, new KeyHint { Kind = HintKind.Key, Key = "R", Shift = true }, style, false, 1f);
                   Gfx.KeyChip(dl, origin.X + 118f * scale, y, height, 200f, KeyHint.Missing, style, false, 1f);
                   Gfx.KeyChip(dl, origin.X + 148f * scale, y, height, 200f, new KeyHint { Kind = HintKind.Slot, Bar = 3, Slot = 7 }, style, false, 1f);
               });

        Legend("Cooldown badges", "The recast of your big buttons. Teal marks the ones you use every minute, gold the ones you use "
                                   + "every two minutes - the ones that have to line up with the party.", scale, (dl, origin) =>
        {
            using var font = host.PushFont(TimelineFont.Small);
            var size = MathF.Round(34f * scale);
            var one = Pick(sample, CooldownClass.OneMinute, 60f);
            var two = Pick(sample, CooldownClass.TwoMinute, 120f);
            var a = origin + new Vector2(2f * scale, 8f * scale);
            IconPainter.Draw(dl, host, one, a, a + new Vector2(size), 1f, size * IconPainter.RoundingRatio);
            Gfx.CooldownBadge(dl, a, a + new Vector2(size), one, style, 1f);
            var b = a + new Vector2(size + 22f * scale, 0f);
            IconPainter.Draw(dl, host, two, b, b + new Vector2(size), 1f, size * IconPainter.RoundingRatio);
            Gfx.CooldownBadge(dl, b, b + new Vector2(size), two, style, 1f);
        });

        Legend("GCD numbers and time", "Under each GCD: which one it is, and roughly how many seconds into the fight it happens at your "
                                        + "GCD speed. Above a two minute cooldown, a note says which GCD it goes out on - \"GCD 4\" - so you can "
                                        + "tell at a glance whether your buffs line up with everyone else's.", scale, (dl, origin) =>
        {
            using var font = host.PushFont(TimelineFont.Small);
            var y = origin.Y + 10f * scale;
            dl.AddText(new Vector2(origin.X + 4f * scale, y), Palette.Pack(Palette.TwoMinute), "GCD 4");
            y += 22f * scale;
            var x = origin.X + 4f * scale;
            dl.AddText(new Vector2(x, y), Palette.Pack(Palette.White, Palette.TextDimAlpha), "4");
            x += ImGui.CalcTextSize("4").X + 5f * scale;
            dl.AddCircleFilled(new Vector2(x, y + ImGui.GetTextLineHeight() * 0.55f), 1.2f * scale, Palette.Pack(Palette.White, Palette.TextFaintAlpha), 8);
            dl.AddText(new Vector2(x + 5f * scale, y), Palette.Pack(Palette.White, Palette.TextFaintAlpha), "5.0");
        });

        Legend("After a slip", "A small coral dot under a step means you pressed something else first. An amber dash means you skipped "
                               + "it. Both are deliberately quiet while you play and easier to see once the run is over.", scale, (dl, origin) =>
        {
            var size = MathF.Round(34f * scale);
            var a = origin + new Vector2(2f * scale, 2f * scale);
            var first = sample.Length > 0 ? sample[0] : default;
            IconPainter.Draw(dl, host, first, a, a + new Vector2(size), 0.5f, size * IconPainter.RoundingRatio);
            dl.AddCircleFilled(new Vector2(a.X + size * 0.5f, a.Y + size + 5f * scale), 2.6f * scale, Palette.Pack(Palette.Slip), 12);
            var b = a + new Vector2(size + 22f * scale, 0f);
            IconPainter.Draw(dl, host, first, b, b + new Vector2(size), 0.3f, size * IconPainter.RoundingRatio);
            dl.AddLine(new Vector2(b.X + size * 0.5f - 6f * scale, b.Y + size + 5f * scale), new Vector2(b.X + size * 0.5f + 6f * scale, b.Y + size + 5f * scale),
                       Palette.Pack(Palette.Warn), 2f * scale);
        });

        Widgets.Note("About the numbers: The Balance publishes its openers as pictures, so there is nothing for a plugin to "
                     + "download and keep in sync. The times and GCD numbers are worked out from the game's own data and "
                     + "your current GCD speed instead. Treat them as a plan, not a promise.");
    }

    private void DrawPractising(float scale, Vector3 accent)
    {
        Paragraph("Stand at a striking dummy and target it: the timeline appears. Press the first action of the opener and you are off. "
                  + "Anywhere else it comes up the moment you press that first action.");

        demo.Draw(false, true, scale);

        Bullet("A wrong press is noted, not punished: the ring warms for a moment and what you hit shows up small beside "
               + "the step. Press the right thing and carry on.", accent, scale);
        Bullet("If you skip a weave and go straight to what follows, the skipped step is marked and the timeline jumps ahead "
               + "with you instead of getting stuck.", accent, scale);
        Bullet("While you cast, a bar fills along the bottom of the icon.", accent, scale);
        Bullet("Sprint and role actions such as True North never count as mistakes.", accent, scale);
        Bullet("When you finish or stop, the result shows for a moment. Stop pressing things and it readies itself again.", accent, scale);

        ImGui.Dummy(new Vector2(0f, 4f * scale));
        if (ImGui.Button(host.Trainer.CountdownRemaining >= 0f ? "Stop the countdown" : "Start a practice countdown"))
        {
            if (host.Trainer.CountdownRemaining >= 0f)
                host.Trainer.StopCountdown();
            else
                host.Trainer.StartCountdown();
        }

        ImGui.SameLine();
        if (ImGui.Button("See my results"))
            open(MainView.Section.Results);
        ImGui.SameLine();
        if (ImGui.Button("Change how mistakes are shown"))
            openSettings(SettingsView.Page.Behaviour);

        ImGui.Dummy(new Vector2(0f, 2f * scale));
        Widgets.Note("There you can also pick a soft sound for a wrong press, switch to a louder visual cue, or turn the cue off "
                     + "entirely and only look at the result.");
    }

    private void DrawVisibility(float scale, Vector3 accent)
    {
        var settings = host.Settings;

        Paragraph("The timeline tries to be there when you want it and gone when you do not. During an opener, and briefly "
                  + "afterwards with the result, it is always visible. Beforehand it comes up on its own:");

        State("when you target a striking dummy", settings.ShowAtDummy, accent, scale);
        State("when you target any enemy", settings.ShowOnEnemyTarget, accent, scale);
        State("as soon as you are inside a trial or raid", settings.ShowInBossDuties, accent, scale);
        State("always", settings.ShowAlways, accent, scale);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        Paragraph("It only follows what you press in places you allow:");

        State("the open world and housing", settings.TrackOpenWorld, accent, scale);
        State("trials, raids and ultimates", settings.TrackTrials && settings.TrackRaids && settings.TrackUltimates, accent, scale);
        State("alliance raids", settings.TrackAllianceRaids, accent, scale);
        State("dungeons", settings.TrackDungeons, accent, scale);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        if (Widgets.PrimaryButton("Change these", accent))
            openSettings(SettingsView.Page.Behaviour);
    }

    private void DrawLevels(float scale, Vector3 accent)
    {
        var trainer = host.Trainer;
        var cap = host.Catalog.MaxLevel;

        Paragraph($"The bundled openers are written for level {cap}. Below that - level sync, an older Ultimate, a job you are "
                  + "still levelling - some of those buttons do not exist yet.");

        Bullet("The timeline fits the opener to your level automatically: actions you have not learned are left out, and "
               + "upgraded actions fall back to the version you do have (High Thunder becomes Thunder III, and so on).", accent, scale);
        Bullet("That keeps it playable, but it is an approximation. For content you care about, build the opener you "
               + "actually want for that level.", accent, scale);
        Bullet("Whatever you select while you are at an older level cap is remembered for that cap only. Pick a level 90 "
               + "opener while synced to 90 and it comes back whenever you are at 90, without touching your current one.", accent, scale);
        Bullet("In the Editor, set the level to see only actions available then, and use \"Fit to level\" to start from an "
               + "existing opener.", accent, scale);

        ImGui.Dummy(new Vector2(0f, 4f * scale));
        if (trainer.Level > 0)
        {
            var bracket = LevelAdapter.Bracket(trainer.Level);
            var text = trainer.Adjustment is { Changed: true } adjusted
                           ? $"You are level {trainer.Level}. The opener on your timeline was fitted to it: {adjusted.Removed} left out, {adjusted.Replaced} swapped."
                           : $"You are level {trainer.Level}; choices you make now are kept for level {bracket} content.";
            Widgets.Note(text);
        }

        ImGui.Dummy(new Vector2(0f, 2f * scale));
        if (ImGui.Button("Open the editor"))
            open(MainView.Section.Editor);
    }

    private void DrawCommands(float scale, Vector3 accent)
    {
        Paragraph("Everything can be done with the mouse, but these are handy for macros:");

        Command(Branding.Command, "Open this window", scale);
        Command(Branding.Command + " guide  |  settings", "Open this guide, or the settings", scale);
        Command(Branding.Command + " setup", "Go through the first-run setup again", scale);
        Command(Branding.Command + " show  |  min  |  hide", "Show the timeline, minimize it to a tab, or hide it", scale);
        Command(Branding.Command + " toggle", "Flip between shown and hidden", scale);
        Command(Branding.Command + " unlock  |  lock", "Free the timeline to move it, or pin it", scale);
        Command(Branding.Command + " view full | compact | auto", "Switch views", scale);
        Command(Branding.Command + " reset", "Abandon the current attempt", scale);
        Command(Branding.Command + " cd [seconds]", "Start a private practice countdown", scale);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        Widgets.Note("Right-clicking the timeline gives you most of these as a menu. When it is locked, hold Ctrl+Shift while you right-click. "
                     + "If no other plugin uses it, " + Branding.ShortCommand + " works as a shorter " + Branding.Command + ".");

        Widgets.SectionTitle("Credits", accent, scale);
        Widgets.Note($"{Branding.Name} {host.Version}. Based on OpenerCreator by Infi, herulume and sevii, rebuilt around a live "
                     + "timeline. The bundled openers are the ones published by The Balance. Licensed AGPL-3.0-or-later.");

        ImGui.Dummy(new Vector2(0f, 2f * scale));
        if (ImGui.Button("Website"))
            host.OpenLink(Branding.SiteUrl);
        ImGui.SameLine();
        if (ImGui.Button("Source"))
            host.OpenLink(Branding.RepoUrl);
        ImGui.SameLine();
        if (ImGui.Button("OpenerCreator (original)"))
            host.OpenLink("https://github.com/Infiziert90/OpenerCreator");
        ImGui.SameLine();
        if (ImGui.Button("The Balance"))
            host.OpenLink("https://www.thebalanceffxiv.com/");
    }

    // ---- Building blocks ----

    private static void Paragraph(string text)
    {
        ImGui.TextWrapped(text);
        ImGui.Dummy(new Vector2(0f, 2f));
    }

    private static void Bullet(string text, Vector3 accent, float scale)
    {
        Widgets.Bullet(text, accent, scale);
    }

    /// <summary>A line that says whether something is currently on or off.</summary>
    private static void State(string text, bool on, Vector3 accent, float scale)
    {
        var start = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var center = start + new Vector2(9f * scale, ImGui.GetTextLineHeight() * 0.55f);
        if (on)
        {
            dl.AddCircleFilled(center, 6.5f * scale, Palette.Pack(accent, 0.9f), 16);
            Gfx.Check(dl, center, 3f * scale, Palette.Pack(Palette.OnAccent), 1.6f * scale);
        }
        else
        {
            dl.AddCircle(center, 6f * scale, Palette.Pack(Palette.White, 0.3f), 16, 1.2f * scale);
        }

        ImGui.Indent(24f * scale);
        ImGui.TextColored(on ? new Vector4(1f, 1f, 1f, Palette.TextAlpha) : Widgets.Faint, on ? text : text + "  (off)");
        ImGui.Unindent(24f * scale);
    }

    /// <summary>An illustration on the left, its explanation on the right.</summary>
    private static void Legend(string title, string text, float scale, Action<ImDrawListPtr, Vector2> illustration)
    {
        var art = MathF.Round(190f * scale);
        var start = ImGui.GetCursorScreenPos();
        var height = MathF.Round(62f * scale);
        var dl = ImGui.GetWindowDrawList();
        var width = ImGui.GetContentRegionAvail().X;

        dl.AddRectFilled(start, start + new Vector2(art, height), Palette.Pack(Palette.White, 0.04f), 7f * scale);
        illustration(dl, start + new Vector2(10f * scale, 6f * scale));

        ImGui.SetCursorScreenPos(start + new Vector2(art + 14f * scale, 0f));
        ImGui.BeginGroup();
        ImGui.TextUnformatted(title);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - art - 20f * scale);
        ImGui.PushStyleColor(ImGuiCol.Text, Widgets.Dim);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();

        var bottom = MathF.Max(ImGui.GetCursorScreenPos().Y, start.Y + height);
        ImGui.SetCursorScreenPos(new Vector2(start.X, bottom));
        ImGui.Dummy(new Vector2(0f, 8f * scale));
    }

    private static void Command(string command, string description, float scale)
    {
        ImGui.TextUnformatted(command);
        ImGui.SameLine(290f * scale);
        Widgets.TextDim(description);
    }

    private static TimelineStep Pick(TimelineStep[] steps, CooldownClass cooldown, float recast)
    {
        foreach (var step in steps)
            if (step.Cooldown == cooldown)
                return step;

        var fallback = steps.Length > 0 ? steps[0] : default;
        fallback.Cooldown = cooldown;
        fallback.Recast = recast;
        return fallback;
    }

    private void SetLocked(bool locked)
    {
        host.Settings.OverlayLocked = locked;

        // This page is about putting the timeline somewhere, so make sure it can be seen.
        host.Settings.Presence = TimelinePresence.Shown;
        host.SaveSettings();
    }

    private void SetPresence(TimelinePresence presence)
    {
        host.Settings.Presence = presence;
        host.SaveSettings();
    }

    private void SetView(TimelineView view)
    {
        host.Settings.View = view;
        host.SaveSettings();
    }
}
