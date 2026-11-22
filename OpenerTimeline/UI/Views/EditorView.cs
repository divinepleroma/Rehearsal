using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using OpenerTimeline.Data;
using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline.UI.Views;

/// <summary>Build or change an opener: search actions, click to append, drag to reorder.</summary>
public sealed class EditorView(IAppHost host, Action libraryChanged)
{
    private readonly List<int> results = [];
    private readonly List<int> working = [];
    private int dragging = -1;
    private Jobs job = Jobs.ANY;
    private bool jobChosen;
    private int level;
    private bool levelChosen;
    private string name = string.Empty;
    private string query = string.Empty;
    private bool resultsDirty = true;
    private string status = string.Empty;
    private double statusUntil;
    private TimelineStep[] steps = [];
    private bool stepsDirty = true;
    private ActionTypes type = ActionTypes.ANY;

    /// <summary>Opens a library opener for editing (as a copy: nothing changes until it is saved).</summary>
    public void Load(OpenerRef opener)
    {
        if (!host.Library.TryGet(opener, out var actions))
            return;

        working.Clear();
        working.AddRange(actions);
        name = opener.Name;
        job = opener.Job;
        jobChosen = true;
        stepsDirty = true;
        resultsDirty = true;
        dragging = -1;
    }

    public void Draw()
    {
        var scale = host.Scale;
        var accent = host.Settings.Accent;

        if (!jobChosen && host.CurrentJob != Jobs.ANY)
        {
            job = host.CurrentJob;
            jobChosen = true;
            resultsDirty = true;
        }

        // Someone below the cap most likely wants to build for the cap they are playing at.
        if (!levelChosen && host.Trainer.Level > 0)
        {
            var bracket = LevelAdapter.Bracket(host.Trainer.Level);
            level = bracket < host.Catalog.MaxLevel ? bracket : 0;
            levelChosen = true;
            resultsDirty = true;
        }

        if (stepsDirty)
        {
            steps = TimelineFrameBuilder.Describe(host.Catalog, working);
            stepsDirty = false;
        }

        DrawStrip(scale, accent);
        DrawToolbar(scale, accent);
        ImGui.Dummy(new Vector2(0f, 2f * scale));
        DrawSearch(scale, accent);
    }

    // ---- The opener being built ----

    private void DrawStrip(float scale, Vector3 accent)
    {
        var gcd = MathF.Round(44f * scale);
        var off = MathF.Round(gcd * 0.76f);
        var gap = MathF.Round(6f * scale);
        var drop = MathF.Round(off * 0.6f);
        var pad = MathF.Round(12f * scale);
        var height = pad * 2f + drop + gcd + ImGui.GetStyle().ScrollbarSize;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1f, 1f, 1f, 0.045f));
        var open = ImGui.BeginChild("##strip", new Vector2(0f, height), false, ImGuiWindowFlags.HorizontalScrollbar);
        ImGui.PopStyleColor();

        if (open)
        {
            var dl = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos() + new Vector2(pad, pad);

            if (working.Count == 0)
            {
                var hint = "Empty. Click actions below to add them in order.";
                var size = ImGui.CalcTextSize(hint);
                var area = ImGui.GetWindowSize();
                dl.AddText(ImGui.GetWindowPos() + new Vector2((area.X - size.X) * 0.5f, (height - ImGui.GetStyle().ScrollbarSize - size.Y) * 0.5f),
                           Palette.Pack(Palette.White, Palette.TextFaintAlpha), hint);
            }

            var railY = origin.Y + MathF.Round((off + drop) * 0.5f);
            var x = 0f;
            var remove = -1;
            Span<float> rightEdges = working.Count <= 128 ? stackalloc float[working.Count] : new float[working.Count];

            for (var i = 0; i < working.Count && i < steps.Length; i++)
            {
                var step = steps[i];
                var size = step.IsGcd ? gcd : off;
                var min = origin + new Vector2(x, step.IsGcd ? drop : 0f);
                var max = min + new Vector2(size);

                ImGui.SetCursorScreenPos(min);
                ImGui.InvisibleButton($"##step{i}", new Vector2(size));
                var hovered = ImGui.IsItemHovered();
                if (ImGui.IsItemActive() && dragging < 0 && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 4f * scale))
                    dragging = i;
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    remove = i;

                var rounding = size * IconPainter.RoundingRatio;
                IconPainter.Draw(dl, host, step, min, max, dragging == i ? 0.25f : 1f, rounding);
                dl.AddRect(min, max, hovered && dragging < 0 ? Palette.Pack(accent, 0.95f) : Palette.Pack(Palette.White, 0.16f), rounding,
                           hovered && dragging < 0 ? 2f : 1f);

                if (dragging < 0)
                {
                    using (host.PushFont(TimelineFont.Small))
                        Gfx.CooldownBadge(dl, min, max, step, TimelineStyle.Default with { Scale = scale }, 1f);
                    Widgets.StepTooltip(step, scale);
                }

                rightEdges[i] = min.X + size;
                x += size + gap;
            }

            if (working.Count > 0)
            {
                dl.AddLine(new Vector2(origin.X, railY), new Vector2(origin.X + x - gap, railY), Palette.Pack(Palette.White, 0.06f),
                           MathF.Max(1f, scale));
            }

            // Tell ImGui how wide the content is so the scrollbar is right.
            ImGui.SetCursorScreenPos(origin + new Vector2(x, 0f));
            ImGui.Dummy(new Vector2(pad, drop + gcd));

            if (remove >= 0 && dragging < 0)
            {
                working.RemoveAt(remove);
                stepsDirty = true;
            }

            if (dragging >= 0)
                HandleDrag(dl, origin, rightEdges, gap, drop + gcd, gcd, accent);
        }

        ImGui.EndChild();
    }

    private void HandleDrag(ImDrawListPtr dl, Vector2 origin, Span<float> rightEdges, float gap, float height, float iconSize, Vector3 accent)
    {
        if (dragging >= working.Count || dragging >= steps.Length)
        {
            dragging = -1;
            return;
        }

        // The insertion point is the first gap to the right of every icon the cursor has passed.
        var mouse = ImGui.GetMousePos();
        var insert = 0;
        for (var i = 0; i < rightEdges.Length; i++)
        {
            var width = steps[i].IsGcd ? iconSize : MathF.Round(iconSize * 0.76f);
            if (mouse.X > rightEdges[i] - width * 0.5f)
                insert = i + 1;
        }

        var caretX = insert == 0 ? origin.X - gap * 0.5f : rightEdges[insert - 1] + gap * 0.5f;
        dl.AddLine(new Vector2(caretX, origin.Y - 2f), new Vector2(caretX, origin.Y + height + 2f), Palette.Pack(accent), 2f);

        var ghost = iconSize * 0.9f;
        var gmin = mouse - new Vector2(ghost * 0.5f);
        IconPainter.Draw(ImGui.GetForegroundDrawList(), host, steps[dragging], gmin, gmin + new Vector2(ghost), 0.92f,
                         ghost * IconPainter.RoundingRatio);

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;

        var item = working[dragging];
        working.RemoveAt(dragging);
        if (insert > dragging)
            insert--;
        working.Insert(Math.Clamp(insert, 0, working.Count), item);
        dragging = -1;
        stepsDirty = true;
    }

    // ---- Name, job, save ----

    private void DrawToolbar(float scale, Vector3 accent)
    {
        Widgets.TextFaint(working.Count == 0
                              ? "Drag to reorder, right-click to remove."
                              : $"{working.Count} actions  -  drag to reorder, right-click to remove.");

        ImGui.SetNextItemWidth(230f * scale);
        ImGui.InputTextWithHint("##name", "Opener name", ref name, 48);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(96f * scale);
        if (Widgets.EnumCombo("##job", ref job, JobsExtensions.PrettyPrint))
        {
            jobChosen = true;
            resultsDirty = true;
        }

        Widgets.Tooltip("The job this opener belongs to. Also filters the action list below.", scale);

        ImGui.SameLine();
        var canSave = working.Count > 0 && job != Jobs.ANY && name.Trim().Length > 0;
        ImGui.BeginDisabled(!canSave);
        if (Widgets.PrimaryButton("Save", accent))
        {
            var trimmed = name.Trim();
            host.Library.Save(trimmed, job, working);
            libraryChanged();
            host.Trainer.Select(new OpenerRef(job, trimmed, false));
            Announce($"Saved \"{trimmed}\" and selected it for {job}.");
        }

        ImGui.EndDisabled();
        if (!canSave)
            Widgets.Tooltip("Add at least one action, pick a job and give the opener a name.", scale);

        ImGui.SameLine();
        ImGui.BeginDisabled(working.Count == 0);
        if (ImGui.Button("Practice unsaved"))
        {
            host.Trainer.Practice(name.Trim().Length > 0 ? name.Trim() : "Unsaved opener", job, [..working]);
            Announce("The timeline now shows this opener. It is not saved yet.");
        }

        ImGui.EndDisabled();
        Widgets.Tooltip("Try the opener on the timeline without saving it.", scale);

        ImGui.SameLine();
        ImGui.BeginDisabled(working.Count == 0);
        if (Widgets.DangerButton("Clear"))
        {
            working.Clear();
            stepsDirty = true;
        }

        ImGui.EndDisabled();

        if (status.Length > 0 && ImGui.GetTime() < statusUntil)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(new Vector4(Palette.Good, 1f), status);
        }
    }

    // ---- Action search ----

    private void DrawSearch(float scale, Vector3 accent)
    {
        ImGui.SetNextItemWidth(230f * scale);
        if (ImGui.InputTextWithHint("##query", "Search actions", ref query, 48))
            resultsDirty = true;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(96f * scale);
        if (Widgets.EnumCombo("##type", ref type, ActionTypesExtension.PrettyPrint))
            resultsDirty = true;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(118f * scale);
        if (ImGui.BeginCombo("##level", level == 0 ? "Any level" : $"Level {level}"))
        {
            if (ImGui.Selectable("Any level", level == 0))
                SetLevel(0);
            for (var i = LevelAdapter.LevelCaps.Length - 1; i >= 0; i--)
            {
                var cap = LevelAdapter.LevelCaps[i];
                if (cap <= host.Catalog.MaxLevel && ImGui.Selectable($"Level {cap}", level == cap))
                    SetLevel(cap);
            }

            ImGui.EndCombo();
        }

        Widgets.Tooltip("Only list actions you have at that level - for building an opener for older content.", scale);

        if (level > 0)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(working.Count == 0);
            if (ImGui.Button($"Fit to Lv {level}"))
            {
                var adapted = host.Catalog.Adapt(working, level);
                working.Clear();
                working.AddRange(adapted.Actions);
                stepsDirty = true;
                Announce(adapted.Changed
                             ? $"Fitted to level {level}: {adapted.Removed} left out, {adapted.Replaced} swapped. Check it over and save it under a new name."
                             : $"Everything in it is already available at level {level}.");
            }

            ImGui.EndDisabled();
            Widgets.Tooltip("Rewrites the opener above for this level: actions you would not have are removed, upgraded ones "
                            + "fall back to their older version. A starting point - adjust it by hand afterwards.", scale);
        }

        ImGui.SameLine();
        if (ImGui.Button("+ Any action"))
            Append(SpecialActions.CatchAll);
        Widgets.Tooltip("A step that accepts whatever you press. Useful for filler GCDs.", scale);

        ImGui.SameLine();
        if (ImGui.Button("+ Potion"))
            Append(SpecialActions.Potion);
        Widgets.Tooltip("A step that is satisfied by using any item, e.g. your tincture.", scale);

        if (resultsDirty)
        {
            results.Clear();
            results.AddRange(GroupOfActions.GetFilteredGroups(query, job, type));
            results.AddRange(host.Catalog.Search(query, job, type, level));
            resultsDirty = false;
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        Widgets.TextFaint(results.Count >= 200 ? "200+ results, refine the search" : $"{results.Count} results");

        if (ImGui.BeginChild("##results", Vector2.Zero, false))
        {
            var dl = ImGui.GetWindowDrawList();
            var spacing = 6f * scale;
            var tileHeight = MathF.Round(44f * scale);
            var available = ImGui.GetContentRegionAvail().X;
            var columns = Math.Max(1, (int)((available + spacing) / (220f * scale + spacing)));
            var tileWidth = MathF.Floor((available - spacing * (columns - 1)) / columns);
            var icon = MathF.Round(32f * scale);

            for (var i = 0; i < results.Count; i++)
            {
                if (i % columns != 0)
                    ImGui.SameLine(0f, spacing);

                var id = results[i];
                var step = TimelineFrameBuilder.Describe(host.Catalog, id);
                var min = ImGui.GetCursorScreenPos();
                var max = min + new Vector2(tileWidth, tileHeight);

                if (ImGui.InvisibleButton($"##result{id}", new Vector2(tileWidth, tileHeight)))
                    Append(id);
                var hovered = ImGui.IsItemHovered();
                Widgets.StepTooltip(step, scale);

                dl.AddRectFilled(min, max, Palette.Pack(Palette.White, hovered ? 0.11f : 0.04f), 7f * scale);
                if (hovered)
                    dl.AddRect(min, max, Palette.Pack(accent, 0.6f), 7f * scale, 1f);

                var iconMin = min + new Vector2(6f * scale, MathF.Round((tileHeight - icon) * 0.5f));
                IconPainter.Draw(dl, host, step, iconMin, iconMin + new Vector2(icon), 1f, icon * IconPainter.RoundingRatio);

                var textX = iconMin.X + icon + 8f * scale;
                dl.PushClipRect(new Vector2(textX, min.Y), new Vector2(max.X - 6f * scale, max.Y), true);
                dl.AddText(new Vector2(textX, min.Y + 5f * scale), Palette.Pack(Palette.White, Palette.TextAlpha), step.Name);
                using (host.PushFont(TimelineFont.Small))
                {
                    var kind = step.IsGcd ? "GCD" : "oGCD";
                    if (step.Recast >= 5f)
                        kind += $"  {step.Recast:0.#}s";
                    var kindColor = step.Cooldown switch
                    {
                        CooldownClass.OneMinute => Palette.Pack(Palette.OneMinute, 0.95f),
                        CooldownClass.TwoMinute => Palette.Pack(Palette.TwoMinute, 0.95f),
                        _ => Palette.Pack(Palette.White, Palette.TextFaintAlpha * 1.2f)
                    };
                    dl.AddText(new Vector2(textX, min.Y + tileHeight - ImGui.GetTextLineHeight() - 5f * scale), kindColor, kind);
                }

                dl.PopClipRect();
            }
        }

        ImGui.EndChild();
    }

    private void SetLevel(int value)
    {
        level = value;
        levelChosen = true;
        resultsDirty = true;
    }

    private void Append(int id)
    {
        working.Add(id);
        stepsDirty = true;
    }

    private void Announce(string message)
    {
        status = message;
        statusUntil = ImGui.GetTime() + 5d;
    }
}
