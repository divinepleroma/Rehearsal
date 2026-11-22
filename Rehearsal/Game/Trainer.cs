using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Rehearsal.Data;
using TerritoryType = Lumina.Excel.Sheets.TerritoryType;
using Rehearsal.Training;
using Rehearsal.UI;
using Rehearsal.UI.Timeline;

namespace Rehearsal.Game;

/// <summary>
/// Runs practice attempts against the live game: picks the opener for the current job and level,
/// feeds used actions to the session, decides when an attempt is over and when to arm the next
/// one, and works out whether the timeline has any business being on screen.
/// </summary>
internal sealed class Trainer : ITrainer, IDisposable
{
    /// <summary>After a result has been shown, the player must have stopped pressing things for this long.</summary>
    private const double RearmIdleSeconds = 4d;

    /// <summary>Combat has to be over for this long before an unfinished run is called off.</summary>
    private const double CombatEndGraceSeconds = 2.5d;

    /// <summary>Keeps the waiting timeline up briefly after the target is dropped, so re-targeting does not flicker.</summary>
    private const double TargetGraceSeconds = 1.5d;

    private const double RefreshSeconds = 0.5d;
    private const double SoundGapSeconds = 0.35d;
    private const int MaxHistory = 30;

    // Striking dummies, old and "timeworn".
    private const uint StrikingDummyNameId = 541;
    private const uint TimewornDummyNameId = 13078;

    // ContentType rows of the duty finder.
    private const uint DungeonContent = 2;
    private const uint TrialContent = 4;
    private const uint RaidContent = 5;
    private const uint UltimateContent = 28;

    /// <summary>ContentMemberType of a full party of eight; raids with anything else are alliance raids.</summary>
    private const uint FullPartyMembers = 3;

    private readonly AbilityAntsHook ants;
    private readonly ActionCatalog catalog;
    private readonly Countdown countdown;
    private readonly HotbarHints hints;
    private readonly List<RunRecord> history = [];
    private readonly OpenerLibrary library;
    private readonly ActionMatcher matcher;
    private readonly Action<int> playSound;
    private readonly Action saveSettings;
    private readonly Settings settings;
    private readonly ExcelSheet<TerritoryType> territories;

    private List<int> actions = [];
    private bool appliedAutoAdjust;
    private double dummyTargetedAt = double.MinValue;
    private DutyKind duty;
    private uint dutyTerritory = uint.MaxValue;
    private double enemyTargetedAt = double.MinValue;
    private double finishedAt;
    private bool inDuty;
    private double lastActionAt;
    private uint lastJobId = uint.MaxValue;
    private int lastLevel = -1;
    private double lastRefresh = double.MinValue;
    private double lastSoundAt = double.MinValue;
    private double outOfCombatSince = -1d;
    private bool sawCombat;
    private List<int> sourceActions = [];
    private string sourceTitle = string.Empty;
    private bool strictAtArm;

    public Trainer(
        Settings settings, ActionCatalog catalog, OpenerLibrary library, HotbarHints hints, AbilityAntsHook ants, Countdown countdown,
        Action saveSettings, Action<int> playSound)
    {
        this.settings = settings;
        this.catalog = catalog;
        this.library = library;
        this.hints = hints;
        this.ants = ants;
        this.countdown = countdown;
        this.saveSettings = saveSettings;
        this.playSound = playSound;
        matcher = new ActionMatcher(catalog.GetName, AdjustedActionId);
        territories = Plugin.DataManager.GetExcelSheet<TerritoryType>();

        Plugin.Framework.Update += OnUpdate;
    }

    private enum DutyKind
    {
        None,
        Dungeon,
        Trial,
        Raid,
        AllianceRaid,
        Ultimate,
        Other
    }

    public OpenerRef? Active { get; private set; }
    public string ActiveTitle { get; private set; } = string.Empty;
    public Jobs ActiveJob { get; private set; } = Jobs.ANY;
    public IReadOnlyList<int> ActiveActions => actions;
    public int Level { get; private set; }
    public AdaptedOpener? Adjustment { get; private set; }
    public float Speed { get; private set; } = 1f;
    public TrainerSession? Session { get; private set; }
    public int RunId { get; private set; }
    public IReadOnlyList<RunRecord> History => history;
    public float CastProgress { get; private set; } = -1f;
    public bool CastMatches { get; private set; }

    public float CountdownRemaining => countdown.IsActive && countdown.Remaining > 0f ? countdown.Remaining : -1f;

    /// <summary>The job the player is on, as an opener job. ANY for jobs without openers.</summary>
    public Jobs CurrentJob { get; private set; } = Jobs.ANY;

    /// <summary>The trainer is tracking actions right now.</summary>
    public bool IsTracking { get; private set; }

    /// <summary>Which of the two views should be on screen at this moment.</summary>
    public TimelineView EffectiveView
    {
        get
        {
            if (settings.View != TimelineView.Automatic)
                return settings.View;

            // The whole plan while you wait; just what is coming up once you are in it.
            return Session is { State: RunState.Running or RunState.Finished } ? TimelineView.Compact : TimelineView.Expanded;
        }
    }

    /// <summary>Whether the timeline has something worth showing at the moment.</summary>
    public bool WantsOverlay
    {
        get
        {
            if (!IsTracking || Session == null)
                return false;

            return Session.State switch
            {
                RunState.Running => true,
                RunState.Finished => Plugin.Now - finishedAt < settings.ResultSeconds,
                _ => ShowsWhileArmed
            };
        }
    }

    /// <summary>Whether a waiting attempt is worth showing right now.</summary>
    private bool ShowsWhileArmed
    {
        get
        {
            if (countdown.IsActive || settings.ShowAlways)
                return true;

            var now = Plugin.Now;
            if (settings.ShowAtDummy && now - dummyTargetedAt < TargetGraceSeconds)
                return true;
            if (settings.ShowOnEnemyTarget && (now - enemyTargetedAt < TargetGraceSeconds || Plugin.Condition[ConditionFlag.InCombat]))
                return true;

            return settings.ShowInBossDuties && inDuty && duty is DutyKind.Trial or DutyKind.Raid or DutyKind.Ultimate;
        }
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        ants.CurrentStep = 0;
        ants.SetEnabled(false);
    }

    // ---- Choosing what to practise ----

    public bool IsSelected(OpenerRef opener)
    {
        if (Active == opener)
            return true;

        return settings.Selected.TryGetValue(KeyFor(opener.Job), out var saved) && saved.Name == opener.Name
                                                                              && saved.IsDefault == opener.IsDefault;
    }

    public void Select(OpenerRef opener)
    {
        if (!library.TryGet(opener, out var loaded))
            return;

        settings.Selected[KeyFor(opener.Job)] = new SavedSelection { Name = opener.Name, IsDefault = opener.IsDefault };
        saveSettings();

        if (opener.Job == CurrentJob)
            Load(opener, loaded);
    }

    public void Deselect(Jobs job)
    {
        // An empty name records the decision, so the default opener is not picked again next time.
        settings.Selected[KeyFor(job)] = new SavedSelection { Name = string.Empty };
        saveSettings();

        if (job == CurrentJob || job == ActiveJob)
            Unload();
    }

    public void Practice(string title, Jobs job, IReadOnlyList<int> opener)
    {
        Active = null;
        sourceTitle = title;
        ActiveJob = job;
        sourceActions = [..opener];
        Apply();
    }

    public void Reset()
    {
        if (Session is { State: RunState.Running })
        {
            Session.Finish(FinishReason.Aborted, Plugin.Now);
            Record(Session);
        }

        countdown.Stop();
        Arm();
    }

    public void StartCountdown()
    {
        if (actions.Count == 0)
            return;

        if (Session is not { State: RunState.Armed })
            Arm();
        countdown.Start(settings.CountdownSeconds);
    }

    public void StopCountdown()
    {
        countdown.Stop();
    }

    public KeyHint HintFor(int stepId, bool isCurrent)
    {
        // Each view has its own key cap switch; only when both are off is there nothing to look up.
        return settings.ShowKeybinds || settings.CompactShowKeybinds ? hints.Get(stepId, isCurrent) : default;
    }

    // ---- Input from the game ----

    /// <summary>Called by the action effect hook for everything the player executes.</summary>
    public void OnActionUsed(uint id, bool isItem)
    {
        var now = Plugin.Now;
        lastActionAt = now;

        if (!IsTracking || Session == null)
            return;

        // Auto-attacks, mounts, limit breaks and the like are not part of any opener.
        if (!isItem && !catalog.Contains(id))
            return;

        var isUtility = !isItem && settings.IgnoreUtilityActions && catalog.IsUtility(id);
        var before = Session.State;
        var outcome = Session.OnAction(id, isItem, isUtility, now);

        if (before == RunState.Armed && Session.State != RunState.Armed)
        {
            sawCombat = Plugin.Condition[ConditionFlag.InCombat];
            outOfCombatSince = -1d;
        }

        if (outcome is ActionOutcome.Wrong or ActionOutcome.HitAfterSkip && settings.MistakeSound > 0 && now - lastSoundAt >= SoundGapSeconds)
        {
            lastSoundAt = now;
            playSound(settings.MistakeSound);
        }

        if (Session.State == RunState.Finished && before != RunState.Finished)
            OnFinished(Session, now);
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            Update();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Trainer update failed");
        }
    }

    private void Update()
    {
        countdown.Update();

        if (!Plugin.PlayerState.IsLoaded)
        {
            Idle();
            return;
        }

        var now = Plugin.Now;

        // Level sync changes what you can press just as much as changing job does.
        var jobId = Plugin.PlayerState.ClassJob.RowId;
        var level = Plugin.PlayerState.EffectiveLevel > 0 ? Plugin.PlayerState.EffectiveLevel : catalog.MaxLevel;
        if (jobId != lastJobId)
        {
            lastJobId = jobId;
            lastLevel = level;
            Level = level;
            CurrentJob = JobsExtensions.FromAbbreviation(Plugin.PlayerState.ClassJob.ValueNullable?.Abbreviation.ExtractText());
            LoadSelectionForCurrentJob();
            lastRefresh = double.MinValue;
        }
        else if (level != lastLevel)
        {
            lastLevel = level;
            Level = level;
            if (Active != null)
                LoadSelectionForCurrentJob(); // another level cap may have its own choice
            else if (sourceActions.Count > 0)
                Apply();                      // an unsaved opener is kept, just fitted again
            lastRefresh = double.MinValue;
        }
        else if (appliedAutoAdjust != settings.AutoAdjustLevel && sourceActions.Count > 0)
        {
            Apply();
        }

        UpdateDuty();

        var allowed = inDuty ? TracksIn(duty) : settings.TrackOpenWorld;
        var tracking = settings.OverlayEnabled && actions.Count > 0 && allowed;
        if (tracking && !IsTracking)
            Arm();
        IsTracking = tracking;

        if (!tracking)
        {
            Idle();
            return;
        }

        if (Plugin.Targets.Target is IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant } target)
        {
            if (target.NameId is StrikingDummyNameId or TimewornDummyNameId)
                dummyTargetedAt = now;
            else
                enemyTargetedAt = now;
        }

        if (now - lastRefresh >= RefreshSeconds)
        {
            lastRefresh = now;
            Speed = MeasureSpeed();
            hints.Refresh();
            if (hints.LearnedSomething)
            {
                hints.LearnedSomething = false;
                saveSettings();
            }
        }

        // A waiting attempt picks up a changed "stop at first mistake" setting straight away.
        var optionChanged = Session is { State: RunState.Armed } && strictAtArm != settings.StopAtFirstMistake;
        if (Session == null || Session.Opener.Count != actions.Count || optionChanged)
            Arm();

        UpdateRun(Session!, now);
        UpdateCast(Session!);
        UpdateAnts(Session!);
    }

    private void Idle()
    {
        IsTracking = false;
        CastProgress = -1f;
        ants.CurrentStep = 0;
        ants.SetEnabled(false);
    }

    private void UpdateRun(TrainerSession session, double now)
    {
        switch (session.State)
        {
            case RunState.Running:
            {
                // Out of combat for a moment after having been in it: the pull is over.
                if (Plugin.Condition[ConditionFlag.InCombat])
                {
                    sawCombat = true;
                    outOfCombatSince = -1d;
                }
                else if (sawCombat)
                {
                    if (outOfCombatSince < 0d)
                        outOfCombatSince = now;
                    else if (now - outOfCombatSince >= CombatEndGraceSeconds)
                        session.Finish(FinishReason.CombatEnded, now);
                }

                if (session.State == RunState.Running && now - session.LastActionTime >= settings.IdleTimeoutSeconds)
                    session.Finish(FinishReason.TimedOut, now);

                if (session.State == RunState.Finished)
                    OnFinished(session, now);
                break;
            }
            case RunState.Finished:
            {
                // Arm again once the result has had its time and the player has actually stopped;
                // someone carrying on with their rotation must not start a new attempt by accident.
                var shown = now - finishedAt >= settings.ResultSeconds;
                var idle = now - lastActionAt >= RearmIdleSeconds;
                if ((shown && idle) || (idle && !Plugin.Condition[ConditionFlag.InCombat] && now - finishedAt >= 3d))
                    Arm();
                break;
            }
        }
    }

    private void UpdateCast(TrainerSession session)
    {
        CastProgress = -1f;
        CastMatches = false;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null || !player.IsCasting || player.TotalCastTime <= 0f || player.CastActionType != (byte)ActionType.Action)
            return;
        if (session.State == RunState.Finished || session.CurrentIndex >= session.Opener.Count)
            return;

        CastProgress = Math.Clamp(player.CurrentCastTime / player.TotalCastTime, 0f, 1f);
        CastMatches = matcher.Matches(session.Opener[session.CurrentIndex], player.CastActionId);
    }

    private void UpdateAnts(TrainerSession session)
    {
        var show = settings.AbilityAnts && session.State != RunState.Finished && session.CurrentIndex < session.Opener.Count
                   && (session.State == RunState.Running || ShowsWhileArmed);
        ants.CurrentStep = show ? session.Opener[session.CurrentIndex] : 0;
        ants.SetEnabled(show);
    }

    // ---- Where am I ----

    private void UpdateDuty()
    {
        inDuty = Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56]
                                                              || Plugin.Condition[ConditionFlag.BoundByDuty95];

        var territory = Plugin.ClientState.TerritoryType;
        if (territory == dutyTerritory)
            return;

        dutyTerritory = territory;
        duty = DutyKind.Other;
        if (!territories.TryGetRow(territory, out var row) || row.ContentFinderCondition.ValueNullable is not { } content)
            return;

        duty = content.ContentType.RowId switch
        {
            DungeonContent => DutyKind.Dungeon,
            TrialContent => DutyKind.Trial,
            RaidContent => content.ContentMemberType.RowId == FullPartyMembers ? DutyKind.Raid : DutyKind.AllianceRaid,
            UltimateContent => DutyKind.Ultimate,
            _ => DutyKind.Other
        };
    }

    private bool TracksIn(DutyKind kind)
    {
        return kind switch
        {
            DutyKind.Dungeon => settings.TrackDungeons,
            DutyKind.Trial => settings.TrackTrials,
            DutyKind.Raid => settings.TrackRaids,
            DutyKind.AllianceRaid => settings.TrackAllianceRaids,
            DutyKind.Ultimate => settings.TrackUltimates,
            _ => settings.TrackOtherDuties
        };
    }

    /// <summary>The player's global cooldown relative to the base 2.5s, read off a plain GCD of the opener.</summary>
    private float MeasureSpeed()
    {
        foreach (var step in actions)
        {
            if (step <= 0 || !catalog.TryGet((uint)step, out var info))
                continue;
            if (!info.IsGcd || info.RecastSeconds != ActionCatalog.BaseGcdSeconds)
                continue;

            var milliseconds = ActionManager.GetAdjustedRecastTime(ActionType.Action, info.Id);
            if (milliseconds > 0)
                return Math.Clamp(milliseconds / (ActionCatalog.BaseGcdSeconds * 1000f), 0.6f, 1.2f);
        }

        return 1f;
    }

    // ---- Attempt lifecycle ----

    private void Arm()
    {
        strictAtArm = settings.StopAtFirstMistake;
        Session = new TrainerSession(actions, matcher, new TrainerOptions { StopAtFirstMistake = strictAtArm });
        RunId++;
        sawCombat = false;
        outOfCombatSince = -1d;
    }

    private void OnFinished(TrainerSession session, double now)
    {
        finishedAt = now;
        countdown.Stop();
        Record(session);
    }

    private void Record(TrainerSession session)
    {
        // An attempt that never got past "armed" is not worth remembering.
        if (session.HitCount == 0 && session.Mistakes.Count == 0)
            return;

        history.Insert(0, new RunRecord(DateTime.Now, ActiveTitle, ActiveJob, session.FinishReason,
                                        Math.Max(0d, session.EndTime - session.StartTime), session.Opener, session.Steps,
                                        session.Mistakes.ToArray()));
        if (history.Count > MaxHistory)
            history.RemoveRange(MaxHistory, history.Count - MaxHistory);
    }

    // ---- Selection ----

    /// <summary>
    /// Where the choice for a job is stored. For the job you are on it depends on the level cap you
    /// are playing at, so an opener picked while synced to 90 does not replace your current one.
    /// </summary>
    private string KeyFor(Jobs job)
    {
        var cap = job == CurrentJob && Level > 0 ? LevelAdapter.Bracket(Level) : catalog.MaxLevel;
        return Settings.SelectionKey(job.ToString(), cap, catalog.MaxLevel);
    }

    private void LoadSelectionForCurrentJob()
    {
        var job = CurrentJob;
        if (job == Jobs.ANY)
        {
            Unload();
            return;
        }

        // First a choice made for this level cap, then the general one for the job.
        string[] keys = [KeyFor(job), job.ToString()];
        foreach (var key in keys.Distinct())
        {
            if (!settings.Selected.TryGetValue(key, out var saved))
                continue;

            if (saved.Name.Length == 0)
            {
                Unload();
                return;
            }

            var opener = new OpenerRef(job, saved.Name, saved.IsDefault);
            if (library.TryGet(opener, out var loaded))
            {
                Load(opener, loaded);
                return;
            }
        }

        // First time on this job: start with its first bundled opener so there is something to see.
        var first = library.ForJob(job).FirstOrDefault(o => o.IsDefault);
        if (first.Name is { Length: > 0 } && library.TryGet(first, out var defaults))
            Load(first, defaults);
        else
            Unload();
    }

    private void Load(OpenerRef opener, List<int> loaded)
    {
        Active = opener;
        sourceTitle = opener.Name;
        ActiveJob = opener.Job;
        sourceActions = loaded;
        Apply();
    }

    /// <summary>Turns the chosen opener into what is actually shown: fitted to the level when it needs to be.</summary>
    private void Apply()
    {
        appliedAutoAdjust = settings.AutoAdjustLevel;
        Adjustment = null;

        if (settings.AutoAdjustLevel && Level > 0 && sourceActions.Count > 0 && catalog.RequiredLevel(sourceActions) > Level)
        {
            var adapted = catalog.Adapt(sourceActions, Level);
            if (adapted.Changed)
                Adjustment = adapted;
        }

        actions = Adjustment?.Actions ?? [..sourceActions];
        ActiveTitle = Adjustment != null ? $"{sourceTitle}  -  Lv {Level}" : sourceTitle;
        Arm();
    }

    private void Unload()
    {
        Active = null;
        sourceTitle = string.Empty;
        ActiveTitle = string.Empty;
        ActiveJob = CurrentJob;
        sourceActions = [];
        actions = [];
        Adjustment = null;
        Session = null;
        RunId++;
    }

    private static unsafe uint AdjustedActionId(uint id)
    {
        var manager = ActionManager.Instance();
        return manager == null ? id : manager->GetAdjustedActionId(id);
    }
}
