using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Platform.Prefilter;

/// <summary>
/// Decides whether a profile's declarative <see cref="ResourceProfile.Condition"/> is satisfied
/// on this host, by dispatching to whichever registered <see cref="IResourceConditionEvaluator"/>
/// handles the condition's type. This is the single pre-filter step composition roots call before
/// binding a profile to an input; it replaces per-host duplicated, platform-#if-gated condition
/// logic.
/// </summary>
public sealed class ResourceProfilePrefilter
{
    private readonly IReadOnlyList<IResourceConditionEvaluator> _evaluators;

    public ResourceProfilePrefilter(IEnumerable<IResourceConditionEvaluator> evaluators)
    {
        _evaluators = evaluators.ToList();
    }

    /// <summary>
    /// Returns whether <paramref name="profile"/> should run on this host. A profile with no
    /// condition is always satisfied. When the condition is declared <c>mandatory</c> (the
    /// default) and cannot be evaluated, has no registered evaluator, or evaluates to false,
    /// this throws rather than silently continuing. When the condition is declared optional
    /// (<c>mandatory: false</c>), the same cases return false with a human-readable
    /// <paramref name="warning"/> instead of throwing.
    /// </summary>
    public bool IsSatisfied(ResourceProfile profile, out string? warning)
    {
        warning = null;
        var condition = profile.Condition;
        if (condition is null)
        {
            return true;
        }

        var evaluator = _evaluators.FirstOrDefault(candidate => candidate.Handles(condition.Type));
        if (evaluator is null)
        {
            return Reject(profile, condition, $"profile '{profile.Id}' condition.type '{condition.Type}' has no registered evaluator on this platform.", error: null, out warning);
        }

        if (!evaluator.TryEvaluate(condition, out var isSatisfied, out var error))
        {
            return Reject(profile, condition, $"profile '{profile.Id}' condition could not be evaluated: {error?.Message ?? "unknown error"}", error, out warning);
        }

        if (!isSatisfied)
        {
            return Reject(profile, condition, $"profile '{profile.Id}' condition '{condition.Type}' is not satisfied.", error: null, out warning);
        }

        return true;
    }

    private static bool Reject(ResourceProfile profile, ResourceCondition condition, string message, Exception? error, out string? warning)
    {
        if (condition.Mandatory)
        {
            throw new InvalidOperationException(message, error);
        }

        warning = message;
        return false;
    }
}
