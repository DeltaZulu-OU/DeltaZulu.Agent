using System.Text.Json.Serialization;

namespace DeltaZulu.Pipeline.Core.Delivery;

public sealed record DeliveryBatch
{
    [JsonPropertyName("batchId")]
    public required string BatchId { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("records")]
    public required IReadOnlyList<DeliveryRecord> Records { get; init; }
}