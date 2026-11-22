using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline.UI.Views;

/// <summary>All options, split into pages. Every change is applied and saved immediately.</summary>
public sealed class SettingsView(IAppHost host, Action showGuide)
{
    public enum Page
    {
        Timeline,
        Visibility,
        Hints,
        Practice,
        Appearance,
        About
    }

    private static readonly (Page Page, string Label)[] Pages =
    [
        (Page.Timeline, "Timeline"),
        (Page.Visibility, "When it shows"),
        (Page.Hints, "Key hints"),
        (Page.Practice, "Practice"),
        (Page.Appearance, "Appearance"),
        (Page.About, "About")
    ];

    private Vector3 hexFor = new(-1f);
    private string hexText = string.Empty;
    private Page page;

    public void Show(Page target)
    {
        page = target;
    }

    public void Draw()
    {
        var scale = host.Scale;
        var settings = host.Settings;
        var accent = settings.Accent;
        var changed = false;

        for (var i = 0; i < Pages.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine();
            if (Widgets.Chip(Pages[i].Label + "##page", page == Pages[i].Page, accent, scale))
                page = Pages[i].Page;
        }

        ImGui.Dummy(new Vector2(0f, 2f * scale));

        if (ImGui.BeginChild("##settings", Vector2.Zero, false))
        {
            var column = MathF.Min(270f * scale, ImGui.GetContentRegionAvail().X * 0.42f);
            ImGui.PushItemWidth(-1f);

            switch (page)
            {
                case Page.Timeline:
                    changed |= DrawTimeline(settings, accent, scale, column);
                    break;
                case Page.Visibility:
                    changed |= DrawVisibility(settings, accent, scale);
                    break;
                case Page.Hints:
                    changed |= DrawHints(settings, accent, scale, column);
                    break;
                case Page.Practice:
                    changed |= DrawPractice(settings, accent, scale, column);
                    break;
                case Page.Appearance:
                    changed |= DrawAppearance(settings, accent, scale, column);
                    break;
                default:
                    DrawAbout(accent, scale);
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
    }

    // ---- Timeline ----

    private static bool DrawTimeline(Settings settings, Vector3 accent, float scale, float column)
    {
        var changed = false;

        Widgets.SectionTitle("Timeline", accent, scale);

        changed |= Widgets.Toggle("Show the timeline##enabled", ref settings.OverlayEnabled, accent, scale);
        Widgets.Tooltip("Master switch. Also: /opener toggle", scale);

        changed |= Widgets.Toggle("Lock in place (click-through)##locked", ref settings.OverlayLocked, accent, scale);
        Widgets.Tooltip("Unlocked: drag it anywhere and resize its width. Locked: it ignores the mouse entirely, "
                        + "like part of the HUD. Also: /opener lock, /opener unlock", scale);

        changed |= Widgets.Toggle("Hold Ctrl+Shift to reach it while locked##reach", ref settings.MenuWhileLocked, accent, scale);
        Widgets.Tooltip("With both keys held the locked timeline takes the mouse again, so you can right-click it "
                        + "for its menu without unlocking.", scale);

        Widgets.Label("View", column, scale, "The full panel shows the whole plan. The compact strip is a small floating row with just "
                                             + "what is coming up, and keeps the action to press in one fixed spot. Each view remembers "
                                             + "its own position.");
        changed |= Widgets.EnumCombo("##view", ref settings.View, v => v switch
        {
            TimelineView.Expanded => "Full panel",
            TimelineView.Compact => "Compact strip",
            _ => "Automatic (full before the pull, compact once it starts)"
        });

        Widgets.Label("Keep the result on screen", column, scale);
        changed |= ImGui.SliderFloat("##linger", ref settings.ResultSeconds, 3f, 60f, "%.0f s");

        Widgets.SectionTitle("Full panel", accent, scale);

        Widgets.Label("Layout", column, scale, "Two lanes: abilities sit raised and smaller between the GCDs they are woven into.");
        changed |= Widgets.EnumCombo("##layout", ref settings.Layout, v => v switch
        {
            TimelineLayoutMode.Staggered => "Two lanes (oGCDs raised)",
            _ => "Flat row"
        });

        Widgets.Label("Icon size", column, scale);
        changed |= ImGui.SliderFloat("##iconsize", ref settings.IconSize, 28f, 88f, "%.0f px");

        Widgets.Label("oGCD icon size", column, scale, "Relative to a GCD icon. Applies to both views.");
        var offPercent = settings.OffGcdScale * 100f;
        if (ImGui.SliderFloat("##offscale", ref offPercent, 55f, 100f, "%.0f%%"))
        {
            settings.OffGcdScale = offPercent / 100f;
            changed = true;
        }

        Widgets.Label("Spacing", column, scale);
        changed |= ImGui.SliderFloat("##gap", ref settings.IconGap, 0f, 20f, "%.0f px");

        Widgets.Label("Cooldown markers", column, scale,
                      "Small badges with the recast time. Teal: used every minute. Gold: used every two minutes.");
        changed |= Widgets.EnumCombo("##badges", ref settings.CooldownBadges, v => v switch
        {
            CooldownBadgeMode.Off => "Off",
            CooldownBadgeMode.Burst => "60s and 120s cooldowns",
            _ => "Every cooldown of 30s or more"
        });

        Widgets.Label("Burst notes", column, scale,
                      "A small \"GCD 4\" above your big cooldowns: the global cooldown they go out on. Handy for lining "
                      + "up with the rest of the party.");
        changed |= Widgets.EnumCombo("##callouts", ref settings.BurstCallouts, v => v switch
        {
            BurstCallouts.Off => "Off",
            BurstCallouts.TwoMinute => "Two minute cooldowns",
            _ => "One and two minute cooldowns"
        });

        changed |= Widgets.Toggle("GCD numbers and estimated time##ruler", ref settings.ShowRuler, accent, scale);
        Widgets.Tooltip("Under every GCD: which one it is and roughly how many seconds into the fight it happens, worked "
                        + "out from your own GCD speed. An estimate - it assumes the GCD keeps rolling.", scale);

        changed |= Widgets.Toggle("Count the pre-pull cast as GCD 1##precast", ref settings.CountPrecast, accent, scale);
        Widgets.Tooltip("Guides differ: some start counting at the cast you begin before the pull, others at the first "
                        + "GCD after it. On: every GCD you press has a number.", scale);

        changed |= Widgets.Toggle("Header (opener name, timer, result)##header", ref settings.ShowHeader, accent, scale);
        changed |= Widgets.Toggle("Lane labels (GCD / oGCD)##lanes", ref settings.ShowLaneLabels, accent, scale);
        changed |= Widgets.Toggle("Progress bar##progress", ref settings.ShowProgress, accent, scale);

        Widgets.SectionTitle("Compact strip", accent, scale);

        Widgets.Label("Icon size", column, scale);
        changed |= ImGui.SliderFloat("##compactsize", ref settings.CompactIconSize, 24f, 64f, "%.0f px");

        changed |= Widgets.Toggle("Status line above it (state, timer, counter)##cstatus", ref settings.CompactShowStatus, accent, scale);
        changed |= Widgets.Toggle("Key caps##ckeys", ref settings.CompactShowKeybinds, accent, scale);
        changed |= Widgets.Toggle("Cooldown markers##cbadges", ref settings.CompactShowBadges, accent, scale);

        return changed;
    }

    // ---- When it shows ----

    private static bool DrawVisibility(Settings settings, Vector3 accent, float scale)
    {
        var changed = false;

        Widgets.SectionTitle("Before the pull, bring the timeline up when...", accent, scale);
        Widgets.Note("While an opener is running, and for a moment afterwards with the result, it is always there. "
                     + "These decide when it appears on its own beforehand, so it is out of the way the rest of the time.");
        ImGui.Dummy(new Vector2(0f, 2f * scale));

        changed |= Widgets.Toggle("I target a striking dummy##dummy", ref settings.ShowAtDummy, accent, scale);
        changed |= Widgets.Toggle("I target any enemy##enemy", ref settings.ShowOnEnemyTarget, accent, scale);
        changed |= Widgets.Toggle("I am inside a trial or raid##boss", ref settings.ShowInBossDuties, accent, scale);
        Widgets.Tooltip("There it comes up as soon as you zone in, so it is ready during the countdown without needing a target.", scale);
        changed |= Widgets.Toggle("Always, wherever openers are followed##always", ref settings.ShowAlways, accent, scale);

        Widgets.SectionTitle("Follow my opener in...", accent, scale);
        Widgets.Note("Where the plugin watches what you press at all. Anywhere that is switched off, the timeline stays hidden.");
        ImGui.Dummy(new Vector2(0f, 2f * scale));

        changed |= Widgets.Toggle("The open world and housing (striking dummies)##world", ref settings.TrackOpenWorld, accent, scale);
        changed |= Widgets.Toggle("Trials (including Extreme and Unreal)##trials", ref settings.TrackTrials, accent, scale);
        changed |= Widgets.Toggle("Raids (including Savage)##raids", ref settings.TrackRaids, accent, scale);
        changed |= Widgets.Toggle("Ultimate raids##ultimates", ref settings.TrackUltimates, accent, scale);
        changed |= Widgets.Toggle("Alliance raids##alliance", ref settings.TrackAllianceRaids, accent, scale);
        changed |= Widgets.Toggle("Dungeons##dungeons", ref settings.TrackDungeons, accent, scale);
        Widgets.Tooltip("Every pack counts as a pull there, so expect the timeline to react to each one.", scale);
        changed |= Widgets.Toggle("Other duties##other", ref settings.TrackOtherDuties, accent, scale);

        return changed;
    }

    // ---- Key hints ----

    private static bool DrawHints(Settings settings, Vector3 accent, float scale, float column)
    {
        var changed = false;

        Widgets.SectionTitle("Key hints", accent, scale);

        changed |= Widgets.Toggle("Show what to press for each action##hints", ref settings.ShowKeybinds, accent, scale);
        Widgets.Tooltip("Read live from your own hotbars and keybinds. An amber dash means the action is not on any of "
                        + "your hotbars; a position like 3-7 means hotbar 3, slot 7 has no key bound.", scale);

        Widgets.Label("Modifier style", column, scale);
        changed |= Widgets.EnumCombo("##hintstyle", ref settings.KeybindStyle, v => v switch
        {
            KeybindStyle.Auto => "Automatic (ctrl 1 in the full panel, c 1 in the compact strip)",
            KeybindStyle.Full => "Always spelled out (ctrl 1)",
            _ => "Always letters, like the game's hotbars (c 1)"
        });

        Widgets.Label("Read hints from", column, scale);
        changed |= Widgets.EnumCombo("##hintsource", ref settings.HintSource, v => v switch
        {
            HintSource.Auto => "Automatic (follows your gamepad mode)",
            HintSource.Keyboard => "Hotbars and keyboard",
            _ => "Cross hotbar"
        });

        Widgets.Label("Controller labels", column, scale);
        changed |= Widgets.EnumCombo("##pad", ref settings.PadGlyphs, v => v switch
        {
            PadGlyphs.Xbox => "LT / RT",
            _ => "L2 / R2"
        });

        return changed;
    }

    // ---- Practice ----

    private bool DrawPractice(Settings settings, Vector3 accent, float scale, float column)
    {
        var changed = false;

        Widgets.SectionTitle("Mistakes", accent, scale);

        Widgets.Label("When I press the wrong thing", column, scale,
                      "Gentle: the ring around the expected action warms for a moment and what you pressed appears "
                      + "small beside it. Nothing moves and nothing is covered, so you can keep going.\n\n"
                      + "Strong: a red flash, a shake and the wrong icon crossed out on the step itself.\n\n"
                      + "Nothing: no cue while you play; mistakes only show in the result.");
        changed |= Widgets.EnumCombo("##feedback", ref settings.MistakeFeedback, v => v switch
        {
            FeedbackStyle.Gentle => "Gentle cue",
            FeedbackStyle.Strong => "Strong cue",
            _ => "Nothing while playing"
        });

        Widgets.Label("Sound on a wrong press", column, scale, "One of the game's own macro sounds, at your sound effect volume.");
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

        changed |= Widgets.Toggle("Stop at the first mistake##strict", ref settings.StopAtFirstMistake, accent, scale);
        Widgets.Tooltip("Off (recommended): a wrong press is noted and you carry on; a step you skipped is marked and "
                        + "the timeline jumps ahead with you.", scale);

        changed |= Widgets.Toggle("Ignore Sprint and role actions that are not in the opener##utility", ref settings.IgnoreUtilityActions,
                                  accent, scale);
        Widgets.Tooltip("True North, Second Wind, Arm's Length and friends never count as mistakes.", scale);

        Widgets.SectionTitle("Helpers", accent, scale);

        changed |= Widgets.Toggle("Fit openers to my level##levels", ref settings.AutoAdjustLevel, accent, scale);
        Widgets.Tooltip("Below the level an opener was written for - synced content, an old Ultimate, a job you are still "
                        + "levelling - actions you do not have are left out and upgraded ones fall back to the version "
                        + "you do have. An approximation, but it keeps the timeline playable.", scale);

        changed |= Widgets.Toggle("Highlight the next action on my hotbar##ants", ref settings.AbilityAnts, accent, scale);
        Widgets.Tooltip("Uses the game's own proc highlight (the dotted outline).", scale);

        Widgets.Label("Give up after no input for", column, scale);
        changed |= ImGui.SliderFloat("##idle", ref settings.IdleTimeoutSeconds, 5f, 60f, "%.0f s");

        Widgets.Label("Countdown length", column, scale, "Start one with /opener cd, from the Results tab or the timeline's menu.");
        changed |= ImGui.SliderInt("##countdown", ref settings.CountdownSeconds, 3, 30, "%d s");

        changed |= Widgets.Toggle("Large countdown numbers in the middle of the screen##bigcd", ref settings.BigCountdown, accent, scale);

        return changed;
    }

    private static string SoundLabel(int sound)
    {
        return sound <= 0 ? "Off" : $"Sound effect {sound}  (<se.{sound}>)";
    }

    // ---- Appearance ----

    private bool DrawAppearance(Settings settings, Vector3 accent, float scale, float column)
    {
        var changed = false;

        Widgets.SectionTitle("Presets", accent, scale);
        changed |= DrawPresets(settings, accent, scale);

        Widgets.SectionTitle("Custom", accent, scale);

        Widgets.Label("Timeline opacity", column, scale, "How dark the panel behind the icons is.");
        var panelPercent = settings.PanelOpacity * 100f;
        if (ImGui.SliderFloat("##panel", ref panelPercent, 0f, 100f, "%.0f%%"))
        {
            settings.PanelOpacity = panelPercent / 100f;
            changed = true;
        }

        Widgets.Label("Background blur", column, scale, "Acrylic: blurs the game behind the timeline and this window. 0 turns it off.");
        var blurPercent = settings.Blur * 100f;
        if (ImGui.SliderFloat("##blur", ref blurPercent, 0f, 100f, "%.0f%%"))
        {
            settings.Blur = blurPercent / 100f;
            changed = true;
        }

        Widgets.Label("Window opacity", column, scale);
        var windowPercent = settings.WindowOpacity * 100f;
        if (ImGui.SliderFloat("##window", ref windowPercent, 30f, 100f, "%.0f%%"))
        {
            settings.WindowOpacity = windowPercent / 100f;
            changed = true;
        }

        Widgets.SectionTitle("Accent colour", accent, scale);
        changed |= DrawAccent(settings, scale);

        return changed;
    }

    private static bool DrawPresets(Settings settings, Vector3 accent, float scale)
    {
        var changed = false;
        var presets = Appearance.Presets;
        var spacing = 8f * scale;
        var available = ImGui.GetContentRegionAvail().X;
        var width = MathF.Floor((available - spacing * (presets.Length - 1)) / presets.Length);
        var height = MathF.Round(84f * scale);
        var dl = ImGui.GetWindowDrawList();

        for (var i = 0; i < presets.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0f, spacing);

            var preset = presets[i];
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(width, height);
            if (ImGui.InvisibleButton($"##preset{i}", new Vector2(width, height)))
            {
                preset.Apply(settings);
                changed = true;
            }

            var hovered = ImGui.IsItemHovered();
            Widgets.Tooltip(preset.Description, scale);
            var selected = preset.Matches(settings);
            var rounding = 8f * scale;

            dl.AddRectFilled(min, max, Palette.Pack(Palette.White, hovered ? 0.10f : 0.05f), rounding);
            if (selected)
                dl.AddRect(min, max, Palette.Pack(accent, 0.95f), rounding, MathF.Max(1.5f, 2f * scale));

            // A swatch: some "scenery" with a panel over it, so the opacity and blur can be judged.
            var pad = 8f * scale;
            var sampleMin = min + new Vector2(pad, pad);
            var sampleMax = new Vector2(max.X - pad, min.Y + height * 0.58f);
            DrawPresetSample(dl, sampleMin, sampleMax, preset, accent, scale);

            var nameSize = ImGui.CalcTextSize(preset.Name);
            dl.AddText(new Vector2(MathF.Round((min.X + max.X - nameSize.X) * 0.5f), MathF.Round(sampleMax.Y + (max.Y - sampleMax.Y - nameSize.Y) * 0.5f)),
                       Palette.Pack(selected ? Palette.Mix(accent, Palette.White, 0.4f) : Palette.White, selected ? 1f : 0.8f), preset.Name);
        }

        return changed;
    }

    private static void DrawPresetSample(ImDrawListPtr dl, Vector2 min, Vector2 max, in AppearancePreset preset, Vector3 accent, float scale)
    {
        var rounding = 5f * scale;
        var width = max.X - min.X;

        // Scenery: a few bright bands. Blur is faked by letting them bleed towards their average.
        Span<Vector3> bands = [Palette.Rgb(0x6F, 0xA8, 0x6A), Palette.Rgb(0xE9, 0xC4, 0x7A), Palette.Rgb(0x5B, 0x8F, 0xC9), Palette.Rgb(0xD9, 0x8C, 0x8C)];
        var average = (bands[0] + bands[1] + bands[2] + bands[3]) * 0.25f;
        dl.PushClipRect(min, max, true);
        dl.AddRectFilled(min, max, Palette.Pack(average), rounding);
        var bandWidth = width / bands.Length;
        for (var b = 0; b < bands.Length; b++)
        {
            var color = Palette.Mix(bands[b], average, preset.Blur * 0.9f);
            var skew = (max.Y - min.Y) * 0.6f;
            var x0 = min.X + b * bandWidth;
            dl.AddQuadFilled(new Vector2(x0 + skew, min.Y), new Vector2(x0 + bandWidth + skew, min.Y), new Vector2(x0 + bandWidth - skew, max.Y),
                             new Vector2(x0 - skew, max.Y), Palette.Pack(color));
        }

        dl.PopClipRect();

        // The panel, with three stand-in icons.
        var inset = 5f * scale;
        var panelMin = min + new Vector2(inset, inset);
        var panelMax = max - new Vector2(inset, inset);
        dl.AddRectFilled(panelMin, panelMax, Palette.Pack(Palette.Surface, preset.PanelOpacity), rounding);
        dl.AddRect(panelMin, panelMax, Palette.Pack(Palette.White, 0.14f), rounding, 1f);

        var icon = MathF.Min((panelMax.Y - panelMin.Y) - 8f * scale, 14f * scale);
        var gap = 4f * scale;
        var x = MathF.Round((panelMin.X + panelMax.X) * 0.5f - (icon * 3f + gap * 2f) * 0.5f);
        var y = MathF.Round((panelMin.Y + panelMax.Y - icon) * 0.5f);
        for (var k = 0; k < 3; k++)
        {
            var color = k == 0 ? accent : Palette.White;
            dl.AddRectFilled(new Vector2(x, y), new Vector2(x + icon, y + icon), Palette.Pack(color, k == 0 ? 0.95f : 0.55f), 3f * scale);
            x += icon + gap;
        }
    }

    private bool DrawAccent(Settings settings, float scale)
    {
        var changed = false;
        var dl = ImGui.GetWindowDrawList();
        var radius = MathF.Round(12f * scale);
        var spacing = 9f * scale;

        for (var i = 0; i < Appearance.Accents.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0f, spacing);

            var swatch = Appearance.Accents[i];
            var min = ImGui.GetCursorScreenPos();
            var size = new Vector2(radius * 2f);
            if (ImGui.InvisibleButton($"##accent{i}", size))
            {
                settings.Accent = swatch.Color;
                changed = true;
            }

            var hovered = ImGui.IsItemHovered();
            Widgets.Tooltip($"{swatch.Name}  {Appearance.ToHex(swatch.Color)}", scale);

            var center = min + size * 0.5f;
            var selected = Vector3.DistanceSquared(settings.Accent, swatch.Color) < 0.0004f;
            dl.AddCircleFilled(center, radius - (selected || hovered ? 2f * scale : 3f * scale), Palette.Pack(swatch.Color), 28);
            if (selected)
                dl.AddCircle(center, radius, Palette.Pack(Palette.White, 0.95f), 28, MathF.Max(1.5f, 2f * scale));
        }

        ImGui.Dummy(new Vector2(0f, 2f * scale));

        // Any other colour: type its hex code or pick it.
        if (settings.Accent != hexFor)
        {
            hexFor = settings.Accent;
            hexText = Appearance.ToHex(settings.Accent);
        }

        ImGui.AlignTextToFramePadding();
        Widgets.TextDim("Hex");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f * scale);
        if (ImGui.InputText("##hex", ref hexText, 8) && Appearance.TryParseHex(hexText, out var typed))
        {
            settings.Accent = typed;
            hexFor = typed; // leave what is being typed alone
            changed = true;
        }

        ImGui.SameLine();
        var picked = settings.Accent;
        if (ImGui.ColorEdit3("##picker", ref picked, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
        {
            settings.Accent = picked;
            changed = true;
        }

        Widgets.Tooltip("Pick any colour.", scale);

        ImGui.SameLine();
        Widgets.TextFaint("The first swatch is Material UI's default accent.");

        return changed;
    }

    // ---- About ----

    private void DrawAbout(Vector3 accent, float scale)
    {
        Widgets.SectionTitle("About", accent, scale);
        Widgets.Note($"Opener Timeline {host.Version}. A fork of OpenerCreator by Infi, herulume and sevii, rebuilt around a live "
                     + "timeline. The bundled openers are the ones published by The Balance. Licensed AGPL-3.0-or-later.");

        ImGui.Dummy(new Vector2(0f, 2f * scale));
        if (Widgets.PrimaryButton("How to use", accent))
            showGuide();
        ImGui.SameLine();
        if (ImGui.Button("OpenerCreator (original)"))
            host.OpenLink("https://github.com/Infiziert90/OpenerCreator");
        ImGui.SameLine();
        if (ImGui.Button("The Balance"))
            host.OpenLink("https://www.thebalanceffxiv.com/");

        Widgets.SectionTitle("Commands", accent, scale);
        Command("/opener", "Open this window");
        Command("/opener guide", "Open the walkthrough");
        Command("/opener settings", "Open the settings");
        Command("/opener unlock  |  lock", "Free the timeline to move it, or pin it");
        Command("/opener view full | compact | auto", "Switch between the full panel and the compact strip");
        Command("/opener toggle", "Show or hide the timeline");
        Command("/opener reset", "Abandon the current attempt");
        Command("/opener cd [seconds]", "Start a private practice countdown");

        Widgets.SectionTitle("A note on the numbers", accent, scale);
        Widgets.Note("The Balance publishes its openers as pictures, so there is nothing for a plugin to download and keep in "
                     + "sync. The times and GCD numbers on the timeline are worked out from the game's own data and your "
                     + "current GCD speed instead. Treat them as a plan, not a promise.");
    }

    private void Command(string command, string description)
    {
        ImGui.TextUnformatted(command);
        ImGui.SameLine(300f * host.Scale);
        Widgets.TextDim(description);
    }
}
