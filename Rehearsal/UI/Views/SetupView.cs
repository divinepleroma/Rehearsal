using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Rehearsal.UI.Timeline;

namespace Rehearsal.UI.Views;

/// <summary>
/// The first-run setup: five short steps through the choices that matter, each already made, with
/// a timeline that plays itself above them and changes as you click. "Next" five times is a
/// perfectly good way through it; so is skipping it.
/// Every change is applied and saved immediately, exactly as on the Settings tab.
/// </summary>
public sealed class SetupView(IAppHost host, Action done)
{
    private static readonly string[] Titles = ["Pick your view", "When it appears", "Mistakes", "Look", "Place it"];

    private readonly DemoTimeline demo = new(host);

    /// <summary>The timeline was unlocked by the last step, so leaving that step locks it again.</summary>
    private bool placing;

    private int step;

    public static int StepCount => Titles.Length;

    private static int LastStep => Titles.Length - 1;

    /// <summary>Begins the setup (again) at a step (zero-based).</summary>
    public void Start(int at = 0)
    {
        Leave();
        step = Math.Clamp(at, 0, LastStep);
    }

    /// <summary>The setup is being left without finishing it: put back what it only borrowed.</summary>
    public void Leave()
    {
        if (!placing)
            return;

        placing = false;
        host.Settings.OverlayLocked = true;
        host.SaveSettings();
    }

    public void Draw()
    {
        var scale = host.Scale;
        var settings = host.Settings;
        var accent = settings.Accent;

        DrawHeader(accent, scale);
        DrawSteps(accent, scale);
        SyncPlacing(settings);

        var changed = false;
        var footer = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y + MathF.Round(6f * scale);
        if (ImGui.BeginChild("##setupPage", new Vector2(0f, -footer), false))
        {
            var column = MathF.Min(230f * scale, ImGui.GetContentRegionAvail().X * 0.36f);
            ImGui.PushItemWidth(-1f);

            switch (step)
            {
                case 0:
                    DrawStage(settings, false, scale);
                    changed |= DrawView(settings, accent, scale, column);
                    break;
                case 1:
                    DrawStage(settings, false, scale);
                    changed |= DrawWhen(settings, accent, scale, column);
                    break;
                case 2:
                    DrawStage(settings, true, scale);
                    changed |= DrawMistakes(settings, accent, scale, column);
                    break;
                case 3:
                    DrawStage(settings, false, scale);
                    changed |= DrawLook(settings, accent, scale);
                    break;
                default:
                    DrawPlacing(settings, accent, scale);
                    break;
            }

            ImGui.PopItemWidth();
        }

        ImGui.EndChild();

        if (changed)
        {
            settings.Sanitize();
            host.SaveSettings();
        }

        DrawFooter(accent, scale);
    }

    // ---- Frame ----

    private void DrawHeader(Vector3 accent, float scale)
    {
        const string skip = "Skip setup";
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var skipWidth = ImGui.CalcTextSize(skip).X + ImGui.GetStyle().FramePadding.X * 2f;

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(new Vector4(Palette.Mix(accent, Palette.White, 0.3f), 1f), "Welcome to " + Branding.Name);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        if (ImGui.GetCursorScreenPos().X + ImGui.CalcTextSize("Five quick choices, all already made. Next is always a fine answer.").X
            < start.X + width - skipWidth - 12f * scale)
            Widgets.TextDim("Five quick choices, all already made. Next is always a fine answer.");
        else
            ImGui.NewLine();

        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(MathF.Round(start.X + width - skipWidth), start.Y));
        if (ImGui.Button(skip))
            Finish();
        Widgets.Tooltip("Keeps everything as it is right now and locks the timeline where it is. "
                        + "You can run the setup again from the Guide tab or with " + Branding.Command + " setup.", scale);

        ImGui.Dummy(new Vector2(0f, 2f * scale));
    }

    /// <summary>The five steps as a row of numbered dots; any of them can be clicked.</summary>
    private void DrawSteps(Vector3 accent, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = MathF.Round(34f * scale);
        var cell = MathF.Floor(width / Titles.Length);
        var radius = 10f * scale;

        for (var i = 0; i < Titles.Length; i++)
        {
            var min = new Vector2(origin.X + i * cell, origin.Y);
            var size = new Vector2(cell - 4f * scale, height);
            ImGui.SetCursorScreenPos(min);
            if (ImGui.InvisibleButton($"##setupStep{i}", size))
                step = i;

            var hovered = ImGui.IsItemHovered();
            var current = i == step;
            var passed = i < step;
            if (current || hovered)
                dl.AddRectFilled(min, min + size, current ? Palette.Pack(accent, 0.18f) : Palette.Pack(Palette.White, 0.06f), 7f * scale);

            var badge = min + new Vector2(18f * scale, height * 0.5f);
            dl.AddCircleFilled(badge, radius, current || passed ? Palette.Pack(accent, current ? 0.95f : 0.55f) : Palette.Pack(Palette.White, 0.10f), 20);
            if (passed)
            {
                Gfx.Check(dl, badge, 4f * scale, Palette.Pack(Palette.OnAccent), 1.6f * scale);
            }
            else
            {
                var number = (i + 1).ToString();
                dl.AddText(badge - ImGui.CalcTextSize(number) * 0.5f, current ? Palette.Pack(Palette.OnAccent) : Palette.Pack(Palette.White, 0.7f), number);
            }

            // The title, as long as it fits its cell; the number alone says enough otherwise.
            var titleSize = ImGui.CalcTextSize(Titles[i]);
            if (38f * scale + titleSize.X <= size.X - 4f * scale)
            {
                dl.AddText(new Vector2(min.X + 38f * scale, min.Y + (height - titleSize.Y) * 0.5f),
                           Palette.Pack(Palette.White, current ? Palette.TextAlpha : Palette.TextDimAlpha), Titles[i]);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));
        ImGui.Dummy(new Vector2(0f, 4f * scale));
    }

    private void DrawFooter(Vector3 accent, float scale)
    {
        ImGui.Dummy(new Vector2(0f, 2f * scale));
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;

        ImGui.BeginDisabled(step == 0);
        if (ImGui.Button("Back"))
            step--;
        ImGui.EndDisabled();
        ImGui.SameLine();

        if (step < LastStep)
        {
            if (Widgets.PrimaryButton("Next", accent))
                step++;
        }
        else if (Widgets.PrimaryButton("Finish and lock it in place", accent))
        {
            Finish();
        }

        var counter = $"{step + 1} of {Titles.Length}";
        var counterSize = ImGui.CalcTextSize(counter);
        ImGui.GetWindowDrawList().AddText(
            new Vector2(start.X + width - counterSize.X, MathF.Round(start.Y + (ImGui.GetFrameHeight() - counterSize.Y) * 0.5f)),
            Palette.Pack(Palette.White, Palette.TextFaintAlpha), counter);
    }

    /// <summary>The self-playing timeline, drawn the way it is set up right now.</summary>
    private void DrawStage(Settings settings, bool mistakes, float scale)
    {
        // Automatic ends up as the strip for as long as an opener runs, so that is what plays here.
        var compact = settings.View != TimelineView.Expanded;
        var size = demo.Measure(compact, scale, true);
        if (size.X <= 0f)
            return;

        var dl = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var pad = MathF.Round(12f * scale);
        var height = size.Y + pad * 2f;

        dl.AddRectFilled(start, start + new Vector2(width, height), Palette.Pack(Palette.White, 0.035f), 9f * scale);
        ImGui.SetCursorScreenPos(new Vector2(MathF.Round(start.X + MathF.Max(0f, (width - size.X) * 0.5f)), start.Y + pad - 4f * scale));
        demo.Draw(compact, mistakes, scale, true);

        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + height));
        ImGui.Dummy(new Vector2(0f, 6f * scale));
    }

    // ---- Steps ----

    private static bool DrawView(Settings settings, Vector3 accent, float scale, float column)
    {
        var changed = false;

        Widgets.Label("On my HUD I want", column, scale);
        var row = new ChipRow(accent, scale);
        changed |= row.Choice("Compact strip##view", ref settings.View, TimelineView.Compact);
        changed |= row.Choice("Full panel##view", ref settings.View, TimelineView.Expanded);
        changed |= row.Choice("Automatic##view", ref settings.View, TimelineView.Automatic);
        row.End();
        Hint(settings.View switch
        {
            TimelineView.Compact => "A small floating row with just what is coming up. It scrolls sideways as you play, and the "
                                    + "action to press always sits in the same spot.",
            TimelineView.Expanded => "The whole plan at once: GCDs on the line, oGCDs woven between them, keys spelled out, "
                                     + "GCD numbers and times.",
            _ => "The full panel to study before the pull, then the small strip as soon as you start."
        }, column);

        if (settings.View != TimelineView.Expanded)
        {
            Widgets.Label(settings.View == TimelineView.Automatic ? "The strip shows" : "It shows", column, scale);
            row = new ChipRow(accent, scale);
            changed |= row.Flag("Key caps##compact", ref settings.CompactShowKeybinds);
            Widgets.Tooltip("What to press for each action, read live from your own hotbars and keybinds.", scale);
            changed |= row.Flag("Cooldown markers##compact", ref settings.CompactShowBadges);
            Widgets.Tooltip("Small badges with the recast. Teal: used every minute. Gold: used every two minutes.", scale);
            changed |= row.Flag("Status line##compact", ref settings.CompactShowStatus);
            Widgets.Tooltip("State, timer and counter, as a line of text above the strip.", scale);
            changed |= row.Flag("Progress bar##compact", ref settings.ShowProgress);
            row.End();

            Widgets.Label("Icon size", column, scale);
            changed |= ImGui.SliderFloat("##compactsize", ref settings.CompactIconSize, 24f, 64f, "%.0f px");
        }

        if (settings.View != TimelineView.Compact)
        {
            Widgets.Label(settings.View == TimelineView.Automatic ? "The panel shows" : "It shows", column, scale);
            row = new ChipRow(accent, scale);
            changed |= row.Flag("Key caps##full", ref settings.ShowKeybinds);
            Widgets.Tooltip("What to press for each action, read live from your own hotbars and keybinds.", scale);
            changed |= row.Flag("GCD numbers and time##full", ref settings.ShowRuler);
            Widgets.Tooltip("Under every GCD: which one it is and roughly how many seconds into the fight it happens.", scale);
            changed |= row.Flag("Header##full", ref settings.ShowHeader);
            changed |= row.Flag("Lane labels##full", ref settings.ShowLaneLabels);
            if (settings.View == TimelineView.Expanded)
                changed |= row.Flag("Progress bar##full", ref settings.ShowProgress);
            row.End();

            Widgets.Label("Layout", column, scale);
            row = new ChipRow(accent, scale);
            changed |= row.Choice("Two lanes##layout", ref settings.Layout, TimelineLayoutMode.Staggered);
            changed |= row.Choice("Flat row##layout", ref settings.Layout, TimelineLayoutMode.Inline);
            row.End();

            if (settings.View == TimelineView.Expanded)
            {
                Widgets.Label("Icon size", column, scale);
                changed |= ImGui.SliderFloat("##iconsize", ref settings.IconSize, 28f, 88f, "%.0f px");
            }
        }

        return changed;
    }

    private static bool DrawWhen(Settings settings, Vector3 accent, float scale, float column)
    {
        var changed = false;

        Widgets.Label("Comes up before the pull", column, scale);
        var row = new ChipRow(accent, scale);
        changed |= row.Flag("Targeting a dummy##dummy", ref settings.ShowAtDummy);
        changed |= row.Flag("In a trial or raid##boss", ref settings.ShowInBossDuties);
        Widgets.Tooltip("There it comes up as soon as you zone in, so it is ready during the countdown without needing a target.", scale);
        changed |= row.Flag("Targeting any enemy##enemy", ref settings.ShowOnEnemyTarget);
        Widgets.Tooltip("Out in the world that is every mob you click on, which is why it starts switched off.", scale);
        changed |= row.Flag("Always##always", ref settings.ShowAlways);
        row.End();
        Hint("The rest of the time it is out of sight. Whatever you pick here, it appears the moment you press the first "
             + "action of your opener, and leaves again shortly after the result.", column);

        Widgets.Label("Follows my opener in", column, scale);
        row = new ChipRow(accent, scale);
        changed |= row.Flag("Open world##world", ref settings.TrackOpenWorld);
        Widgets.Tooltip("And housing: this is where the striking dummies are.", scale);
        changed |= row.Flag("Trials##trials", ref settings.TrackTrials);
        Widgets.Tooltip("Including Extreme and Unreal.", scale);
        changed |= row.Flag("Raids##raids", ref settings.TrackRaids);
        Widgets.Tooltip("Including Savage.", scale);
        changed |= row.Flag("Ultimates##ultimates", ref settings.TrackUltimates);
        changed |= row.Flag("Alliance raids##alliance", ref settings.TrackAllianceRaids);
        changed |= row.Flag("Dungeons##dungeons", ref settings.TrackDungeons);
        Widgets.Tooltip("Every pack counts as a pull there, so expect the timeline to react to each one.", scale);
        changed |= row.Flag("Other duties##other", ref settings.TrackOtherDuties);
        row.End();
        Hint("Anywhere that is switched off, nothing is watched and nothing is shown.", column);

        return changed;
    }

    private bool DrawMistakes(Settings settings, Vector3 accent, float scale, float column)
    {
        var changed = false;

        Widgets.Label("A wrong press gets", column, scale);
        var row = new ChipRow(accent, scale);
        changed |= row.Choice("A gentle cue##feedback", ref settings.MistakeFeedback, FeedbackStyle.Gentle);
        changed |= row.Choice("A strong cue##feedback", ref settings.MistakeFeedback, FeedbackStyle.Strong);
        changed |= row.Choice("Nothing while I play##feedback", ref settings.MistakeFeedback, FeedbackStyle.Off);
        row.End();
        Hint(settings.MistakeFeedback switch
        {
            FeedbackStyle.Gentle => "The ring around the expected action warms for a moment and what you pressed appears small "
                                    + "beside it. Nothing moves and nothing is covered, so you can keep going.",
            FeedbackStyle.Strong => "A red flash, a shake and the wrong icon crossed out on the step itself.",
            _ => "No cue during the run; mistakes only show in the result afterwards."
        }, column);

        Widgets.Label("And a sound", column, scale, "One of the game's own macro sounds, at your sound effect volume.");
        ImGui.SetNextItemWidth(MathF.Max(120f * scale, ImGui.GetContentRegionAvail().X - 76f * scale));
        if (ImGui.BeginCombo("##sound", SoundLabel(settings.MistakeSound)))
        {
            for (var n = 0; n <= 16; n++)
            {
                if (!ImGui.Selectable(SoundLabel(n), n == settings.MistakeSound))
                    continue;

                settings.MistakeSound = n;
                changed = true;
                if (n > 0)
                    host.PlaySound(n);
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(settings.MistakeSound == 0);
        if (ImGui.Button("Test"))
            host.PlaySound(settings.MistakeSound);
        ImGui.EndDisabled();

        Widgets.Label("Rules", column, scale);
        row = new ChipRow(accent, scale);
        changed |= row.Flag("Stop at the first mistake##strict", ref settings.StopAtFirstMistake);
        Widgets.Tooltip("Off (recommended): a wrong press is noted and you carry on; a step you skipped is marked and "
                        + "the timeline jumps ahead with you.", scale);
        changed |= row.Flag("Ignore Sprint and role actions##utility", ref settings.IgnoreUtilityActions);
        Widgets.Tooltip("True North, Second Wind, Arm's Length and friends never count as mistakes when they are not "
                        + "part of the opener.", scale);
        row.End();
        Hint("The example above slips once and forgets a weave, so you can see what each choice looks like.", column);

        return changed;
    }

    private static string SoundLabel(int sound)
    {
        return sound <= 0 ? "No sound" : $"Sound effect {sound}  (<se.{sound}>)";
    }

    private static bool DrawLook(Settings settings, Vector3 accent, float scale)
    {
        var changed = SettingsView.DrawPresets(settings, accent, scale);

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        Widgets.TextDim("Accent colour");
        ImGui.Dummy(new Vector2(0f, 2f * scale));
        changed |= SettingsView.DrawAccentSwatches(settings, scale);

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        Widgets.Note("The look applies to the timeline and to this window. Exact opacity, blur, any other colour and how "
                     + "keys are written are under Settings, Appearance.");
        return changed;
    }

    private void DrawPlacing(Settings settings, Vector3 accent, float scale)
    {
        var both = settings.View == TimelineView.Automatic;
        var thing = settings.View == TimelineView.Expanded ? "panel" : "strip";

        ImGui.TextWrapped(both
                              ? "Both views are on your screen now, each with a coloured outline, so each can be given its own place. "
                                + "Drag them where you want them, and drag a right edge to make that one wider or narrower."
                              : $"Your timeline is on your screen now, with a coloured outline. Drag the {thing} where you want it, and "
                                + "drag its right edge to make it wider or narrower: what does not fit scrolls sideways as you play.");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        if (settings.OverlayLocked)
        {
            if (ImGui.Button("Unlock it again"))
            {
                settings.OverlayLocked = false;
                settings.Presence = TimelinePresence.Shown;
                host.SaveSettings();
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            Widgets.TextDim("It is locked right now.");
        }
        else
        {
            Widgets.TextDim(both ? "They are unlocked right now: look for the two outlined panels."
                                 : $"It is unlocked right now: look for the outlined {thing}.");
        }

        if (host.Trainer.ActiveActions.Count == 0)
        {
            ImGui.Dummy(new Vector2(0f, 2f * scale));
            Widgets.Note("There is no opener for your current job yet, so it is empty for now. The Openers tab, which opens "
                         + "when you finish, is where you pick one.");
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        Widgets.TextDim("Good to know for later");
        ImGui.Dummy(new Vector2(0f, 2f * scale));
        Widgets.Bullet("Finishing locks it in place. From then on it ignores the mouse, and you click and target straight through it.", accent, scale);
        Widgets.Bullet("Hold Ctrl+Shift to reach it without unlocking: two small buttons at its top right minimize it to a tab "
                       + "or hide it, and a right-click opens its menu.", accent, scale);
        Widgets.Bullet("Shown, minimized or hidden is also a switch at the top of this window, on every tab.", accent, scale);
        Widgets.Bullet($"Everything you chose here, and the finer details, are on the Settings tab. {Branding.Command} opens this window.", accent, scale);
    }

    // ---- Helpers ----

    /// <summary>A line of explanation under a row, starting where the row's controls start.</summary>
    private static void Hint(string text, float column)
    {
        ImGui.SetCursorPosX(column);
        ImGui.PushStyleColor(ImGuiCol.Text, Widgets.Dim);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0f, 4f));
    }

    /// <summary>The last step frees the timeline so it can be dragged; every other step has it locked.</summary>
    private void SyncPlacing(Settings settings)
    {
        if (step == LastStep && !placing)
        {
            placing = true;
            settings.OverlayLocked = false;
            settings.Presence = TimelinePresence.Shown;
            host.SaveSettings();
        }
        else if (step != LastStep && placing)
        {
            Leave();
        }
    }

    private void Finish()
    {
        placing = false;
        step = 0;

        var settings = host.Settings;
        settings.SetupDone = true;
        settings.OverlayLocked = true;
        host.SaveSettings();
        done();
    }
}
