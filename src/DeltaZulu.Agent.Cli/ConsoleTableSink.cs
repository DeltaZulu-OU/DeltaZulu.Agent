using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Outputs.Ndjson;
using System.Text.Json;

#if WINDOWS
#endif

namespace DeltaZulu.Agent.Cli;

internal static partial class Program
{
    private sealed class ConsoleTableSink : IResourceSink
    {
        private readonly JsonSerializerOptions _jsonOptions = NdjsonSerializerOptions.CreateDefault();
        private bool _printedHeader;
        public string Name => "table-console";

        public void Dispose()
        { }

        public void OnCompleted()
        { }

        public void OnError(Exception error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.Flush();
        }

        public void OnNext(ResourceOutputRecord value)
        {
            if (!_printedHeader)
            {
                Console.WriteLine("timestamp\tsource\tevent");
                _printedHeader = true;
            }

            value.Metadata.TryGetValue("ingestedAt", out var timestamp);
            value.Metadata.TryGetValue("sourceName", out var source);
            Console.WriteLine($"{timestamp}\t{source}\t{JsonSerializer.Serialize(value.Event, _jsonOptions)}");
            Console.Out.Flush();
        }
    }
}
