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
using Rehearsal.Data;
using Rehearsal.Game;
using Rehearsal.UI.Views;
using Rehearsal.Windows;
using Action = Lumina.Excel.Sheets.Action;

namespace Rehearsal;

public sealed class Plugin : IDalamudPlugin
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>The plugin this one grew out of, and the name it had itself before it was called Rehearsal.</summary>
    private static readonly string[] LegacyConfigFolders = ["OpenerCreator", "OpenerTimeline"];

    private readonly AbilityAntsHook abilityAnts;
    private readonly ActionEffectHook actionEffects;
    private readonly Countdown countdown;
    private readonly GameHost host;
    private readonly MainWindow mainWindow;
    private readonly Settings settings;
    private readonly bool shortCommand;
    private readonly Trainer trainer;
    private readonly WindowSystem windowSystem = new("Rehearsal");
    private bool setupOffered;

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
        ImportSavedOpeners(library, settingsPath, LogError);

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

        const string c = Branding.Command;
        CommandManager.AddHandler(c, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the opener library.\n"
                          + c + " setup → go through the first-run setup again\n"
                          + c + " guide → open the guide\n"
                          + c + " settings → open the settings\n"
                          + c + " show | min | hide → show the timeline, minimize it to a small tab, or hide it\n"
                          + c + " toggle → flip between shown and hidden\n"
                          + c + " lock | unlock → pin the timeline, or free it so it can be moved\n"
                          + c + " view full | compact | auto → switch between the full panel and the compact strip\n"
                          + c + " reset → abandon the current attempt\n"
                          + c + " cd [seconds] → start a practice countdown"
        });

        // The short form is a convenience only: another plugin may already own it, and then it is theirs.
        if (!CommandManager.Commands.ContainsKey(Branding.ShortCommand))
        {
            shortCommand = CommandManager.AddHandler(Branding.ShortCommand, new CommandInfo(OnCommand)
            {
                HelpMessage = "The same as " + c + "."
            });
        }
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
        CommandManager.RemoveHandler(Branding.Command);
        if (shortCommand)
            CommandManager.RemoveHandler(Branding.ShortCommand);

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
        if (!settings.SetupDone && !setupOffered && PlayerState.IsLoaded)
        {
            // Until it has been finished or skipped, the setup opens once per session: five short
            // choices, each already made, rather than a stray panel nobody explained.
            setupOffered = true;
            mainWindow.ShowSetup(false);
            Say("Welcome! A short setup has opened: five quick choices, all with sensible defaults. "
                + Branding.Command + " setup brings it back any time.");
        }

        host.FlushSettings();
        windowSystem.Draw();

        if (settings.BigCountdown)
            countdown.DrawLarge();
    }

    private void OpenMainUi()
    {
        // "Open" in the plugin installer, pressed right after installing, should not skip past the setup.
        if (settings.SetupDone)
            mainWindow.Show(MainView.Section.Openers);
        else
            mainWindow.ShowSetup(false);
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
            case "setup" or "welcome":
                mainWindow.ShowSetup();
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
                Say("Timeline locked. " + Branding.Command + " unlock frees it again.");
                break;
            case "unlock":
                // Unlocking is for moving it, so it has to be there to be moved.
                settings.OverlayLocked = false;
                settings.Presence = TimelinePresence.Shown;
                host.SaveSettings();
                Say("Timeline unlocked: drag it into place, then " + Branding.Command + " lock.");
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
                Say(settings.Presence switch
                {
                    TimelinePresence.Shown => "Timeline shown.",
                    TimelinePresence.Minimized => "Timeline back, minimized. Click its tab to open it.",
                    _ => "Timeline hidden."
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
                Say("Unknown option. Try: setup, guide, settings, show, min, hide, toggle, lock, unlock, "
                    + "view full|compact|auto, reset, cd [seconds].");
                break;
        }
    }

    private static void Say(string message)
    {
        Chat.Print(Branding.ChatTag + message);
    }

    private void SetPresence(TimelinePresence presence)
    {
        settings.Presence = presence;
        host.SaveSettings();
        Say(presence switch
        {
            TimelinePresence.Shown => "Timeline shown.",
            TimelinePresence.Minimized => "Timeline minimized. Click its tab, or " + Branding.Command + " show, to open it again.",
            _ => "Timeline hidden. " + Branding.Command + " show brings it back."
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
            Say("Views: full, compact, auto.");
            return;
        }

        settings.View = view.Value;
        host.SaveSettings();
        Say(view.Value switch
        {
            TimelineView.Expanded => "Full panel.",
            TimelineView.Compact => "Compact strip.",
            _ => "Automatic: full panel before the pull, compact strip once it starts."
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
    /// Brings over openers saved with OpenerCreator, or with this plugin under its earlier name,
    /// the first time it runs. The originals are only read, never changed.
    /// </summary>
    private void ImportSavedOpeners(OpenerLibrary library, string settingsPath, Action<string, Exception> logError)
    {
        if (settings.ImportedLegacyOpeners)
            return;

        settings.ImportedLegacyOpeners = true;
        var configRoot = PluginInterface.ConfigDirectory.Parent;
        foreach (var source in LegacyConfigFolders)
        {
            try
            {
                var legacy = configRoot == null ? null : Path.Combine(configRoot.FullName, source, "openers.json");
                if (legacy != null && File.Exists(legacy))
                {
                    var added = library.Import(legacy);
                    if (added > 0)
                        Log.Information($"Imported {added} saved opener(s) from {source}");
                }
            }
            catch (Exception ex)
            {
                logError($"Could not import openers from {source}", ex);
            }
        }

        settings.Save(settingsPath, logError);
    }
}
