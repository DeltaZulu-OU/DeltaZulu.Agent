namespace DeltaZulu.Pipeline.Enrichment.Rpc;

public sealed record RpcOperationDescriptor(
    string InterfaceUuid,
    string InterfaceName,
    int ProcNum,
    string OperationName,
    string OperationCategory,
    string ResolverVersion);

public static class RpcOperationResolver
{
    public const string CurrentResolverVersion = "rpc-map-2026.07.1";

    private static readonly Dictionary<(string InterfaceUuid, int ProcNum), RpcOperationDescriptor> Operations = BuildMap();
    private static readonly Dictionary<string, string> InterfaceNames = BuildInterfaceNameCache();

    public static RpcOperationDescriptor? Resolve(string? interfaceUuid, int procNum)
    {
        if (string.IsNullOrWhiteSpace(interfaceUuid))
        {
            return null;
        }

        var normalized = NormalizeUuid(interfaceUuid);
        return Operations.TryGetValue((normalized, procNum), out var descriptor) ? descriptor : null;
    }

    public static string? ResolveInterfaceName(string? interfaceUuid)
    {
        if (string.IsNullOrWhiteSpace(interfaceUuid))
        {
            return null;
        }

        var normalized = NormalizeUuid(interfaceUuid);
        return InterfaceNames.TryGetValue(normalized, out var interfaceName) ? interfaceName : null;
    }

    public static string NormalizeUuid(string interfaceUuid) => interfaceUuid.Trim().Trim('{', '}').ToLowerInvariant();

    private static Dictionary<(string InterfaceUuid, int ProcNum), RpcOperationDescriptor> BuildMap()
    {
        var map = new Dictionary<(string InterfaceUuid, int ProcNum), RpcOperationDescriptor>();

        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 0, "RCloseServiceHandle", "ServiceControl");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 1, "RControlService", "ServiceControl");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 2, "RDeleteService", "ServiceDelete");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 11, "RChangeServiceConfigW", "ServiceConfigChange");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 12, "RCreateServiceW", "ServiceCreate");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 15, "ROpenSCManagerW", "ServiceOpenContext");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 16, "ROpenServiceW", "ServiceOpenContext");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 19, "RStartServiceW", "ServiceStart");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 23, "RChangeServiceConfigA", "ServiceConfigChange");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 24, "RCreateServiceA", "ServiceCreate");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 27, "ROpenSCManagerA", "ServiceOpenContext");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 28, "ROpenServiceA", "ServiceOpenContext");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 31, "RStartServiceA", "ServiceStart");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 36, "RChangeServiceConfig2A", "ServiceConfigChange");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 37, "RChangeServiceConfig2W", "ServiceConfigChange");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 44, "RCreateServiceWOW64A", "ServiceCreate");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 45, "RCreateServiceWOW64W", "ServiceCreate");
        Add(map, "367abb81-9844-35f1-ad32-98f038001003", "MS-SCMR", 60, "RCreateWowService", "ServiceCreate");

        Add(map, "00000136-0000-0000-c000-000000000046", "ISCMLocalActivator", 3, "SCMActivatorGetClassObject", "ComActivation");

        Add(map, "e3514235-4b06-11d1-ab04-00c04fc2dcd2", "MS-DRSR", 0, "IDL_DRSBind", "DirectoryReplicationBind");
        Add(map, "e3514235-4b06-11d1-ab04-00c04fc2dcd2", "MS-DRSR", 3, "IDL_DRSGetNCChanges", "DirectoryReplicationGetChanges");

        return map;
    }

    private static Dictionary<string, string> BuildInterfaceNameCache()
    {
        var interfaceNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddInterface(interfaceNames, "00000136-0000-0000-c000-000000000046", "ISCMLocalActivator");
        AddInterface(interfaceNames, "000001a0-0000-0000-c000-000000000046", "IRemoteSCMActivator");
        AddInterface(interfaceNames, "4d9f4ab8-7d1c-11cf-861e-0020af6e7c57", "IActivation");
        AddInterface(interfaceNames, "99fcfec4-5260-101b-bbcb-00aa0021347a", "IObjectExporter");

        foreach (var descriptor in Operations.Values)
        {
            interfaceNames.TryAdd(descriptor.InterfaceUuid, descriptor.InterfaceName);
        }

        return interfaceNames;
    }

    private static void AddInterface(
        IDictionary<string, string> map,
        string interfaceUuid,
        string interfaceName) => map[NormalizeUuid(interfaceUuid)] = interfaceName;

    private static void Add(
        IDictionary<(string InterfaceUuid, int ProcNum), RpcOperationDescriptor> map,
        string interfaceUuid,
        string interfaceName,
        int procNum,
        string operationName,
        string operationCategory)
    {
        var normalized = NormalizeUuid(interfaceUuid);
        map[(normalized, procNum)] = new RpcOperationDescriptor(
            normalized,
            interfaceName,
            procNum,
            operationName,
            operationCategory,
            CurrentResolverVersion);
    }
}
