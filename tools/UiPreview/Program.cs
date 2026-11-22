using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Lumina;
using Lumina.Data;
using OpenerTimeline.Data;

namespace OpenerTimeline.Preview;

internal static class Program
{
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options == null)
        {
            Console.WriteLine("usage: UiPreview [--game <sqpack dir>] [--out <dir>] [--bg <image>] [--scale <ui scale>] [--font <ttf/otf>] [--data]");
            return 1;
        }

        Console.WriteLine($"game data : {options.SqPack}");
        Console.WriteLine($"output    : {options.OutDir}");

        var gameData = new GameData(options.SqPack, new LuminaOptions { PanicOnSheetChecksumMismatch = false });
        var upgrades = new UpgradeSource(gameData.GetExcelSheet<Lumina.Excel.Sheets.Action>(Language.English)!,
                                         gameData.GetExcelSheet<Lumina.Excel.Sheets.Trait>()!,
                                         gameData.GetExcelSheet<Lumina.Excel.Sheets.TraitTransient>(Language.English)!);
        var catalog = new ActionCatalog(gameData.GetExcelSheet<Lumina.Excel.Sheets.Action>()!, upgrades,
                                        (m, e) => Console.WriteLine($"{m}: {e}"));
        var library = new OpenerLibrary(options.OpenersFile, null, (m, e) => Console.WriteLine($"{m}: {e.Message}"));
        Console.WriteLine($"actions   : {catalog.Count}, upgrades: {catalog.UpgradeCount}, openers: {library.All.Count()}");

        if (options.DataReport)
        {
            DataReport.Print(catalog, library);
            return 0;
        }

        Directory.CreateDirectory(options.OutDir);
        using var background = options.Background != null && File.Exists(options.Background) ? new Bitmap(options.Background) : null;

        ImGui.SetCurrentContext(ImGui.CreateContext());
        var io = ImGui.GetIO();
        io.DeltaTime = 1f / 60f;
        unsafe
        {
            io.Handle->IniFilename = null;
        }

        var host = new PreviewHost(gameData, catalog, library, options.Scale);
        host.BuildFonts(options.Font);

        var stage = new Stage(host, background);
        TimelineScenes.Render(stage, Path.Combine(options.OutDir, "timeline.png"));
        WindowScenes.Render(stage, options.OutDir);

        Console.WriteLine("done");
        return 0;
    }
}

internal sealed class Options
{
    public string? Background;
    public bool DataReport;
    public string Font = string.Empty;
    public string OpenersFile = string.Empty;
    public string OutDir = string.Empty;
    public float Scale = 1f;
    public string SqPack = string.Empty;

    public static Options? Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string? Next()
            {
                return i + 1 < args.Length ? args[++i] : null;
            }

            switch (args[i])
            {
                case "--game":
                    options.SqPack = Next() ?? string.Empty;
                    break;
                case "--out":
                    options.OutDir = Next() ?? string.Empty;
                    break;
                case "--bg":
                    options.Background = Next();
                    break;
                case "--data":
                    options.DataReport = true;
                    break;
                case "--font":
                    options.Font = Next() ?? string.Empty;
                    break;
                case "--openers":
                    options.OpenersFile = Next() ?? string.Empty;
                    break;
                case "--scale":
                    if (!float.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out options.Scale))
                        return null;
                    break;
                default:
                    return null;
            }
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (options.SqPack.Length == 0)
            options.SqPack = FindSqPack(appData) ?? string.Empty;
        if (options.SqPack.Length == 0 || !Directory.Exists(options.SqPack))
        {
            Console.WriteLine("Could not find the game's sqpack directory; pass it with --game.");
            return null;
        }

        if (options.Font.Length == 0)
            options.Font = Path.Combine(appData, "XIVLauncher", "dalamudAssets", "dev", "UIRes", "NotoSansCJKjp-Medium.otf");
        if (!File.Exists(options.Font))
            options.Font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");

        var repo = FindRepoRoot();
        if (options.OpenersFile.Length == 0)
            options.OpenersFile = Path.Combine(repo, "Openers", "openers.json");
        if (options.OutDir.Length == 0)
            options.OutDir = Path.Combine(repo, "artifacts", "preview");

        return options;
    }

    /// <summary>Reads the install location XIVLauncher is configured with.</summary>
    private static string? FindSqPack(string appData)
    {
        try
        {
            var config = Path.Combine(appData, "XIVLauncher", "launcherConfigV3.json");
            if (!File.Exists(config))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(config));
            if (doc.RootElement.TryGetProperty("GamePath", out var path) && path.GetString() is { Length: > 0 } gamePath)
                return Path.Combine(gamePath, "game", "sqpack");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Could not read the launcher config: {e.Message}");
        }

        return null;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenerTimeline.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}

/// <summary>Runs ImGui frames for a scene and rasterises the last one over a piece of the background image.</summary>
internal sealed class Stage(PreviewHost host, Bitmap? background)
{
    public PreviewHost Host => host;
    public float Scale => host.Scale;

    /// <param name="width">Canvas width in pixels.</param>
    /// <param name="height">Canvas height in pixels.</param>
    /// <param name="backgroundOrigin">Which part of the background image ends up behind the scene.</param>
    /// <param name="draw">Submits the scene.</param>
    /// <param name="frames">ImGui needs a few frames before window layouts settle.</param>
    /// <param name="mouse">Where the cursor hovers, if anywhere.</param>
    public Canvas Render(int width, int height, Point backgroundOrigin, Action draw, int frames = 1, Vector2? mouse = null)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(width, height);
        io.MousePos = mouse ?? new Vector2(-10000f);

        for (var i = 0; i < frames; i++)
        {
            host.Backdrops.Clear();
            ImGui.NewFrame();
            ImGui.PushFont(host.DefaultFont);
            draw();
            ImGui.PopFont();
            ImGui.Render();
        }

        var canvas = new Canvas(width, height);
        if (background != null)
        {
            var x = Math.Clamp(backgroundOrigin.X, 0, Math.Max(0, background.Width - width));
            var y = Math.Clamp(backgroundOrigin.Y, 0, Math.Max(0, background.Height - height));
            canvas.DrawBackground(background, new Rectangle(x, y, Math.Min(width, background.Width), Math.Min(height, background.Height)));
        }
        else
        {
            canvas.Clear(new Vector3(0.22f, 0.27f, 0.24f));
        }

        foreach (var (min, max, rounding, strength) in host.Backdrops)
            canvas.Blur(min, max, rounding, Math.Max(1, (int)MathF.Round(strength * 16f * host.Scale)));

        canvas.Draw(ImGui.GetDrawData(), host.Textures);
        return canvas;
    }

    /// <summary>Stacks canvases vertically into one image.</summary>
    public static Canvas Stack(IReadOnlyList<Canvas> rows)
    {
        var sheet = new Canvas(rows.Max(r => r.Width), rows.Sum(r => r.Height));
        sheet.Clear(new Vector3(0.05f, 0.05f, 0.06f));
        var y = 0;
        foreach (var row in rows)
        {
            sheet.Blit(row, 0, y);
            y += row.Height;
        }

        return sheet;
    }
}
