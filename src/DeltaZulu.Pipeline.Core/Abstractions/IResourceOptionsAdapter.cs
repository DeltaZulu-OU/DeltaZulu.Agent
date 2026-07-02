using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Core.Abstractions;

/// <summary>
/// Adapts the opaque <see cref="ResourceDescriptor.Options"/> bag into a strongly-typed,
/// family-owned options record. Core defines the contract; each input family (ETW, and future
/// families) owns its own <typeparamref name="TOptions"/> shape and binding logic.
/// </summary>
public interface IResourceOptionsAdapter<out TOptions>
{
    TOptions Adapt(ResourceDescriptor resource);
}
