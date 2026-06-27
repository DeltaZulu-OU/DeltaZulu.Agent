using DeltaZulu.Agent.Shared.Pipeline.Delivery;

namespace DeltaZulu.Agent.Shared.Pipeline.Abstractions;

public interface IDeliveryTransport
{
    ValueTask<DeliveryAck> SendAsync(
        DeliveryBatch batch,
        CancellationToken cancellationToken = default);
}
