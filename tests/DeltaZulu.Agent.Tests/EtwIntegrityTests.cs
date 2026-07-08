using System.Collections.Concurrent;
using System.Security.Cryptography;
using DeltaZulu.Agent.Runtime.Security.EtwIntegrity;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class EtwIntegrityTests
{
    private static readonly byte[] Baseline = [0x4C, 0x8B, 0xD1, 0xB8, 0x00, 0x00, 0x00, 0x00];

    [TestMethod]
    [DataRow(new byte[] { 0xC3, 0x8B, 0xD1, 0xB8, 0x00, 0x00, 0x00, 0x00 }, EtwIntegrityPattern.Ret)]
    [DataRow(new byte[] { 0x90, 0x90, 0x90, 0x90, 0x00, 0x00, 0x00, 0x00 }, EtwIntegrityPattern.NopSled)]
    [DataRow(new byte[] { 0x31, 0xC0, 0xC3, 0xB8, 0x00, 0x00, 0x00, 0x00 }, EtwIntegrityPattern.XorEaxRet)]
    [DataRow(new byte[] { 0x33, 0xC0, 0xC3, 0xB8, 0x00, 0x00, 0x00, 0x00 }, EtwIntegrityPattern.XorEaxRet)]
    [DataRow(new byte[] { 0xB8, 0x22, 0x11, 0x00, 0x00, 0xC3, 0x00, 0x00 }, EtwIntegrityPattern.MovEaxImmediateRet)]
    public void Detector_ClassifiesKnownPatchPatterns(byte[] current, EtwIntegrityPattern expectedPattern)
    {
        var result = EtwBypassPatternDetector.Detect(current, Baseline, "EtwEventWrite", 8);

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.IsDetected);
        Assert.AreEqual(expectedPattern, result.Pattern);
    }

    [TestMethod]
    public void Detector_ClassifiesGenericModification()
    {
        var current = (byte[])Baseline.Clone();
        current[6] = 0xFF;

        var result = EtwBypassPatternDetector.Detect(current, Baseline, "NtTraceEvent", 8);

        Assert.IsTrue(result.IsDetected);
        Assert.AreEqual(EtwIntegrityPattern.GenericModification, result.Pattern);
        Assert.AreEqual(6, result.ChangedOffset);
        Assert.AreEqual(result.ExpectedByte, Baseline[6]);
        Assert.AreEqual(0xFF, (int)result.ActualByte!);
    }

    [TestMethod]
    public void Detector_ReturnsCleanWhenBytesMatchBaseline()
    {
        var result = EtwBypassPatternDetector.Detect(Baseline, Baseline, "EtwEventWrite", 8);

        Assert.IsTrue(result.IsValid);
        Assert.IsFalse(result.IsDetected);
        Assert.AreEqual(EtwIntegrityPattern.None, result.Pattern);
    }

    [TestMethod]
    public async Task Monitor_SuppressesSameBytes_ReportsChangedBytes_AndClearsAfterBaseline()
    {
        var memoryReader = new SequenceMemoryReader(
            MemoryReadResult.Ok([0xC3, 0x8B, 0xD1, 0xB8, 0x00, 0x00, 0x00, 0x00]),
            MemoryReadResult.Ok([0xC3, 0x8B, 0xD1, 0xB8, 0x00, 0x00, 0x00, 0x00]),
            MemoryReadResult.Ok([0x90, 0x90, 0x90, 0x90, 0x00, 0x00, 0x00, 0x00]),
            MemoryReadResult.Ok(Baseline),
            MemoryReadResult.Ok([0x90, 0x90, 0x90, 0x90, 0x00, 0x00, 0x00, 0x00]));
        var reporter = new CapturingReporter();
        await using var monitor = CreateTestMonitor(memoryReader, reporter);

        monitor.Start();
        await WaitForFindingsAsync(reporter, 3);
        await monitor.StopAsync();

        CollectionAssert.AreEqual(
            new[] { EtwIntegrityPattern.Ret, EtwIntegrityPattern.NopSled, EtwIntegrityPattern.NopSled },
            reporter.Findings.Select(f => f.Pattern).ToArray());
    }

    [TestMethod]
    public async Task Monitor_DisablesTargetAfterConfiguredReadFailures()
    {
        var memoryReader = new SequenceMemoryReader(
            MemoryReadResult.Fail("one"),
            MemoryReadResult.Fail("two"),
            MemoryReadResult.Ok([0xC3, 0x8B, 0xD1, 0xB8, 0x00, 0x00, 0x00, 0x00]));
        var reporter = new CapturingReporter();
        await using var monitor = CreateTestMonitor(memoryReader, reporter, failuresBeforeDisable: 2);

        monitor.Start();
        await WaitForFindingsAsync(reporter, 1);
        await Task.Delay(500, TestContext.CancellationToken);
        await monitor.StopAsync();

        Assert.HasCount(1, reporter.Findings);
        Assert.AreEqual(EtwIntegrityPattern.ReadFailure, reporter.Findings[0].Pattern);
    }

    private static EtwIntegrityMonitor CreateTestMonitor(
        IProcessMemoryReader memoryReader,
        IEtwIntegrityReporter reporter,
        int failuresBeforeDisable = 3)
    {
        var options = new EtwIntegrityOptions
        {
            PrologueSize = 8,
            CheckInterval = TimeSpan.FromMilliseconds(250),
            ConsecutiveReadFailuresBeforeDisable = failuresBeforeDisable
        };

        EtwFunctionBaseline baseline = new(
            "ntdll.dll",
            "EtwEventWrite",
            "C:\\Windows\\System32\\ntdll.dll",
            new IntPtr(0x1234),
            Baseline,
            Convert.ToHexString(SHA256.HashData(Baseline)),
            EtwBaselineSource.LiveProcessStartup,
            DateTimeOffset.UtcNow,
            Environment.ProcessId,
            "X64");

        return new EtwIntegrityMonitor(options, memoryReader, reporter, _ => [baseline], requireWindows: false);
    }

    private static async Task WaitForFindingsAsync(CapturingReporter reporter, int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (reporter.Findings.Count < count && !cts.IsCancellationRequested)
        {
            await Task.Delay(25);
        }

        Assert.IsGreaterThanOrEqualTo(count, reporter.Findings.Count, $"Expected at least {count} finding(s), got {reporter.Findings.Count}.");
    }

    private sealed class SequenceMemoryReader(params MemoryReadResult[] reads) : IProcessMemoryReader
    {
        private readonly ConcurrentQueue<MemoryReadResult> _reads = new(reads);
        private MemoryReadResult _last = reads.LastOrDefault() ?? MemoryReadResult.Fail("No reads configured.");

        public MemoryReadResult TryRead(IntPtr address, int byteCount)
        {
            if (_reads.TryDequeue(out var read))
            {
                _last = read;
                return read;
            }

            return _last;
        }
    }

    private sealed class CapturingReporter : IEtwIntegrityReporter
    {
        private readonly List<EtwIntegrityFinding> _findings = [];

        public IReadOnlyList<EtwIntegrityFinding> Findings
        {
            get
            {
                lock (_findings)
                {
                    return _findings.ToArray();
                }
            }
        }

        public ValueTask ReportAsync(EtwIntegrityFinding finding, CancellationToken cancellationToken)
        {
            lock (_findings)
            {
                _findings.Add(finding);
            }

            return ValueTask.CompletedTask;
        }
    }

    public TestContext TestContext { get; set; }
}
