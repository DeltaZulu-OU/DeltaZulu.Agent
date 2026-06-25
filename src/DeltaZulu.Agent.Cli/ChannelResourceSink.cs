using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Threading.Channels;

namespace DeltaZulu.Agent.Cli;

internal static partial class Program
{
    private sealed class ChannelResourceSink : IResourceSink
    {
        private readonly Channel<SinkMessage> _channel = Channel.CreateUnbounded<SinkMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        private readonly IResourceSink _inner;
        private readonly Task _reader;
        private readonly Lock _lock = new();
        private bool _completed;

        public ChannelResourceSink(IResourceSink inner)
        {
            _inner = inner;
            _reader = Task.Run(ReadMessages);
        }

        public string Name => _inner.Name;

        internal Exception? Error
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

        public void OnNext(ResourceOutputRecord value) => _channel.Writer.TryWrite(SinkMessage.Next(value));

        public void OnError(Exception error)
        {
            CaptureError(error);
            _channel.Writer.TryWrite(SinkMessage.Error(error));
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
            _reader.GetAwaiter().GetResult();
            _inner.OnCompleted();
        }

        public void Dispose() => Complete();

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
}
