using DeltaZulu.Forward;
using DeltaZulu.Pipeline.Core.Delivery;

namespace DeltaZulu.Pipeline.Core.Abstractions;

public interface IDeliveryTransport
{
    ValueTask<DeliveryAck> SendAsync(
        ForwardLogBatch batch,
        CancellationToken cancellationToken = default);
}
