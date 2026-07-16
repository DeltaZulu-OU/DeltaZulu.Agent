using MessagePack;
using MessagePack.Resolvers;

namespace DeltaZulu.Pipeline.Core.MessagePack;

/// <summary>
/// Provides the shared MessagePack serializer options used by MessagePack inputs and outputs.
/// </summary>
public static class AgentMessagePackSerializerOptions
{
    /// <summary>
    /// Creates the default cross-boundary MessagePack serializer options for DeltaZulu Agent payloads.
    /// </summary>
    public static MessagePackSerializerOptions CreateDefault() => MessagePackSerializerOptions.Standard
        .WithResolver(CompositeResolver.Create(
            PrimitiveObjectResolver.Instance,
            ContractlessStandardResolver.Instance))
        .WithSecurity(MessagePackSecurity.UntrustedData);
}
