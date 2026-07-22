using DeltaZulu.Pipeline.Inputs.Windows;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class EventLogStartPositionResolverTests
{
    [TestMethod]
    public void FromNow_ResolvesToNewest()
    {
        var resolved = EventLogStartPositionResolver.Resolve(EventLogStartPosition.FromNow);
        Assert.AreEqual(ResolvedStartKind.Newest, resolved.Kind);
    }

    [TestMethod]
    public void FromOldest_ResolvesToOldest()
    {
        var resolved = EventLogStartPositionResolver.Resolve(EventLogStartPosition.FromOldest);
        Assert.AreEqual(ResolvedStartKind.Oldest, resolved.Kind);
    }

    [TestMethod]
    public void FromRecordId_ResolvesToAfterThatRecord()
    {
        var resolved = EventLogStartPositionResolver.Resolve(EventLogStartPosition.FromRecordId, configuredRecordId: 987);
        Assert.AreEqual(ResolvedStartKind.AfterRecordId, resolved.Kind);
        Assert.AreEqual(987, resolved.RecordId);
    }

    [TestMethod]
    public void FromRecordId_WithoutRecordId_Throws() => Assert.ThrowsExactly<ArgumentException>(() =>
                                                                  EventLogStartPositionResolver.Resolve(EventLogStartPosition.FromRecordId));

    [TestMethod]
    public void Lookback_ResolvesToWindow()
    {
        var resolved = EventLogStartPositionResolver.Resolve(EventLogStartPosition.Lookback, lookback: TimeSpan.FromHours(2));
        Assert.AreEqual(ResolvedStartKind.Lookback, resolved.Kind);
        Assert.AreEqual(TimeSpan.FromHours(2), resolved.Lookback);
    }

    [TestMethod]
    public void Lookback_WithoutDuration_Throws() => Assert.ThrowsExactly<ArgumentException>(() =>
                                                              EventLogStartPositionResolver.Resolve(EventLogStartPosition.Lookback));

    [TestMethod]
    public void Bookmark_WithValidToken_ResumesAfterThatRecord()
    {
        var resolved = EventLogStartPositionResolver.Resolve(EventLogStartPosition.Bookmark, bookmarkToken: "5000");
        Assert.AreEqual(ResolvedStartKind.AfterRecordId, resolved.Kind);
        Assert.AreEqual(5000, resolved.RecordId);
    }

    [TestMethod]
    public void Bookmark_Absent_UsesDefaultFallbackFromNow()
    {
        var resolved = EventLogStartPositionResolver.Resolve(EventLogStartPosition.Bookmark, bookmarkToken: null);
        Assert.AreEqual(ResolvedStartKind.Newest, resolved.Kind);
    }

    [TestMethod]
    public void Bookmark_InvalidToken_UsesFallback()
    {
        var resolved = EventLogStartPositionResolver.Resolve(EventLogStartPosition.Bookmark, bookmarkToken: "not-a-number");
        Assert.AreEqual(ResolvedStartKind.Newest, resolved.Kind);
    }

    [TestMethod]
    public void Bookmark_Absent_HonorsCustomFallback()
    {
        var resolved = EventLogStartPositionResolver.Resolve(
            EventLogStartPosition.Bookmark,
            bookmarkToken: null,
            bookmarkFallback: EventLogStartPosition.FromOldest);
        Assert.AreEqual(ResolvedStartKind.Oldest, resolved.Kind);
    }

    [TestMethod]
    public void Bookmark_Absent_WithBookmarkFallback_DoesNotRecurseAndDefaultsToNewest()
    {
        var resolved = EventLogStartPositionResolver.Resolve(
            EventLogStartPosition.Bookmark,
            bookmarkToken: null,
            bookmarkFallback: EventLogStartPosition.Bookmark);
        Assert.AreEqual(ResolvedStartKind.Newest, resolved.Kind);
    }
}
