namespace DeltaZulu.Pipeline.Inputs.Windows;

/// <summary>
/// Pure, host-neutral resolution of an <see cref="EventLogStartPosition"/> policy (plus any
/// configured record id / lookback and a persisted bookmark token) into a concrete
/// <see cref="ResolvedStartPosition"/> the Windows reader executes. Kept free of any Windows API so
/// the branching logic — especially the bookmark-with-fallback behavior — is unit-testable.
/// </summary>
public static class EventLogStartPositionResolver
{
    /// <summary>
    /// Resolves the initial read instruction.
    /// </summary>
    /// <param name="position">The configured start-position policy.</param>
    /// <param name="configuredRecordId">Required when <paramref name="position"/> is <see cref="EventLogStartPosition.FromRecordId"/>.</param>
    /// <param name="lookback">Required when <paramref name="position"/> is <see cref="EventLogStartPosition.Lookback"/>.</param>
    /// <param name="bookmarkToken">The persisted bookmark token, if any (an EventRecordID as a string).</param>
    /// <param name="bookmarkFallback">
    /// Fallback used when <paramref name="position"/> is <see cref="EventLogStartPosition.Bookmark"/> but no valid
    /// bookmark is present. Defaults to <see cref="EventLogStartPosition.FromNow"/> for safe daemon behavior.
    /// </param>
    public static ResolvedStartPosition Resolve(
        EventLogStartPosition position,
        long? configuredRecordId = null,
        TimeSpan? lookback = null,
        string? bookmarkToken = null,
        EventLogStartPosition bookmarkFallback = EventLogStartPosition.FromNow)
    {
        switch (position)
        {
            case EventLogStartPosition.FromNow:
                return ResolvedStartPosition.Newest;

            case EventLogStartPosition.FromOldest:
                return ResolvedStartPosition.Oldest;

            case EventLogStartPosition.FromRecordId:
                return ResolvedStartPosition.AfterRecord(
                    configuredRecordId ?? throw new ArgumentException("startPosition 'fromRecordId' requires a recordId."));

            case EventLogStartPosition.Lookback:
                return ResolvedStartPosition.Within(
                    lookback ?? throw new ArgumentException("startPosition 'lookback' requires a lookback duration."));

            case EventLogStartPosition.Bookmark:
                if (TryParseBookmark(bookmarkToken, out var recordId))
                {
                    return ResolvedStartPosition.AfterRecord(recordId);
                }

                if (bookmarkFallback == EventLogStartPosition.Bookmark)
                {
                    // Avoid infinite fallback recursion; default to the safe daemon behavior.
                    return ResolvedStartPosition.Newest;
                }

                return Resolve(bookmarkFallback, configuredRecordId, lookback);

            default:
                return ResolvedStartPosition.Newest;
        }
    }

    private static bool TryParseBookmark(string? token, out long recordId)
    {
        recordId = 0;
        return !string.IsNullOrWhiteSpace(token)
            && long.TryParse(token.Trim(), out recordId)
            && recordId >= 0;
    }
}
