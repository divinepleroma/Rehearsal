using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using OpenerTimeline.Training;
using OpenerTimeline.UI.Timeline;

namespace OpenerTimeline.UI.Views;

/// <summary>How the attempts went: the live one, the last result in detail, and recent history.</summary>
public sealed class ResultsView(IAppHost host)
{
    private int selected;

    public void Draw()
    {
        var scale = host.Scale;
        var accent = host.Settings.Accent;
        var trainer = host.Trainer;

        DrawCurrent(trainer, scale, accent);

        var history = trainer.History;
        if (history.Count == 0)
        {
            ImGui.Dummy(new Vector2(0f, 10f * scale));
            Widgets.Note("No finished attempts yet. Pick an opener, stand at a striking dummy and press its first action: "
                         + "the timeline follows along and the result shows up here.");
            return;
        }

        selected = Math.Clamp(selected, 0, history.Count - 1);

        var available = ImGui.GetContentRegionAvail();
        var listWidth = MathF.Min(300f * scale, available.X * 0.38f);

        if (ImGui.BeginChild("##history", new Vector2(listWidth, 0f), false))
        {
            Widgets.SectionTitle("Recent attempts", accent, scale);
            for (var i = 0; i < history.Count; i++)
                DrawHistoryRow(history[i], i, scale, accent);
        }

        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("##detail", Vector2.Zero, false))
            DrawDetail(history[selected], scale, accent);

        ImGui.EndChild();
    }

    private void DrawCurrent(ITrainer trainer, float scale, Vector3 accent)
    {
        Widgets.BeginCard("##current", new Vector2(0f, MathF.Round(58f * scale)), scale);

        var session = trainer.Session;
        var total = trainer.ActiveActions.Count;
        ImGui.SetCursorPosY(MathF.Round((58f * scale - ImGui.GetFrameHeight()) * 0.5f));
        ImGui.AlignTextToFramePadding();

        if (total == 0)
        {
            Widgets.TextDim("No opener selected for the job you are on.");
        }
        else
        {
            ImGui.TextUnformatted(trainer.ActiveTitle);
            ImGui.SameLine();
            var state = session?.State ?? RunState.Armed;
            var text = state switch
            {
                RunState.Running => $"In progress  -  step {Math.Min(total, session!.CurrentIndex + 1)} of {total}",
                RunState.Finished => "Finished  -  waiting for you to stop before arming again",
                _ => trainer.CountdownRemaining >= 0f ? $"Pull in {trainer.CountdownRemaining:0.0}" : "Ready  -  press the first action to start"
            };
            Widgets.TextDim(text);

            var style = ImGui.GetStyle();
            var resetWidth = ImGui.CalcTextSize("Reset run").X + style.FramePadding.X * 2f;
            var countdownLabel = trainer.CountdownRemaining >= 0f ? "Stop countdown" : "Start countdown";
            var countdownWidth = ImGui.CalcTextSize(countdownLabel).X + style.FramePadding.X * 2f;
            ImGui.SameLine(ImGui.GetWindowWidth() - resetWidth - countdownWidth - style.ItemSpacing.X - 12f * scale);

            if (ImGui.Button(countdownLabel))
            {
                if (trainer.CountdownRemaining >= 0f)
                    trainer.StopCountdown();
                else
                    trainer.StartCountdown();
            }

            Widgets.Tooltip("A practice countdown, like a pull timer. Only you can see it.", scale);

            ImGui.SameLine();
            if (ImGui.Button("Reset run"))
                trainer.Reset();
            Widgets.Tooltip("Abandon the current attempt and wait for a new one. Also: /opener reset", scale);
        }

        Widgets.EndCard();
    }

    private void DrawHistoryRow(RunRecord run, int index, float scale, Vector3 accent)
    {
        var height = MathF.Round(46f * scale);
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        if (ImGui.InvisibleButton($"##run{index}", new Vector2(width, height)))
            selected = index;

        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        var max = min + new Vector2(width, height);
        var isSelected = index == selected;
        if (isSelected || hovered)
            dl.AddRectFilled(min, max, isSelected ? Palette.Pack(accent, 0.16f) : Palette.Pack(Palette.White, 0.06f), 7f * scale);

        var color = ResultColor(run);
        dl.AddCircleFilled(min + new Vector2(14f * scale, height * 0.5f), 4f * scale, Palette.Pack(color), 14);

        var x = min.X + 28f * scale;
        dl.PushClipRect(min, max - new Vector2(8f * scale, 0f), true);
        dl.AddText(new Vector2(x, min.Y + 5f * scale), Palette.Pack(Palette.White, Palette.TextAlpha), $"{run.Job}  {run.Title}");
        using (host.PushFont(TimelineFont.Small))
        {
            var detail = $"{run.When:HH:mm}  -  {ResultText(run)}  -  {run.Duration:0.0}s";
            dl.AddText(new Vector2(x, max.Y - ImGui.GetTextLineHeight() - 6f * scale), Palette.Pack(Palette.White, Palette.TextDimAlpha), detail);
        }

        dl.PopClipRect();
    }

    private void DrawDetail(RunRecord run, float scale, Vector3 accent)
    {
        Widgets.SectionTitle($"{run.Job}  -  {run.Title}", accent, scale);

        // Headline numbers.
        var color = ResultColor(run);
        Stat("Result", ResultText(run), color, scale);
        ImGui.SameLine(0f, 28f * scale);
        Stat("Clean steps", $"{run.Clean} / {run.Total}", Palette.White, scale);
        ImGui.SameLine(0f, 28f * scale);
        Stat("Accuracy", $"{run.Accuracy * 100f:0}%", Palette.White, scale);
        ImGui.SameLine(0f, 28f * scale);
        Stat("Time", $"{run.Duration:0.0}s", Palette.White, scale);

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        // The whole sequence with what happened to each step.
        DrawSequence(run, scale);

        ImGui.Dummy(new Vector2(0f, 4f * scale));

        if (run.Mistakes.Count == 0)
        {
            Widgets.Note(run.Reason == FinishReason.Completed
                             ? "Every step in order, nothing extra. That is the opener."
                             : "No mistakes in the part that was played.");
            return;
        }

        Widgets.SectionTitle("What went wrong", accent, scale);
        var dl = ImGui.GetWindowDrawList();
        var icon = MathF.Round(26f * scale);

        for (var i = 0; i < run.Mistakes.Count; i++)
        {
            var mistake = run.Mistakes[i];
            if (mistake.StepIndex < 0 || mistake.StepIndex >= run.Opener.Count)
                continue;

            var expected = TimelineFrameBuilder.Describe(host.Catalog, run.Opener[mistake.StepIndex]);
            var rowMin = ImGui.GetCursorScreenPos();
            var rowHeight = icon + 8f * scale;
            var centerY = rowMin.Y + rowHeight * 0.5f;
            var x = rowMin.X + 4f * scale;

            float Text(string text, uint textColor)
            {
                var size = ImGui.CalcTextSize(text);
                dl.AddText(new Vector2(x, centerY - size.Y * 0.5f), textColor, text);
                return size.X;
            }

            void Icon(in TimelineStep step)
            {
                var min = new Vector2(x, centerY - icon * 0.5f);
                IconPainter.Draw(dl, host, step, min, min + new Vector2(icon), 1f, icon * IconPainter.RoundingRatio);
                x += icon + 6f * scale;
            }

            var dim = Palette.Pack(Palette.White, Palette.TextDimAlpha);
            var normal = Palette.Pack(Palette.White, Palette.TextAlpha);

            x += Text($"{mistake.Time,5:0.0}s", dim) + 10f * scale;
            x += Text($"Step {mistake.StepIndex + 1}", dim) + 10f * scale;

            if (mistake.Kind == MistakeKind.Skipped)
            {
                x += Text("skipped", Palette.Pack(Palette.Warn)) + 8f * scale;
                Icon(expected);
                Text(expected.Name, normal);
            }
            else
            {
                var pressed = TimelineFrameBuilder.Describe(host.Catalog, (int)mistake.PressedId);
                x += Text("pressed", Palette.Pack(Palette.Bad)) + 8f * scale;
                Icon(pressed);
                x += Text(pressed.Name, normal) + 10f * scale;
                x += Text("instead of", dim) + 8f * scale;
                Icon(expected);
                Text(expected.Name, normal);
            }

            ImGui.Dummy(new Vector2(0f, rowHeight));
        }
    }

    /// <summary>The opener as a wrapped grid of icons, each marked with how it went.</summary>
    private void DrawSequence(RunRecord run, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var size = MathF.Round(34f * scale);
        var gap = MathF.Round(6f * scale);
        var perRow = Math.Max(1, (int)((available + gap) / (size + gap)));
        var rows = (run.Opener.Count + perRow - 1) / perRow;

        for (var i = 0; i < run.Opener.Count; i++)
        {
            var step = TimelineFrameBuilder.Describe(host.Catalog, run.Opener[i]);
            var min = origin + new Vector2(i % perRow * (size + gap), i / perRow * (size + gap));
            var max = min + new Vector2(size);
            var rounding = size * IconPainter.RoundingRatio;
            var result = i < run.Steps.Count ? run.Steps[i] : null;

            var alpha = result?.Status switch
            {
                StepStatus.Hit => 1f,
                StepStatus.Skipped => 0.4f,
                _ => 0.25f
            };
            IconPainter.Draw(dl, host, step, min, max, alpha, rounding);

            if (result is { Status: StepStatus.Hit, WrongPresses: > 0 })
                dl.AddRect(min, max, Palette.Pack(Palette.Bad), rounding, 2f);
            else if (result is { Status: StepStatus.Skipped })
            {
                dl.AddRect(min, max, Palette.Pack(Palette.Warn, 0.9f), rounding, 2f);
                dl.AddLine(new Vector2(min.X + 4f, max.Y - 4f), new Vector2(max.X - 4f, min.Y + 4f), Palette.Pack(Palette.Warn, 0.9f), 2f);
            }
            else
                dl.AddRect(min, max, Palette.Pack(Palette.White, 0.14f), rounding, 1f);

            ImGui.SetCursorScreenPos(min);
            ImGui.Dummy(new Vector2(size));
            Widgets.StepTooltip(step, scale);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(available, rows * (size + gap)));
    }

    private void Stat(string label, string value, Vector3 color, float scale)
    {
        ImGui.BeginGroup();
        using (host.PushFont(TimelineFont.Small))
            Widgets.TextFaint(label.ToUpperInvariant());
        ImGui.TextColored(new Vector4(color, Palette.TextAlpha), value);
        ImGui.EndGroup();
    }

    private static Vector3 ResultColor(RunRecord run)
    {
        return run.Reason switch
        {
            FinishReason.Completed when run.Mistakes.Count == 0 => Palette.Good,
            FinishReason.Completed or FinishReason.StoppedOnMistake => Palette.Bad,
            _ => Palette.Warn
        };
    }

    private static string ResultText(RunRecord run)
    {
        return run.Reason switch
        {
            FinishReason.Completed when run.Mistakes.Count == 0 => "Clean run",
            FinishReason.Completed when run.Mistakes.Count == 1 => "1 mistake",
            FinishReason.Completed => $"{run.Mistakes.Count} mistakes",
            FinishReason.StoppedOnMistake => "Stopped at first mistake",
            FinishReason.CombatEnded => "Combat ended early",
            FinishReason.TimedOut => "Abandoned",
            _ => "Reset"
        };
    }
}
