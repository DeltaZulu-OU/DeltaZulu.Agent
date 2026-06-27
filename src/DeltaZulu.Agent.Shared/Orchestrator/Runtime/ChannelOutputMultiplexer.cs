using DeltaZulu.Agent.Shared.Pipeline.Abstractions;
using DeltaZulu.Agent.Shared.Pipeline.Events;
using System.Threading.Channels;

namespace DeltaZulu.Agent.Shared.Orchestrator.Runtime;

public sealed class ChannelOutputMultiplexer : IOutputWriter
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

    private readonly Channel<SinkMessage> _channel = Channel.CreateBounded<SinkMessage>(new BoundedChannelOptions(65_536)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly IOutputWriter _inner;
    private readonly Task _reader;
    private readonly Lock _lock = new();
    private bool _completed;

    public ChannelOutputMultiplexer(IOutputWriter inner)
    {
        _inner = inner;
        _reader = Task.Run(ReadMessages);
    }

    public string Name => _inner.Name;

    public Exception? Error
    {
        get
        {
            lock (_lock)
            {
                return field;
            }
        }
        private set;
    }

    public void OnNext(ResourceOutputRecord value)
    {
        WriteMessage(SinkMessage.Next(value));
    }

    public void OnError(Exception error)
    {
        CaptureError(error);
        if (!TryWriteMessage(SinkMessage.Error(error)))
        {
            try
            {
                _inner.OnError(error);
            }
            catch (Exception ex)
            {
                CaptureError(ex);
            }
        }
    }

    public void OnCompleted()
    {
    }

    public void Complete()
    {
        lock (_lock)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
        }

        _channel.Writer.TryComplete();
        if (_reader.Wait(DrainTimeout))
        {
            _inner.OnCompleted();
        }
        else
        {
            CaptureError(new TimeoutException($"Channel drain did not complete within {DrainTimeout.TotalSeconds}s."));
        }
    }

    public void Dispose() => Complete();

    private void WriteMessage(SinkMessage message)
    {
        if (!TryWriteMessage(message))
        {
            throw new InvalidOperationException("Cannot write to a completed output multiplexer.");
        }
    }

    private bool TryWriteMessage(SinkMessage message)
    {
        if (_channel.Writer.TryWrite(message))
        {
            return true;
        }

        try
        {
            _channel.Writer.WriteAsync(message).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task ReadMessages()
    {
        await foreach (var message in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (message.Exception is not null)
                {
                    _inner.OnError(message.Exception);
                }
                else if (message.Record is not null)
                {
                    _inner.OnNext(message.Record);
                }
            }
            catch (Exception ex)
            {
                CaptureError(ex);
            }
        }
    }

    private void CaptureError(Exception exception)
    {
        lock (_lock)
        {
            Error ??= exception;
        }
    }

    private sealed record SinkMessage(ResourceOutputRecord? Record, Exception? Exception)
    {
        public static SinkMessage Next(ResourceOutputRecord record) => new(record, null);
        public static SinkMessage Error(Exception exception) => new(null, exception);
    }
}
