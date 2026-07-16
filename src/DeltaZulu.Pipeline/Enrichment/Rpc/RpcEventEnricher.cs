using System.Globalization;
using System.Net;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Enrichment.Rpc;

public static class RpcEventEnricher
{
    private static readonly string[] EndpointFields = ["Endpoint", "RpcEndpoint", "EndpointName"];
    private static readonly string[] InterfaceUuidFields = ["InterfaceUuid", "InterfaceUUID", "RpcInterfaceUuid", "RpcInterfaceUUID"];
    private static readonly string[] NetworkAddressFields = ["NetworkAddress", "RpcNetworkAddress", "RemoteAddress"];
    private static readonly string[] ProcNumFields = ["ProcNum", "ProcNumber", "OpNum", "Opnum", "RpcOpNum"];

    public static IReadOnlyDictionary<string, object?>? BuildRpcEnrichment(
        IReadOnlyDictionary<string, object?> fields,
        ResourceMetadata? metadata = null) => IsRpcSource(metadata, fields) ? BuildRpcEnrichmentCore(fields) : null;

    public static IReadOnlyDictionary<string, object?>? BuildRpcEnrichment(
        IReadOnlyDictionary<string, object?> fields,
        IReadOnlyDictionary<string, object?> metadata) => IsRpcSource(metadata, fields) ? BuildRpcEnrichmentCore(fields) : null;

    public static bool IsLikelyLocalRpc(string? endpoint, string? networkAddress)
    {
        if (endpoint?.StartsWith("LRPC-", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(networkAddress))
        {
            return true;
        }

        var normalized = networkAddress.Trim();
        if (normalized.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(normalized, out var address) && IPAddress.IsLoopback(address);
    }

    public static bool IsRpcSource(ResourceMetadata? metadata, IReadOnlyDictionary<string, object?> fields)
    {
        if (metadata?.SourceName.Equals("Microsoft-Windows-RPC", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (metadata?.SourceType.Equals("WindowsEtw", StringComparison.OrdinalIgnoreCase) == true &&
            FirstString(fields, ["ProviderName"])?.Equals("Microsoft-Windows-RPC", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return HasRpcInterfaceUuid(fields);
    }

    public static bool IsRpcSource(IReadOnlyDictionary<string, object?>? metadata, IReadOnlyDictionary<string, object?> fields)
    {
        if (metadata is not null)
        {
            if (MetadataEquals(metadata, "sourceName", "Microsoft-Windows-RPC"))
            {
                return true;
            }

            if (MetadataEquals(metadata, "sourceType", "WindowsEtw") &&
                FirstString(fields, ["ProviderName"])?.Equals("Microsoft-Windows-RPC", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return HasRpcInterfaceUuid(fields);
    }

    private static IReadOnlyDictionary<string, object?>? BuildRpcEnrichmentCore(IReadOnlyDictionary<string, object?> fields)
    {
        var interfaceUuid = FirstString(fields, InterfaceUuidFields);
        var procNum = FirstInt(fields, ProcNumFields);

        if (string.IsNullOrWhiteSpace(interfaceUuid) && procNum is null)
        {
            return null;
        }

        var endpoint = FirstString(fields, EndpointFields);
        var networkAddress = FirstString(fields, NetworkAddressFields);
        var isLocal = IsLikelyLocalRpc(endpoint, networkAddress);
        var rpc = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
            ["InterfaceUuid"] = string.IsNullOrWhiteSpace(interfaceUuid) ? null : RpcOperationResolver.NormalizeUuid(interfaceUuid),
            ["ProcNum"] = procNum,
            ["Endpoint"] = endpoint,
            ["NetworkAddress"] = networkAddress,
            ["IsLocal"] = isLocal,
            ["IsRemote"] = !isLocal,
            ["ResolverVersion"] = RpcOperationResolver.CurrentResolverVersion
        };

        if (!string.IsNullOrWhiteSpace(interfaceUuid))
        {
            var interfaceName = RpcOperationResolver.ResolveInterfaceName(interfaceUuid);
            if (interfaceName is not null)
            {
                rpc["InterfaceName"] = interfaceName;
            }
        }

        if (procNum is not null)
        {
            var descriptor = RpcOperationResolver.Resolve(interfaceUuid, procNum.Value);
            if (descriptor is not null)
            {
                rpc["InterfaceName"] = descriptor.InterfaceName;
                rpc["OperationName"] = descriptor.OperationName;
                rpc["OperationCategory"] = descriptor.OperationCategory;
                rpc["ResolverVersion"] = descriptor.ResolverVersion;
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
            ["Rpc"] = rpc
        };
    }

    private static int? FirstInt(IReadOnlyDictionary<string, object?> fields, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!fields.TryGetValue(name, out var value) || value is null)
            {
                continue;
            }

            switch (value)
            {
                case int intValue:
                    return intValue;

                case long longValue when longValue <= int.MaxValue && longValue >= int.MinValue:
                    return (int)longValue;

                case short shortValue:
                    return shortValue;

                case byte byteValue:
                    return byteValue;

                case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                    return parsed;
            }
        }

        return null;
    }

    private static string? FirstString(IReadOnlyDictionary<string, object?> fields, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetValue(name, out var value) && value is not null)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static bool HasRpcInterfaceUuid(IReadOnlyDictionary<string, object?> fields) =>
                fields.ContainsKey("InterfaceUuid") ||
        fields.ContainsKey("InterfaceUUID") ||
        fields.ContainsKey("RpcInterfaceUuid") ||
        fields.ContainsKey("RpcInterfaceUUID");

    private static bool MetadataEquals(IReadOnlyDictionary<string, object?> metadata, string key, string expected) =>
        metadata.TryGetValue(key, out var value) &&
        value?.ToString()?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true;
}
