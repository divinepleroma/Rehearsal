using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using OpenerTimeline.Data;
using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline.UI.Views;

/// <summary>Browse the bundled and saved openers and pick the one to practise.</summary>
public sealed class LibraryView(IAppHost host, Action<OpenerRef> edit)
{
    private static readonly JobCategory[] Roles =
    [
        JobCategory.Tank, JobCategory.Healer, JobCategory.Melee, JobCategory.PhysicalRanged, JobCategory.MagicalRanged
    ];

    private readonly Dictionary<(OpenerRef Opener, int Level), Row> previews = new();
    private bool mineOnly = true;
    private JobCategory roles = JobCategory.None;
    private string search = string.Empty;

    /// <summary>Call when the library changed so previews are rebuilt.</summary>
    public void Invalidate()
    {
        previews.Clear();
    }

    public void Draw()
    {
        var scale = host.Scale;
        var accent = host.Settings.Accent;
        var job = host.CurrentJob;
        var hasOwn = job != Jobs.ANY && host.Library.ForJob(job).Any();

        // ---- Filters ----
        var mineLabel = job == Jobs.ANY ? "My job" : $"My job ({job})";
        if (Widgets.Chip(mineLabel + "##mine", mineOnly && hasOwn, accent, scale))
        {
            mineOnly = !(mineOnly && hasOwn);
            if (mineOnly)
                roles = JobCategory.None;
        }

        if (!hasOwn)
            Widgets.Tooltip("There are no openers for the job you are on, so everything is shown.", scale);

        foreach (var role in Roles)
        {
            ImGui.SameLine();
            var on = !(mineOnly && hasOwn) && (roles & role) != 0;
            if (Widgets.Chip(role.Label() + "##role", on, accent, scale))
            {
                roles = mineOnly && hasOwn ? role : JobsExtensions.Toggle(roles, role);
                mineOnly = false;
            }
        }

        ImGui.SameLine();
        var searchWidth = MathF.Min(240f * scale, MathF.Max(120f * scale, ImGui.GetContentRegionAvail().X));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - searchWidth));
        ImGui.SetNextItemWidth(searchWidth);
        ImGui.InputTextWithHint("##search", "Search openers", ref search, 48);

        ImGui.Dummy(new Vector2(0f, 2f * scale));

        // ---- List ----
        if (ImGui.BeginChild("##openers", Vector2.Zero, false))
        {
            var shown = 0;
            var lastJob = Jobs.ANY;
            foreach (var opener in host.Library.All.OrderBy(o => o.Job).ThenBy(o => !o.IsDefault))
            {
                if (!Matches(opener, job, hasOwn))
                    continue;

                if (opener.Job != lastJob)
                {
                    if (shown > 0)
                        ImGui.Dummy(new Vector2(0f, 6f * scale));
                    Widgets.Tag(opener.Job.ToString(), accent, scale);
                    ImGui.SameLine();
                    Widgets.TextDim(opener.Job.FullName());
                    lastJob = opener.Job;
                }

                DrawRow(opener, scale, accent);
                shown++;
            }

            if (shown == 0)
            {
                ImGui.Dummy(new Vector2(0f, 12f * scale));
                Widgets.Note(search.Length > 0
                                 ? "No opener matches that search."
                                 : "Nothing here yet. Build your own opener in the Editor tab.");
            }
        }

        ImGui.EndChild();
    }

    private bool Matches(OpenerRef opener, Jobs job, bool hasOwn)
    {
        if (mineOnly && hasOwn && opener.Job != job)
            return false;
        if (!(mineOnly && hasOwn) && roles != JobCategory.None && !JobsExtensions.FilterBy(roles, opener.Job))
            return false;
        if (search.Length == 0)
            return true;

        return opener.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || opener.Job.ToString().Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || opener.Job.FullName().Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void DrawRow(OpenerRef opener, float scale, Vector3 accent)
    {
        var trainer = host.Trainer;
        var active = trainer.Active == opener;       // on the timeline right now
        var selected = trainer.IsSelected(opener);   // chosen for its job
        var row = Preview(opener);
        var steps = row.Steps;
        var height = MathF.Round(64f * scale);

        Widgets.BeginCard($"##row{opener.Job}{opener.IsDefault}{opener.Name}", new Vector2(0f, height), scale, active);

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var pad = 12f * scale;

        if (active)
        {
            // The marker sits in the card's padding, which a child window clips away by default.
            dl.PushClipRect(min, min + size, false);
            dl.AddRectFilled(min + new Vector2(0f, 12f * scale), min + new Vector2(3f * scale, size.Y - 12f * scale),
                             Palette.Pack(accent), 2f * scale);
            dl.PopClipRect();
        }

        // Buttons, right-aligned.
        var style = ImGui.GetStyle();
        var practiceLabel = selected ? "Selected" : "Practice";
        var buttonHeight = ImGui.GetFrameHeight();
        float ButtonWidth(string label)
        {
            return ImGui.CalcTextSize(label).X + style.FramePadding.X * 2f;
        }

        var buttonsWidth = ButtonWidth(practiceLabel) + style.ItemSpacing.X + ButtonWidth("Edit");
        if (!opener.IsDefault)
            buttonsWidth += style.ItemSpacing.X + ButtonWidth("Delete");

        var buttonsX = size.X - pad - buttonsWidth;
        var buttonsY = MathF.Round((size.Y - buttonHeight) * 0.5f);

        // Name and details.
        var nameWidth = Math.Clamp(size.X * 0.27f, 150f * scale, 270f * scale);
        ImGui.SetCursorPos(new Vector2(pad + (active ? 4f * scale : 0f), 11f * scale));
        ImGui.PushClipRect(min, min + new Vector2(nameWidth + pad, size.Y), true);
        ImGui.TextUnformatted(opener.Name);
        ImGui.SetCursorPos(new Vector2(pad + (active ? 4f * scale : 0f), 11f * scale + ImGui.GetTextLineHeight() + 3f * scale));
        if (row.Fitted)
            Widgets.TextDim(row.Detail);
        else
            Widgets.TextFaint(row.Detail);
        ImGui.PopClipRect();

        // Miniature of the sequence.
        var stripX = pad + nameWidth + 10f * scale;
        var stripWidth = buttonsX - stripX - 46f * scale;
        var iconSize = MathF.Round(30f * scale);
        if (stripWidth > iconSize * 2f)
        {
            var origin = min + new Vector2(stripX, MathF.Round((size.Y - iconSize) * 0.5f));
            var hidden = IconPainter.DrawMiniStrip(dl, host, steps, origin, stripWidth, iconSize, 1f);
            if (hidden > 0)
            {
                var more = $"+{hidden}";
                var moreSize = ImGui.CalcTextSize(more);
                dl.AddText(new Vector2(min.X + buttonsX - 12f * scale - moreSize.X, min.Y + (size.Y - moreSize.Y) * 0.5f),
                           Palette.Pack(Palette.White, Palette.TextFaintAlpha), more);
            }
        }

        ImGui.SetCursorPos(new Vector2(buttonsX, buttonsY));
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(accent, 0.22f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(accent, 0.32f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(Palette.Mix(accent, Palette.White, 0.4f), 1f));
            if (ImGui.Button(practiceLabel))
                trainer.Deselect(opener.Job);
            ImGui.PopStyleColor(3);
            Widgets.Tooltip(active
                                ? "This is the opener on your timeline. Click to stop practising it."
                                : $"Chosen for {opener.Job}: the timeline shows it when you are on that job. Click to unselect.", scale);
        }
        else if (Widgets.PrimaryButton(practiceLabel, accent))
        {
            trainer.Select(opener);
        }

        ImGui.SameLine();
        if (ImGui.Button("Edit"))
            edit(opener);
        Widgets.Tooltip(opener.IsDefault ? "Open a copy in the editor." : "Open in the editor.", scale);

        if (!opener.IsDefault)
        {
            ImGui.SameLine();
            if (Widgets.DangerButton("Delete") && ImGui.GetIO().KeyCtrl)
            {
                if (selected)
                    trainer.Deselect(opener.Job);
                host.Library.Delete(opener.Name, opener.Job);
                Invalidate();
            }

            Widgets.Tooltip("Hold Ctrl and click to delete this opener.", scale);
        }

        Widgets.EndCard();
    }

    /// <summary>
    /// What a row shows. When the player is below the level an opener needs, and fitting is on, the
    /// row previews the fitted version - that is what the timeline will show.
    /// </summary>
    private Row Preview(OpenerRef opener)
    {
        var level = host.Settings.AutoAdjustLevel ? host.Trainer.Level : 0;
        if (previews.TryGetValue((opener, level), out var row))
            return row;

        if (!host.Library.TryGet(opener, out var actions))
        {
            row = new Row([], "This opener could not be read", false);
        }
        else
        {
            var catalog = host.Catalog;
            var source = opener.IsDefault ? "The Balance" : "Saved";
            var required = catalog.RequiredLevel(actions);
            var fitted = level > 0 && required > level ? catalog.Adapt(actions, level) : (AdaptedOpener?)null;

            if (fitted is { Changed: true } adapted)
            {
                var steps = TimelineFrameBuilder.Describe(catalog, adapted.Actions);
                var swapped = adapted.Replaced > 0 ? $", {adapted.Replaced} swapped" : string.Empty;
                row = new Row(steps, $"Fitted to Lv {level}: {adapted.Removed} left out{swapped}", true);
            }
            else
            {
                var steps = TimelineFrameBuilder.Describe(catalog, actions);
                row = new Row(steps, $"{steps.Length} actions  -  {steps.Count(s => s.IsGcd)} GCDs  -  Lv {required}  -  {source}", false);
            }
        }

        previews[(opener, level)] = row;
        return row;
    }

    private sealed record Row(TimelineStep[] Steps, string Detail, bool Fitted);
}
