using DeltaZulu.Pipeline.Core.Checkpoints;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class FileSourceCheckpointStoreTests
{
    [TestMethod]
    public void SaveThenLoad_RoundTripsToken()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var store = new FileSourceCheckpointStore(dir.FullName);
            store.Save("windows.eventlog.Security", "12345");

            Assert.IsTrue(store.TryLoad("windows.eventlog.Security", out var token));
            Assert.AreEqual("12345", token);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Save_OverwritesPreviousValue()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var store = new FileSourceCheckpointStore(dir.FullName);
            store.Save("k", "1");
            store.Save("k", "2");

            Assert.IsTrue(store.TryLoad("k", out var token));
            Assert.AreEqual("2", token);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void TryLoad_MissingOrEmpty_ReturnsFalse()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var store = new FileSourceCheckpointStore(dir.FullName);
            Assert.IsFalse(store.TryLoad("absent", out _));

            store.Save("blank", "   ");
            Assert.IsFalse(store.TryLoad("blank", out _), "Whitespace-only content is treated as no checkpoint.");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Keys_WithPathSeparators_AreSanitizedToDistinctFiles()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var store = new FileSourceCheckpointStore(dir.FullName);
            store.Save("windows.eventlog.Microsoft-Windows-Sysmon/Operational", "7");

            Assert.IsTrue(store.TryLoad("windows.eventlog.Microsoft-Windows-Sysmon/Operational", out var token));
            Assert.AreEqual("7", token);
            // The channel path separator must not create nested directories or escape the store dir.
            Assert.IsEmpty(Directory.GetDirectories(dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void NullStore_NeverPersistsAndAlwaysMisses()
    {
        var store = NullSourceCheckpointStore.Instance;
        store.Save("k", "1");
        Assert.IsFalse(store.TryLoad("k", out _));
    }
}
