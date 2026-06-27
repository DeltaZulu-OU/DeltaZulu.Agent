using DeltaZulu.Agent.Pipeline.Delivery;

namespace DeltaZulu.Agent.Pipeline.Abstractions;

public interface IDeliveryTransport
{
    ValueTask<DeliveryAck> SendAsync(
        DeliveryBatch batch,
        CancellationToken cancellationToken = default);
}
