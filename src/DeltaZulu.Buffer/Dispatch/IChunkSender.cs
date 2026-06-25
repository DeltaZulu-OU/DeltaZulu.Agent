using DeltaZulu.Buffer.Chunks;

namespace DeltaZulu.Buffer.Dispatch;

public interface IChunkSender
{
    ValueTask<ChunkSendResult> SendAsync(
        StoredChunk chunk,
        CancellationToken cancellationToken = default);
}