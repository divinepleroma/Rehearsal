using System;
using System.Collections.Generic;

namespace Rehearsal.Training;

/// <summary>What the timing estimate needs to know about a step.</summary>
/// <param name="IsGcd">The step occupies a global cooldown.</param>
/// <param name="GcdSeconds">Length of the global cooldown it triggers, before speed.</param>
/// <param name="CastSeconds">Its cast time, before speed.</param>
public readonly record struct StepSpec(bool IsGcd, float GcdSeconds, float CastSeconds);

/// <summary>Where a step falls in the fight.</summary>
/// <param name="GcdNumber">
/// For a global cooldown: its ordinal. For anything else: the ordinal of the global cooldown it is
/// woven after. Zero means before the first counted one (pre-pull).
/// </param>
/// <param name="Time">Seconds relative to the pull; negative before it.</param>
public readonly record struct StepTiming(int GcdNumber, float Time);

/// <summary>
/// Estimates when each step of an opener happens. It is a plan, not a simulation: every global
/// cooldown is assumed to roll back to back and weaves to fit, which is what an opener is built
/// around. Good for "buffs go out after the third GCD, about seven seconds in".
/// </summary>
public static class OpenerTiming
{
    /// <summary>How long after a global cooldown the first weave can go out.</summary>
    public const float FirstWeaveSeconds = 0.6f;

    /// <summary>Spacing between consecutive weaves.</summary>
    public const float WeaveSeconds = 0.7f;

    /// <param name="steps">The opener.</param>
    /// <param name="speed">The player's global cooldown relative to the base 2.5s (1 = no speed).</param>
    /// <param name="countPrecast">
    /// Whether an opening cast started before the pull counts as GCD 1. Guides disagree on this;
    /// counting it means every button you press on the global cooldown has a number.
    /// </param>
    public static StepTiming[] Estimate(IReadOnlyList<StepSpec> steps, float speed = 1f, bool countPrecast = true)
    {
        var result = new StepTiming[steps.Count];
        if (steps.Count == 0)
            return result;

        speed = Math.Clamp(speed, 0.5f, 1.5f);

        var cursor = 0f;        // earliest moment the next button can be pressed
        var nextGcd = 0f;       // when the global cooldown is back up
        var number = 0;
        var pull = 0f;
        var sawGcd = false;

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.IsGcd)
            {
                var start = MathF.Max(nextGcd, cursor);
                var gcd = (step.GcdSeconds > 0f ? step.GcdSeconds : 2.5f) * speed;

                if (!sawGcd)
                {
                    // The first one lands on the pull: a cast is started early so that it does. It
                    // is the one cast that is always a hard cast, so it holds up whatever follows.
                    sawGcd = true;
                    var cast = step.CastSeconds * speed;
                    pull = start + cast;
                    if (countPrecast || step.CastSeconds <= 0f)
                        number++;

                    result[i] = new StepTiming(number, start);
                    nextGcd = start + MathF.Max(gcd, cast);
                    cursor = start + MathF.Max(FirstWeaveSeconds, cast);
                    continue;
                }

                // Later casts are treated as instant: openers lean on procs and swiftcasts for
                // them, and what matters here is the rhythm of the global cooldown.
                number++;
                result[i] = new StepTiming(number, start);
                nextGcd = start + gcd;
                cursor = start + FirstWeaveSeconds;
            }
            else
            {
                result[i] = new StepTiming(number, cursor);
                cursor += WeaveSeconds;
            }
        }

        for (var i = 0; i < result.Length; i++)
            result[i] = result[i] with { Time = result[i].Time - pull };

        return result;
    }
}
