using DeltaZulu.Pipeline.Inputs.Etw;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class EtwIdentityResolverTests
{
    [TestMethod]
    public void FileIdentityResolver_Resolve_ReturnsFileObjectCacheProvenance()
    {
        var resolver = new FileIdentityResolver();
        var createdAt = DateTimeOffset.Parse("2026-07-01T18:22:31Z");

        resolver.ObserveCreate(0x1234, null, "\\Device\\HarddiskVolume3\\Temp\\a.txt", createdAt);
        var resolved = resolver.Resolve(0x1234, null, createdAt.AddMilliseconds(183));

        Assert.AreEqual("\\Device\\HarddiskVolume3\\Temp\\a.txt", resolved.ResolvedFilePath);
        Assert.AreEqual(FileResolutionSource.FileObjectCache, resolved.FileResolutionSource);
        Assert.AreEqual(ResolutionConfidence.High, resolved.FileResolutionConfidence);
        Assert.AreEqual(183, resolved.FileResolutionAgeMs);
        Assert.AreEqual("FileObject=0x1234", resolved.FileResolverKey);
        Assert.IsFalse(resolved.FileResolverMiss);
    }

    [TestMethod]
    public void FileIdentityResolver_Resolve_EmitsMissForUnresolvedObject()
    {
        var resolver = new FileIdentityResolver();

        var resolved = resolver.Resolve(0x2222, null, DateTimeOffset.Parse("2026-07-01T18:22:31Z"));

        Assert.IsNull(resolved.ResolvedFilePath);
        Assert.AreEqual(FileResolutionSource.Unknown, resolved.FileResolutionSource);
        Assert.AreEqual(ResolutionConfidence.Unknown, resolved.FileResolutionConfidence);
        Assert.IsNull(resolved.FileResolutionAgeMs);
        Assert.AreEqual("FileObject=0x2222", resolved.FileResolverKey);
        Assert.IsTrue(resolved.FileResolverMiss);
    }

    [TestMethod]
    public void FileIdentityResolver_Delete_DowngradesConfidenceBeforeExpiry()
    {
        var resolver = new FileIdentityResolver(deletedTtl: TimeSpan.FromSeconds(30));
        var createdAt = DateTimeOffset.Parse("2026-07-01T18:22:31Z");
        resolver.ObserveCreate(0x1234, null, "\\Device\\HarddiskVolume3\\Temp\\a.txt", createdAt);

        resolver.ObserveDelete(0x1234, createdAt.AddSeconds(1));
        var stale = resolver.Resolve(0x1234, null, createdAt.AddSeconds(2));

        Assert.AreEqual("\\Device\\HarddiskVolume3\\Temp\\a.txt", stale.ResolvedFilePath);
        Assert.AreEqual(FileResolutionSource.DelayedCache, stale.FileResolutionSource);
        Assert.AreEqual(ResolutionConfidence.Low, stale.FileResolutionConfidence);
        Assert.IsFalse(stale.FileResolverMiss);
    }

    [TestMethod]
    public void FileIdentityResolver_Delete_ExpiresStaleEntryAfterDeletedTtl()
    {
        var resolver = new FileIdentityResolver(deletedTtl: TimeSpan.FromSeconds(30));
        var createdAt = DateTimeOffset.Parse("2026-07-01T18:22:31Z");
        resolver.ObserveCreate(0x1234, null, "\\Device\\HarddiskVolume3\\Temp\\a.txt", createdAt);
        resolver.ObserveDelete(0x1234, createdAt.AddSeconds(1));

        var expired = resolver.Resolve(0x1234, null, createdAt.AddSeconds(32));

        Assert.IsNull(expired.ResolvedFilePath);
        Assert.AreEqual(FileResolutionSource.Unknown, expired.FileResolutionSource);
        Assert.IsTrue(expired.FileResolverMiss);
    }

    [TestMethod]
    public void ThreadIdentityResolver_MapsThreadToProcessUntilStop()
    {
        var resolver = new ThreadIdentityResolver();

        resolver.ObserveThreadStart(9124, 4820);
        Assert.AreEqual(4820, resolver.ResolveProcessId(9124));

        resolver.ObserveThreadStop(9124);
        Assert.IsNull(resolver.ResolveProcessId(9124));
    }

    [TestMethod]
    public void ProcessIdentityResolver_UsesGenerationKeyWhenAvailable()
    {
        var resolver = new ProcessIdentityResolver();
        var startedAt = DateTimeOffset.Parse("2026-07-01T18:22:31Z");
        var observedAt = startedAt.AddSeconds(5);

        resolver.ObserveProcess(
            4820,
            startedAt,
            4000,
            "C:\\Windows\\System32\\notepad.exe",
            "notepad.exe C:\\Temp\\a.txt",
            "S-1-5-18",
            observedAt);

        var resolved = resolver.Resolve(4820, startedAt, observedAt.AddMilliseconds(250));

        Assert.AreEqual("C:\\Windows\\System32\\notepad.exe", resolved.ResolvedProcessImage);
        Assert.AreEqual("notepad.exe C:\\Temp\\a.txt", resolved.ResolvedProcessCommandLine);
        Assert.AreEqual("ProcessGenerationCache", resolved.ProcessResolutionSource);
        Assert.AreEqual(ResolutionConfidence.High, resolved.ProcessResolutionConfidence);
        Assert.AreEqual(250, resolved.ProcessResolutionAgeMs);
        Assert.Contains("ProcessId=4820", resolved.ProcessGenerationKey);
    }
}
