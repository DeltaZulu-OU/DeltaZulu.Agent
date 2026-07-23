using System.Text.Json.Serialization;

namespace DeltaZulu.Pipeline.Core.Delivery;

public sealed record DeliveryAck
{
    [JsonPropertyName("batchId")]
    public required Guid BatchId { get; init; }

    [JsonPropertyName("accepted")]
    public required bool Accepted { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
