using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Forward;
using DeltaZulu.Pipeline.Core.MessagePack;
using DeltaZulu.Pipeline.Inputs.Common;
using MessagePack;

namespace DeltaZulu.Pipeline.Inputs.Forwarder;

public sealed record ForwarderInputConfiguration
{
    public bool Enabled { get; init; } = true;
    public string Address { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 2514;
    public bool UseTls { get; init; }
    public string? ServerCertificatePath { get; init; }
    public string? ServerCertificatePassword { get; init; }
}

public sealed class ForwarderInput : ISourceInput
{
    private readonly ForwarderInputConfiguration _configuration;
    private readonly MessagePackPayloadSerializer _serializer = new();

    public ForwarderInput(ForwarderInputConfiguration configuration, string name = "forwarder-input")
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Name = name;
        Validate(configuration);
    }

    public string Name { get; }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) =>
        TcpListenerSourceInput.Create(
            createListener: () => {
                var listener = new TcpListener(IPAddress.Parse(_configuration.Address), _configuration.Port);
                try
                {
                    listener.Start();
                }
                catch (SocketException ex)
                {
                    listener.Stop();
                    throw CreateListenerStartException(ex);
                }

                return listener;
            },
            handleClientAsync: HandleClientAsync,
            cancellationToken);

    private static string? GetString(IReadOnlyDictionary<string, object?> fields, string key) => !fields.TryGetValue(key, out var value) || value is null
            ? null
            : value switch {
                string text when !string.IsNullOrWhiteSpace(text) => text,
                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                JsonElement element when element.ValueKind is JsonValueKind.Object or JsonValueKind.Array => null,
                DateTimeOffset timestamp => timestamp.ToString("O"),
                DateTime timestamp => timestamp.ToString("O"),
                IDictionary => null,
                IEnumerable when value is not string => null,
                _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
            };

    private static string SanitizeErrorMessage(string message) => message.Replace('\r', ' ').Replace('\n', ' ');

    private static void Validate(ForwarderInputConfiguration configuration)
    {
        if (configuration.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration.Port));
        }

        if (configuration.UseTls)
        {
            throw new InvalidDataException("DeltaZulu.Forward input does not support the legacy TLS stream wrapper in this Agent integration path.");
        }
    }

    private InvalidOperationException CreateListenerStartException(SocketException ex)
    {
        var endpoint = $"{_configuration.Address}:{_configuration.Port}";
        var message = ex.SocketErrorCode == SocketError.AccessDenied
            ? $"FORWARDER input could not bind to {endpoint} because the operating system denied access to the socket. "
                + "Choose another forwarderInput.port/address or remove the OS port reservation/firewall policy that blocks this endpoint. "
                + "On Windows, check excluded TCP port ranges with: netsh interface ipv4 show excludedportrange protocol=tcp."
            : $"FORWARDER input could not bind to {endpoint}: {ex.Message}";

        return new InvalidOperationException(message, ex);
    }

    private async Task HandleClientAsync(TcpClient client, IObserver<SourceEvent> observer, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = ForwardConnection.FromAcceptedClient(client);
            var options = new ForwardSessionOptions {
                BatchHandler = (frameType, batchId, payload, ct) =>
                {
                    if (frameType is not ForwardFrameType.TypedBatch and not ForwardFrameType.RawEnvelope)
                    {
                        return Task.FromResult(new ForwardAckOutcome(2, $"Unsupported batch frame type {frameType}"));
                    }

                    var accepted = PublishPayload(payload, observer, out var errorMessage);
                    return Task.FromResult(accepted
                        ? new ForwardAckOutcome(0, null)
                        : new ForwardAckOutcome(1, errorMessage));
                }
            };

            await using var session = await ForwardSession.AcceptAsync(
                connection,
                offer => new ForwardHandshakeAck(
                    Accepted: true,
                    ProtocolVersion: offer.ProtocolVersion,
                    SessionId: Guid.NewGuid(),
                    GrantedWindowSize: offer.RequestedWindowSize,
                    DedupWindowSize: offer.DedupWindowSize,
                    CompressionSelected: offer.CompressionOffered,
                    UnknownSchemaFingerprints: [],
                    RejectReason: string.Empty),
                options,
                cancellationToken).ConfigureAwait(false);

            await session.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
            // The peer can close the connection while the input is shutting down.
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
    }

    private bool PublishPayload(ReadOnlyMemory<byte> payload, IObserver<SourceEvent> observer, out string errorMessage)
    {
        try
        {
            var batch = _serializer.Deserialize<DeliveryBatch>(payload);
            if (batch is null)
            {
                errorMessage = "invalid MessagePack DeliveryBatch: payload decoded to null";
                Console.Error.WriteLine($"FORWARDER input rejected {payload.Length} byte payload: {errorMessage}.");
                Console.Error.Flush();
                return false;
            }

            foreach (var record in batch.Records)
            {
                observer.OnNext(ToSourceEvent(record));
            }

            errorMessage = string.Empty;
            return true;
        }
        catch (MessagePackSerializationException ex)
        {
            errorMessage = $"invalid MessagePack DeliveryBatch: {SanitizeErrorMessage(ex.Message)}";
            Console.Error.WriteLine($"FORWARDER input rejected {payload.Length} byte payload: {errorMessage}");
            Console.Error.Flush();
            return false;
        }
    }

    private SourceEvent ToSourceEvent(DeliveryRecord deliveryRecord)
    {
        var record = deliveryRecord.Record;
        var metadata = new ResourceMetadata {
            CollectorId = GetString(record.Metadata, "collectorId") ?? deliveryRecord.AgentId,
            ProfileId = GetString(record.Metadata, "profileId") ?? deliveryRecord.ProfileId,
            ProfileVersion = GetString(record.Metadata, "profileVersion"),
            SourceType = GetString(record.Metadata, "sourceType") ?? "Transport",
            SourceName = GetString(record.Metadata, "sourceName") ?? deliveryRecord.SourceId,
            Platform = GetString(record.Metadata, "platform") ?? "portable",
            Hostname = GetString(record.Metadata, "hostname") ?? deliveryRecord.AgentId,
            IngestedAt = DateTimeOffset.UtcNow,
            ParserName = nameof(ForwarderInput),
            RawPreserved = true,
            Properties = new Dictionary<string, object?> {
                ["forwarder"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                    ["deliveryID"] = deliveryRecord.DeliveryId,
                    ["recordId"] = deliveryRecord.RecordId,
                    ["createdAt"] = deliveryRecord.CreatedAt
                }
            }
        };

        return new SourceEvent(metadata, new Dictionary<string, object?>(record.Event, StringComparer.OrdinalIgnoreCase));
    }
}
