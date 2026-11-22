using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using OpenerTimeline.Data;
using OpenerTimeline.Game;
using OpenerTimeline.UI.Views;
using OpenerTimeline.Windows;
using Action = Lumina.Excel.Sheets.Action;

namespace OpenerTimeline;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/opener";

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private readonly AbilityAntsHook abilityAnts;
    private readonly ActionEffectHook actionEffects;
    private readonly Countdown countdown;
    private readonly GameHost host;
    private readonly MainWindow mainWindow;
    private readonly Settings settings;
    private readonly Trainer trainer;
    private readonly WindowSystem windowSystem = new("OpenerTimeline");

    public Plugin()
    {
        void LogError(string message, Exception ex)
        {
            Log.Error(ex, message);
        }

        var settingsPath = PluginInterface.ConfigFile.FullName;
        settings = Settings.Load(settingsPath, LogError);

        var catalog = new ActionCatalog(DataManager.GetExcelSheet<Action>(), ReadUpgradeSource(), LogError);
        var library = new OpenerLibrary(Path.Combine(PluginInterface.AssemblyLocation.Directory!.FullName, "openers.json"),
                                        Path.Combine(PluginInterface.ConfigDirectory.FullName, "openers.json"), LogError);
        ImportOpenerCreatorOpeners(library, settingsPath, LogError);

        host = new GameHost(settings, settingsPath, catalog, library);
        abilityAnts = new AbilityAntsHook();
        countdown = new Countdown();
        var hints = new HotbarHints(catalog, settings, abilityAnts.IsHighlightedByGame);
        trainer = new Trainer(settings, catalog, library, hints, abilityAnts, countdown, host.SaveSettings, host.PlaySound);
        host.Attach(trainer);
        actionEffects = new ActionEffectHook(trainer.OnActionUsed);

        mainWindow = new MainWindow(host);
        windowSystem.AddWindow(mainWindow);

        // The two views are separate windows so each keeps its own place on the screen.
        windowSystem.AddWindow(new TimelineOverlay(host, trainer, false, mainWindow.Show));
        windowSystem.AddWindow(new TimelineOverlay(host, trainer, true, mainWindow.Show));

        // And the small tab that stands in for them while the timeline is minimized.
        windowSystem.AddWindow(new MinimizedTab(host, trainer, mainWindow.Show));

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the opener library.\n"
                          + "/opener guide → open the walkthrough\n"
                          + "/opener settings → open the settings\n"
                          + "/opener show | min | hide → show the timeline, minimize it to a small tab, or hide it\n"
                          + "/opener toggle → flip between shown and hidden\n"
                          + "/opener lock | unlock → pin the timeline, or free it so it can be moved\n"
                          + "/opener view full | compact | auto → switch between the full panel and the compact strip\n"
                          + "/opener reset → abandon the current attempt\n"
                          + "/opener cd [seconds] → start a practice countdown"
        });
    }

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static ITargetManager Targets { get; private set; } = null!;
    [PluginService] public static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    /// <summary>Monotonic time in seconds; the clock every part of the plugin shares.</summary>
    public static double Now => Clock.Elapsed.TotalSeconds;

    public void Dispose()
    {
        CommandManager.RemoveHandler(Command);

        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;

        windowSystem.RemoveAllWindows();
        actionEffects.Dispose();
        trainer.Dispose();
        abilityAnts.Dispose();
        host.Dispose();
    }

    private void Draw()
    {
        if (!settings.GuideShown && PlayerState.IsLoaded)
        {
            // Once, the first time: open the walkthrough rather than leave a stray panel unexplained.
            settings.GuideShown = true;
            settings.Welcomed = true;
            host.SaveSettings();
            mainWindow.Show(MainView.Section.Guide);
            Chat.Print("[Opener Timeline] Opened the walkthrough. You can get back to it any time with /opener guide.");
        }

        host.FlushSettings();
        windowSystem.Draw();

        if (settings.BigCountdown)
            countdown.DrawLarge();
    }

    private void OpenMainUi()
    {
        mainWindow.Show(MainView.Section.Openers);
    }

    private void OpenConfigUi()
    {
        mainWindow.Show(MainView.Section.Settings);
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        switch (parts.FirstOrDefault()?.ToLowerInvariant())
        {
            case null:
                mainWindow.Toggle();
                break;
            case "config" or "settings":
                mainWindow.Show(MainView.Section.Settings);
                break;
            case "guide" or "help" or "howto":
                mainWindow.Show(MainView.Section.Guide);
                break;
            case "results":
                mainWindow.Show(MainView.Section.Results);
                break;
            case "editor":
                mainWindow.Show(MainView.Section.Editor);
                break;
            case "lock":
                settings.OverlayLocked = true;
                host.SaveSettings();
                Chat.Print("[Opener Timeline] Timeline locked. /opener unlock frees it again.");
                break;
            case "unlock":
                // Unlocking is for moving it, so it has to be there to be moved.
                settings.OverlayLocked = false;
                settings.Presence = TimelinePresence.Shown;
                host.SaveSettings();
                Chat.Print("[Opener Timeline] Timeline unlocked: drag it into place, then /opener lock.");
                break;
            case "show":
                SetPresence(TimelinePresence.Shown);
                break;
            case "min" or "minimize" or "minimise":
                SetPresence(TimelinePresence.Minimized);
                break;
            case "hide":
                SetPresence(TimelinePresence.Hidden);
                break;
            case "toggle":
                // Coming back, it is the way it was left: minimized stays minimized.
                settings.OverlayEnabled = !settings.OverlayEnabled;
                host.SaveSettings();
                Chat.Print(settings.Presence switch
                {
                    TimelinePresence.Shown => "[Opener Timeline] Timeline shown.",
                    TimelinePresence.Minimized => "[Opener Timeline] Timeline back, minimized. Click its tab to open it.",
                    _ => "[Opener Timeline] Timeline hidden."
                });
                break;
            case "view":
                SetView(parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty);
                break;
            case "compact" or "full" or "expanded" or "auto":
                SetView(parts[0].ToLowerInvariant());
                break;
            case "reset":
                trainer.Reset();
                break;
            case "cd" or "countdown":
                if (parts.Length > 1 && int.TryParse(parts[1], out var seconds))
                {
                    settings.CountdownSeconds = Math.Clamp(seconds, 3, 30);
                    host.SaveSettings();
                }

                trainer.StartCountdown();
                break;
            case "stop":
                trainer.StopCountdown();
                break;
            default:
                Chat.Print("[Opener Timeline] Unknown option. Try: guide, settings, show, min, hide, toggle, lock, unlock, "
                           + "view full|compact|auto, reset, cd [seconds].");
                break;
        }
    }

    private void SetPresence(TimelinePresence presence)
    {
        settings.Presence = presence;
        host.SaveSettings();
        Chat.Print(presence switch
        {
            TimelinePresence.Shown => "[Opener Timeline] Timeline shown.",
            TimelinePresence.Minimized => "[Opener Timeline] Timeline minimized. Click its tab, or /opener show, to open it again.",
            _ => "[Opener Timeline] Timeline hidden. /opener show brings it back."
        });
    }

    private void SetView(string name)
    {
        TimelineView? view = name switch
        {
            "full" or "expanded" or "panel" => TimelineView.Expanded,
            "compact" or "strip" or "small" => TimelineView.Compact,
            "auto" or "automatic" => TimelineView.Automatic,
            // No argument: flip between the two.
            "" => settings.View == TimelineView.Compact ? TimelineView.Expanded : TimelineView.Compact,
            _ => null
        };

        if (view == null)
        {
            Chat.Print("[Opener Timeline] Views: full, compact, auto.");
            return;
        }

        settings.View = view.Value;
        host.SaveSettings();
        Chat.Print(view.Value switch
        {
            TimelineView.Expanded => "[Opener Timeline] Full panel.",
            TimelineView.Compact => "[Opener Timeline] Compact strip.",
            _ => "[Opener Timeline] Automatic: full panel before the pull, compact strip once it starts."
        });
    }

    /// <summary>
    /// The English text that says which action upgrades which. Clients without English data simply
    /// do without it.
    /// </summary>
    private static UpgradeSource? ReadUpgradeSource()
    {
        try
        {
            return new UpgradeSource(DataManager.GetExcelSheet<Action>(ClientLanguage.English), DataManager.GetExcelSheet<Trait>(),
                                     DataManager.GetExcelSheet<TraitTransient>(ClientLanguage.English));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "English action data is not available; level-adjusted openers will only drop actions");
            return null;
        }
    }

    /// <summary>
    /// Brings over openers saved with OpenerCreator the first time this plugin runs. The originals
    /// are only read, never changed.
    /// </summary>
    private void ImportOpenerCreatorOpeners(OpenerLibrary library, string settingsPath, Action<string, Exception> logError)
    {
        if (settings.ImportedLegacyOpeners)
            return;

        settings.ImportedLegacyOpeners = true;
        try
        {
            var configRoot = PluginInterface.ConfigDirectory.Parent;
            var legacy = configRoot == null ? null : Path.Combine(configRoot.FullName, "OpenerCreator", "openers.json");
            if (legacy != null && File.Exists(legacy))
            {
                var added = library.Import(legacy);
                if (added > 0)
                    Log.Information($"Imported {added} saved opener(s) from OpenerCreator");
            }
        }
        catch (Exception ex)
        {
            logError("Could not import openers from OpenerCreator", ex);
        }

        settings.Save(settingsPath, logError);
    }
}
