using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenerTimeline.Data;

/// <summary>Identifies an opener in the library.</summary>
public readonly record struct OpenerRef(Jobs Job, string Name, bool IsDefault);

/// <summary>
/// The bundled openers (taken from The Balance, shipped as openers.json) plus the player's own.
/// The file format is unchanged from OpenerCreator so saved openers carry over.
/// </summary>
public sealed class OpenerLibrary
{
    private readonly Dictionary<Jobs, Dictionary<string, List<int>>> defaults;
    private readonly Action<string, Exception>? logError;
    private readonly Dictionary<Jobs, Dictionary<string, List<int>>> saved;
    private readonly string? savedPath;

    /// <param name="defaultsPath">openers.json next to the plugin assembly.</param>
    /// <param name="savedPath">The player's openers.json in the plugin config directory.</param>
    /// <param name="logError">Called when a file cannot be read or written.</param>
    public OpenerLibrary(string? defaultsPath, string? savedPath, Action<string, Exception>? logError = null)
    {
        this.savedPath = savedPath;
        this.logError = logError;
        defaults = Load(defaultsPath);
        saved = Load(savedPath);
    }

    public IEnumerable<OpenerRef> All =>
        Enumerate(defaults, true).Concat(Enumerate(saved, false));

    public IEnumerable<OpenerRef> ForJob(Jobs job)
    {
        return All.Where(o => o.Job == job);
    }

    public bool TryGet(OpenerRef opener, out List<int> actions)
    {
        var source = opener.IsDefault ? defaults : saved;
        if (source.TryGetValue(opener.Job, out var byName) && byName.TryGetValue(opener.Name, out var stored))
        {
            actions = [..stored];
            return true;
        }

        actions = [];
        return false;
    }

    public bool Exists(OpenerRef opener)
    {
        var source = opener.IsDefault ? defaults : saved;
        return source.TryGetValue(opener.Job, out var byName) && byName.ContainsKey(opener.Name);
    }

    public void Save(string name, Jobs job, IEnumerable<int> actions)
    {
        if (!saved.TryGetValue(job, out var byName))
        {
            byName = new Dictionary<string, List<int>>();
            saved[job] = byName;
        }

        byName[name] = [..actions];
        Persist();
    }

    public void Delete(string name, Jobs job)
    {
        if (!saved.TryGetValue(job, out var byName))
            return;

        byName.Remove(name);
        if (byName.Count == 0)
            saved.Remove(job);

        Persist();
    }

    /// <summary>Copies openers from another file (e.g. an OpenerCreator install) without overwriting.</summary>
    /// <returns>How many openers were added.</returns>
    public int Import(string path)
    {
        var added = 0;
        foreach (var (job, byName) in Load(path))
        {
            foreach (var (name, actions) in byName)
            {
                if (saved.TryGetValue(job, out var existing) && existing.ContainsKey(name))
                    continue;

                if (!saved.TryGetValue(job, out existing))
                {
                    existing = new Dictionary<string, List<int>>();
                    saved[job] = existing;
                }

                existing[name] = [..actions];
                added++;
            }
        }

        if (added > 0)
            Persist();

        return added;
    }

    private static IEnumerable<OpenerRef> Enumerate(Dictionary<Jobs, Dictionary<string, List<int>>> source, bool isDefault)
    {
        foreach (var (job, byName) in source.OrderBy(kv => kv.Key))
            foreach (var name in byName.Keys)
                yield return new OpenerRef(job, name, isDefault);
    }

    private Dictionary<Jobs, Dictionary<string, List<int>>> Load(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new Dictionary<Jobs, Dictionary<string, List<int>>>();

            // Keys are read as text so one job this build does not know about cannot void the whole file.
            var json = File.ReadAllText(path);
            var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<int>>>>(json);
            var result = new Dictionary<Jobs, Dictionary<string, List<int>>>();
            if (raw == null)
                return result;

            foreach (var (key, byName) in raw)
            {
                var job = JobsExtensions.FromAbbreviation(key);
                if (job == Jobs.ANY || byName == null)
                    continue;

                // A hand-edited or imported file may hold "name": null. Dropped here, so nothing
                // that reads the library later has to expect a missing list.
                var usable = new Dictionary<string, List<int>>();
                foreach (var (name, actions) in byName)
                {
                    if (actions != null && !string.IsNullOrWhiteSpace(name))
                        usable[name] = actions;
                }

                if (usable.Count > 0)
                    result[job] = usable;
            }

            return result;
        }
        catch (Exception ex)
        {
            logError?.Invoke($"Failed to load openers from {path}", ex);
            return new Dictionary<Jobs, Dictionary<string, List<int>>>();
        }
    }

    private void Persist()
    {
        if (string.IsNullOrEmpty(savedPath))
            return;

        try
        {
            var directory = Path.GetDirectoryName(savedPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(savedPath, JsonSerializer.Serialize(saved));
        }
        catch (Exception ex)
        {
            logError?.Invoke($"Failed to save openers to {savedPath}", ex);
        }
    }
}
