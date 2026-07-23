using System.Buffers;
using System.Text.Json;
using DeltaZulu.DurableBuffer.Abstractions;
using DeltaZulu.DurableBuffer.Chunks;
using DeltaZulu.Forward;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using Polly;
using Polly.Retry;

namespace DeltaZulu.Pipeline.Outputs.Forwarder;

internal sealed class ForwarderOutputWorker
{
    private readonly Action<DateTimeOffset>? _onActivity;
    private readonly IDurableBufferReader _reader;
    private readonly ForwarderRetryConfiguration _retryConfiguration;
    private readonly ResiliencePipeline<DeliveryAck> _sendPipeline;
    private readonly IDeliveryTransport _transport;
    private long _chunksDeadLettered;
    private long _chunksDiscarded;
    private long _permanentFailures;
    private int _running;
    private long _sendAttempts;
    private long _sendSuccesses;
    private long _transientFailures;

    public ForwarderOutputWorker(
        IDurableBufferReader reader,
        IDeliveryTransport transport,
        ForwarderRetryConfiguration retryConfiguration,
        Action<DateTimeOffset>? onActivity = null)
    {
        _reader = reader;
        _transport = transport;
        _retryConfiguration = retryConfiguration;
        _onActivity = onActivity;

        _sendPipeline = new ResiliencePipelineBuilder<DeliveryAck>()
            .AddRetry(new RetryStrategyOptions<DeliveryAck> {
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is not null and not OperationCanceledException
                    || args.Outcome.Result is { Accepted: false }),
                MaxRetryAttempts = Math.Max(0, retryConfiguration.MaxAttempts - 1),
                BackoffType = DelayBackoffType.Exponential,
                Delay = retryConfiguration.BaseDelay,
                MaxDelay = retryConfiguration.MaxDelay,
                UseJitter = true,
                OnRetry = args => {
                    Interlocked.Increment(ref _transientFailures);
                    RecordActivity();
                    return default;
                }
            })
            .Build();
    }

    public ForwarderTransportSnapshot GetSnapshot() => new() {
        SendAttemptsTotal = Interlocked.Read(ref _sendAttempts),
        SendSuccessesTotal = Interlocked.Read(ref _sendSuccesses),
        TransientFailuresTotal = Interlocked.Read(ref _transientFailures),
        PermanentFailuresTotal = Interlocked.Read(ref _permanentFailures),
        ChunksDeadLetteredTotal = Interlocked.Read(ref _chunksDeadLettered),
        ChunksDiscardedTotal = Interlocked.Read(ref _chunksDiscarded),
        IsRunning = Volatile.Read(ref _running) != 0
    };

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _running, 1);
        try
        {
            await foreach (var chunk in _reader.SealedChunks.ReadAllAsync(cancellationToken))
            {
                await ProcessChunkAsync(chunk, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private static async Task<List<ForwardLogRecord>> ReadRecordsAsync(
        StoredChunk chunk,
        CancellationToken cancellationToken)
    {
        var fileLength = (int)new FileInfo(chunk.ChunkFilePath).Length;
        var chunkBytes = ArrayPool<byte>.Shared.Rent(fileLength);
        try
        {
            await using var stream = new FileStream(
                chunk.ChunkFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.SequentialScan);
            await stream.ReadExactlyAsync(chunkBytes.AsMemory(0, fileLength), cancellationToken).ConfigureAwait(false);

            var records = new List<ForwardLogRecord>();
            foreach (var recordBytes in ChunkFormat.ReadRecords(chunkBytes.AsMemory(0, fileLength)))
            {
                var record = JsonSerializer.Deserialize<ForwardLogRecord>(recordBytes.Span, ForwarderOutputJson.Options);
                if (record is not null)
                {
                    records.Add(record);
                }
            }

            return records;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunkBytes);
        }
    }

    private static Guid DeriveBatchId(string chunkIdValue) => Guid.Parse(chunkIdValue);

    private async Task ProcessChunkAsync(StoredChunk chunk, CancellationToken cancellationToken)
    {
        ForwardLogBatch batch;
        try
        {
            var records = await ReadRecordsAsync(chunk, cancellationToken);
            if (records.Count != chunk.Metadata.RecordCount)
            {
                Interlocked.Increment(ref _permanentFailures);
                Interlocked.Increment(ref _chunksDeadLettered);
                RecordActivity();
                await _reader.DeadLetterAsync(
                    chunk,
                    $"Chunk {chunk.Id} expected {chunk.Metadata.RecordCount} records but decoded {records.Count}.",
                    cancellationToken);
                return;
            }

            batch = new ForwardLogBatch {
                BatchId = DeriveBatchId(chunk.Id.Value),
                CreatedAt = chunk.Metadata.SealedUtc ?? chunk.Metadata.CreatedUtc,
                Records = records
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _permanentFailures);
            Interlocked.Increment(ref _chunksDeadLettered);
            RecordActivity();
            await _reader.DeadLetterAsync(chunk, $"Chunk decode failed: {ex.Message}", cancellationToken);
            return;
        }

        DeliveryAck? ack = null;
        string? failureReason;
        try
        {
            ack = await _sendPipeline.ExecuteAsync(
                async token => {
                    Interlocked.Increment(ref _sendAttempts);
                    RecordActivity();
                    return await _transport.SendAsync(batch, token);
                },
                cancellationToken);
            failureReason = ack.Accepted ? null : ack.Reason ?? "Forwarder batch was not acknowledged.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
        }

        if (ack is { Accepted: true })
        {
            Interlocked.Increment(ref _sendSuccesses);
            RecordActivity();
            await _reader.CompleteAsync(chunk, cancellationToken);
            return;
        }

        Interlocked.Increment(ref _transientFailures);
        RecordActivity();

        if (_retryConfiguration.ExhaustedPolicy == ForwarderRetryExhaustedPolicy.DeadLetter)
        {
            Interlocked.Increment(ref _chunksDeadLettered);
            await _reader.DeadLetterAsync(chunk, $"Retry exhausted: {failureReason}", cancellationToken);
        }
        else
        {
            Interlocked.Increment(ref _chunksDiscarded);
            await _reader.CompleteAsync(chunk, cancellationToken);
        }
    }

    private void RecordActivity() => _onActivity?.Invoke(DateTimeOffset.UtcNow);
}
