using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Buffer;
using DeltaZulu.Buffer.Configuration;

namespace DeltaZulu.Agent.Forwarder;

public sealed class BufferedForwarderSink : IResourceSink
{
    private readonly DeltaZuluBufferHost<DeliveryRecord> _host;
    private readonly string _storagePath;
    private readonly CancellationToken _cancellationToken;
    private bool _disposed;

    public BufferedForwarderSink(
        DeltaZuluBufferOptions options,
        IForwarderTransport transport,
        CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        _storagePath = options.StoragePath;
        _host = new DeltaZuluBufferHost<DeliveryRecord>(
            options,
            new DeliveryRecordSerializer(),
            new ForwarderChunkSender(transport));
        _host.StartAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public string Name => "buffered-forwarder";

    public void OnCompleted()
    {
        _host.Buffer.FlushAsync(_cancellationToken).AsTask().GetAwaiter().GetResult();
        WaitForDispatchIdle(_cancellationToken);
    }

    public void OnError(Exception error)
    {
        Console.Error.WriteLine($"forwarder sink error: {error}");
        Console.Error.Flush();
    }

    public void OnNext(ResourceOutputRecord value)
    {
        var result = _host.Buffer.WriteAsync(
            DeliveryRecord.FromResourceOutput(value),
            _cancellationToken).AsTask().GetAwaiter().GetResult();

        if (!result.IsAccepted)
        {
            throw new InvalidOperationException($"Forwarder buffer rejected record: {result.Status}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _host.Buffer.FlushAsync(_cancellationToken).AsTask().GetAwaiter().GetResult();
        WaitForDispatchIdle(_cancellationToken);
        _host.StopAsync(_cancellationToken).AsTask().GetAwaiter().GetResult();
        _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _disposed = true;
    }

    private void WaitForDispatchIdle(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasPendingChunks())
            {
                return;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }
    }

    private bool HasPendingChunks()
    {
        return HasChunkFiles(Path.Combine(_storagePath, "sealed"))
            || HasChunkFiles(Path.Combine(_storagePath, "dispatching"));
    }

    private static bool HasChunkFiles(string path) =>
        Directory.Exists(path) && Directory.EnumerateFiles(path, "*.chunk").Any();
}
