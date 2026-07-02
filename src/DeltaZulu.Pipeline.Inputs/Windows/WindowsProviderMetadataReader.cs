using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using DeltaZulu.Pipeline.Core.Windows;

namespace DeltaZulu.Pipeline.Inputs.Windows;

/// <summary>
/// Collects Windows event schema (fields per provider/event) from the local host using the .NET
/// Event Log reader (<see cref="ProviderMetadata"/>). This reads schema dynamically from registered
/// provider manifests — there is no maintained data catalog to keep in sync.
/// </summary>
/// <remarks>
/// Uses the BCL <c>System.Diagnostics.Eventing.Reader</c> API (already used by the Event Log input),
/// so there is no new dependency. Per-provider failures (missing/disabled/inaccessible metadata) are
/// isolated and skipped. Metadata reads are host- and OS-build-dependent and are intended for CLI
/// diagnostics and profile authoring, never the live collection hot path.
/// </remarks>
public sealed class WindowsProviderMetadataReader
{
    /// <summary>Enumerates the names of providers registered on the local host.</summary>
    public IReadOnlyList<string> ListProviderNames()
    {
        using var session = new EventLogSession();
        return session.GetProviderNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Reads the event schema declared by a single provider. Returns an empty list when the
    /// provider's metadata cannot be loaded on this host.
    /// </summary>
    public IReadOnlyList<WindowsEventSchema> ReadProvider(string providerName)
    {
        try
        {
            using var metadata = new ProviderMetadata(providerName);
            return MapEvents(metadata);
        }
        catch (EventLogException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (Win32Exception)
        {
            return [];
        }
    }

    /// <summary>Resolves a provider GUID to its registered name, or <see langword="null"/> if not found.</summary>
    public string? ResolveProviderNameByGuid(Guid providerGuid)
    {
        foreach (var name in ListProviderNames())
        {
            try
            {
                using var metadata = new ProviderMetadata(name);
                if (metadata.Id == providerGuid)
                {
                    return name;
                }
            }
            catch (EventLogException) { }
            catch (UnauthorizedAccessException) { }
            catch (Win32Exception) { }
        }

        return null;
    }

    private static IReadOnlyList<WindowsEventSchema> MapEvents(ProviderMetadata metadata)
    {
        var schemas = new List<WindowsEventSchema>();
        var providerName = metadata.Name;

        // Enumerating Events materializes per-event metadata lazily and can throw; isolate it.
        IEnumerable<EventMetadata> events;
        try
        {
            events = metadata.Events.ToList();
        }
        catch (EventLogException)
        {
            return schemas;
        }

        foreach (var ev in events)
        {
            // Keep every declared event. Pure-ETW events often have no channel link (LogLink is
            // null); those are exactly the "fields per ETW source" schema the reader must expose,
            // so channel is recorded when present but is never a reason to drop an event.
            var channel = ev.LogLink?.LogName;

            schemas.Add(new WindowsEventSchema
            {
                Provider = providerName,
                EventId = (int)ev.Id,
                Channel = string.IsNullOrWhiteSpace(channel) ? null : channel,
                Fields = WindowsEventTemplateParser.ExtractFields(ev.Template)
            });
        }

        return schemas;
    }
}
