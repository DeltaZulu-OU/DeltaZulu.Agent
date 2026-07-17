using System.Threading.Channels;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core;

/// <summary>
/// Serializes concurrent writers into one output sink. Completion and errors are terminal: records
/// accepted before the terminal signal are drained before the underlying sink is notified.
/// </summary>
public sealed class ChannelOutputMultiplexer : IOutputWriter
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

    private readonly Channel<ResourceOutputRecord> _channel = Channel.CreateBounded<ResourceOutputRecord>(new BoundedChannelOptions(65_536) {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });

    private readonly IOutputWriter _inner;
    private readonly Lock _lock = new();
    private readonly Task _reader;
    private bool _terminal;

    public ChannelOutputMultiplexer(IOutputWriter inner)
    {
        _inner = inner;
        _reader = Task.Run(ReadMessages);
    }

    public Exception? Error {
        get {
            lock (_lock)
            {
                return field;
            }
        }
        private set;
    }

    public string Name => _inner.Name;

    public void Complete()
    {
        CompleteChannel();
        if (!_reader.Wait(DrainTimeout))
        {
            CaptureError(new TimeoutException($"Channel drain did not complete within {DrainTimeout.TotalSeconds}s."));
        }
    }

    public void Dispose() => Complete();

    public void OnCompleted()
    {
        CompleteChannel();
    }

    public void OnError(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        lock (_lock)
        {
            if (_terminal)
            {
                return;
            }

            _terminal = true;
            Error = error;
        }

        // Completing the channel with the error preserves the ordering of records already
        // accepted by the channel, while making the observer terminal as required by Rx.
        _channel.Writer.TryComplete(error);
    }

    public void OnNext(ResourceOutputRecord value) => WriteRecord(value);

    private void CaptureError(Exception exception)
    {
        lock (_lock)
        {
            Error ??= exception;
        }
    }

    private async Task ReadMessages()
    {
        try
        {
            await foreach (var record in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                _inner.OnNext(record);
            }
        }
        catch (Exception ex)
        {
            CaptureError(ex);
            CompleteChannel();
        }

        NotifyInnerOfTerminalState();
    }

    private void NotifyInnerOfTerminalState()
    {
        try
        {
            if (Error is { } error)
            {
                _inner.OnError(error);
            }
            else
            {
                _inner.OnCompleted();
            }
        }
        catch (Exception ex)
        {
            CaptureError(ex);
        }
    }

    private void CompleteChannel()
    {
        lock (_lock)
        {
            if (_terminal)
            {
                return;
            }

            _terminal = true;
        }

        _channel.Writer.TryComplete();
    }

    private void WriteRecord(ResourceOutputRecord record)
    {
        if (_channel.Writer.TryWrite(record))
        {
            return;
        }

        try
        {
            _channel.Writer.WriteAsync(record).AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException)
        {
            throw new InvalidOperationException("Cannot write to a completed output multiplexer.");
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("Cannot write to a completed output multiplexer.");
        }
    }
}
