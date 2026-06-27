using System.Text.Json.Serialization;

namespace DeltaZulu.Agent.Shared.Pipeline.Delivery;

public sealed record DeliveryAck
{
    [JsonPropertyName("batchId")]
    public required string BatchId { get; init; }

    [JsonPropertyName("accepted")]
    public required bool Accepted { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
