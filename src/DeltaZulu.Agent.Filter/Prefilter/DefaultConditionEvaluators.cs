using DeltaZulu.Pipeline.Core.Abstractions;

#if WINDOWS
using DeltaZulu.Agent.Filter.Prefilter.Windows;
#endif

namespace DeltaZulu.Agent.Filter.Prefilter;

/// <summary>
/// Composes the condition evaluators available on the current build's platform. Composition
/// roots (dzagentctl, dzagentd) call this once to build their <see cref="ResourceProfilePrefilter"/>
/// instead of hand-rolling platform-gated condition logic. Linux-native evaluators (package
/// manager, systemd unit, etc.) get added here once implemented.
/// </summary>
public static class DefaultConditionEvaluators
{
    public static IReadOnlyList<IResourceConditionEvaluator> ForCurrentPlatform() =>
#if WINDOWS
        [new WmiConditionEvaluator()];
#else
        [];
#endif

}
