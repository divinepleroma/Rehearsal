using System;
using System.Collections.Generic;

namespace Rehearsal.Data;

/// <summary>
/// Reads which action replaces which out of trait descriptions ("Upgrades Jolt II to Jolt III.").
/// The game has no table for this - the replacement itself is hard-wired in the client - but the
/// trait text states it in a handful of regular shapes, and unlike a hand-kept list it stays
/// correct across patches.
/// </summary>
public static class UpgradeParser
{
    private const string Keyword = "Upgrades ";

    /// <summary>Extracts (from, to) action name pairs from an English trait description.</summary>
    /// <param name="description">The trait's description.</param>
    /// <param name="isActionName">Whether a piece of text is the name of an action of the trait's job.</param>
    public static List<(string From, string To)> Parse(string description, Func<string, bool> isActionName)
    {
        var pairs = new List<(string, string)>();
        if (string.IsNullOrEmpty(description))
            return pairs;

        var text = description.Replace('\r', ' ').Replace('\n', ' ');
        var search = 0;
        while (true)
        {
            var start = text.IndexOf(Keyword, search, StringComparison.Ordinal);
            if (start < 0)
                break;

            // The statement runs to the end of its sentence.
            var end = text.IndexOf(". ", start, StringComparison.Ordinal);
            if (end < 0)
                end = text.Length;
            var sentence = text[(start + Keyword.Length)..end].TrimEnd('.', ' ');
            search = end;

            ParseSentence(sentence, isActionName, pairs);
        }

        return pairs;
    }

    private static void ParseSentence(string sentence, Func<string, bool> isActionName, List<(string, string)> pairs)
    {
        // Find the action names in the order they appear, and remember what stands between them.
        var names = new List<string>();
        var gaps = new List<string>();
        var position = 0;
        var gapStart = 0;

        while (position < sentence.Length)
        {
            var name = LongestNameAt(sentence, position, isActionName);
            if (name == null)
            {
                position = NextWordStart(sentence, position);
                continue;
            }

            if (names.Count > 0)
                gaps.Add(sentence[gapStart..position]);
            names.Add(name);
            position += name.Length;
            gapStart = position;
        }

        if (names.Count < 2)
            return;

        var toGaps = new List<int>();
        for (var i = 0; i < gaps.Count; i++)
            if (gaps[i].Contains(" to ", StringComparison.Ordinal))
                toGaps.Add(i);

        if (toGaps.Count == 1)
        {
            // "A and B to C and D (respectively)": everything before "to" becomes what follows it.
            var split = toGaps[0] + 1;
            var count = Math.Min(split, names.Count - split);
            for (var i = 0; i < count; i++)
                pairs.Add((names[i], names[split + i]));
        }
        else
        {
            // "A to B, C to D, and E to F".
            foreach (var gap in toGaps)
                pairs.Add((names[gap], names[gap + 1]));
        }
    }

    /// <summary>The longest action name starting at a word boundary, so "Fire II" wins over "Fire".</summary>
    private static string? LongestNameAt(string sentence, int position, Func<string, bool> isActionName)
    {
        string? best = null;
        var end = position;
        for (var words = 0; words < 6 && end < sentence.Length; words++)
        {
            end = WordEnd(sentence, end);
            var candidate = sentence[position..end].TrimEnd(',', '.');
            if (candidate.Length > 0 && isActionName(candidate))
                best = candidate;

            // Step over the single space separating words.
            if (end < sentence.Length && sentence[end] == ' ')
                end++;
            else
                break;
        }

        return best;
    }

    private static int WordEnd(string text, int position)
    {
        while (position < text.Length && text[position] != ' ')
            position++;
        return position;
    }

    private static int NextWordStart(string text, int position)
    {
        position = WordEnd(text, position);
        while (position < text.Length && text[position] == ' ')
            position++;
        return position;
    }
}
