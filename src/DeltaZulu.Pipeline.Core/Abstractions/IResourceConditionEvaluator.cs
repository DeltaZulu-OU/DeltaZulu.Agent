using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Core.Abstractions;

/// <summary>
/// Evaluates one <see cref="ResourceCondition"/> type (e.g. "wmi", or a future Linux-native
/// check such as a package or systemd unit lookup). Core defines the contract only; concrete
/// evaluators live in platform-specific projects and are composed by a pre-filter registry.
/// </summary>
public interface IResourceConditionEvaluator
{
    bool Handles(string conditionType);

    bool TryEvaluate(ResourceCondition condition, out bool isSatisfied, out Exception? error);
}
