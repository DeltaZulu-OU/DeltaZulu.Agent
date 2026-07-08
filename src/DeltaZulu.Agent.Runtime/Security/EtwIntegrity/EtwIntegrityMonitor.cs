using System.Diagnostics;
using System.Security.Cryptography;

namespace DeltaZulu.Agent.Runtime.Security.EtwIntegrity;

public sealed class EtwIntegrityMonitor : IAsyncDisposable, IDisposable
{
    private readonly EtwIntegrityOptions _options;
    private readonly IProcessMemoryReader _memoryReader;
    private readonly IEtwIntegrityReporter _reporter;
    private readonly Func<IProcessMemoryReader, IReadOnlyList<EtwFunctionBaseline>> _baselineFactory;
    private readonly bool _requireWindows;
    private readonly List<EtwTargetState> _targets = [];

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private int _running;

    public EtwIntegrityMonitor(
        EtwIntegrityOptions options,
        IProcessMemoryReader memoryReader,
        IEtwIntegrityReporter reporter)
        : this(
            options,
            memoryReader,
            reporter,
            reader => new NtdllEtwTargetResolver(reader).ResolveAndBaseline(options.TargetFunctions, options.PrologueSize),
            requireWindows: true)
    {
    }

    internal EtwIntegrityMonitor(
        EtwIntegrityOptions options,
        IProcessMemoryReader memoryReader,
        IEtwIntegrityReporter reporter,
        Func<IProcessMemoryReader, IReadOnlyList<EtwFunctionBaseline>> baselineFactory,
        bool requireWindows)
    {
        _options = options;
        _memoryReader = memoryReader;
        _reporter = reporter;
        _baselineFactory = baselineFactory;
        _requireWindows = requireWindows;
    }

    public IReadOnlyList<EtwFunctionBaseline> Baselines => _targets.Select(t => t.Baseline).ToArray();

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (_requireWindows)
            {
                _options.Validate();
            }
            else
            {
                _options.ValidatePortable();
            }

            var baselines = _baselineFactory(_memoryReader);
            if (baselines.Count == 0)
            {
                throw new InvalidOperationException("No ETW target baselines could be created.");
            }

            _targets.Clear();
            foreach (var baseline in baselines)
            {
                _targets.Add(new EtwTargetState(baseline));
            }

            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        }
        catch
        {
            Interlocked.Exchange(ref _running, 0);
            throw;
        }
    }

    public async ValueTask StopAsync()
    {
        if (Interlocked.Exchange(ref _running, 0) == 0)
        {
            return;
        }

        var cts = _cts;
        var task = _monitorTask;
        cts?.Cancel();

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        cts?.Dispose();
        _cts = null;
        _monitorTask = null;
        _targets.Clear();
    }

    public void Dispose() => StopAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.CheckInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var target in _targets)
            {
                await CheckTargetAsync(target, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask CheckTargetAsync(EtwTargetState target, CancellationToken cancellationToken)
    {
        if (!target.Valid)
        {
            return;
        }

        var read = _memoryReader.TryRead(target.Baseline.LiveAddress, _options.PrologueSize);
        if (!read.Success)
        {
            target.ReadFailureCount++;

            if (target.ReadFailureCount >= _options.ConsecutiveReadFailuresBeforeDisable)
            {
                target.Valid = false;
                var finding = BuildReadFailureFinding(target, read.Error ?? "Unknown read failure.");
                await SafeReportAsync(finding, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        target.ReadFailureCount = 0;
        var result = EtwBypassPatternDetector.Detect(
            read.Bytes,
            target.Baseline.BaselineBytes,
            target.Baseline.FunctionName,
            _options.PrologueSize);

        if (!result.IsValid)
        {
            return;
        }

        if (!result.IsDetected)
        {
            target.LastReportedCurrentSha256 = null;
            return;
        }

        string currentSha256 = Convert.ToHexString(SHA256.HashData(read.Bytes));
        if (StringComparer.Ordinal.Equals(target.LastReportedCurrentSha256, currentSha256))
        {
            return;
        }

        target.LastReportedCurrentSha256 = currentSha256;
        var tamperFinding = BuildTamperFinding(target, read.Bytes, currentSha256, result);
        await SafeReportAsync(tamperFinding, cancellationToken).ConfigureAwait(false);
    }

    private EtwIntegrityFinding BuildTamperFinding(
        EtwTargetState target,
        byte[] currentBytes,
        string currentSha256,
        EtwIntegrityDetectionResult result)
    {
        using Process process = Process.GetCurrentProcess();

        return new EtwIntegrityFinding(
            target.Baseline.FunctionName,
            target.Baseline.ModuleName,
            target.Baseline.ModulePath,
            target.Baseline.LiveAddress,
            _options.PrologueSize,
            result.Pattern,
            result.Detail,
            DateTimeOffset.UtcNow,
            target.Baseline.BaselineBytes,
            currentBytes,
            target.Baseline.BaselineSha256,
            currentSha256,
            target.Baseline.BaselineSource,
            target.Baseline.ProcessId,
            process.ProcessName,
            target.Baseline.ProcessArchitecture,
            result.ChangedOffset,
            result.ExpectedByte,
            result.ActualByte,
            result.ForcedReturnValue);
    }

    private EtwIntegrityFinding BuildReadFailureFinding(EtwTargetState target, string error)
    {
        using Process process = Process.GetCurrentProcess();

        return new EtwIntegrityFinding(
            target.Baseline.FunctionName,
            target.Baseline.ModuleName,
            target.Baseline.ModulePath,
            target.Baseline.LiveAddress,
            _options.PrologueSize,
            EtwIntegrityPattern.ReadFailure,
            $"Failed to read ETW target memory: {error}",
            DateTimeOffset.UtcNow,
            target.Baseline.BaselineBytes,
            Array.Empty<byte>(),
            target.Baseline.BaselineSha256,
            string.Empty,
            target.Baseline.BaselineSource,
            target.Baseline.ProcessId,
            process.ProcessName,
            target.Baseline.ProcessArchitecture,
            null,
            null,
            null,
            null);
    }

    private async ValueTask SafeReportAsync(EtwIntegrityFinding finding, CancellationToken cancellationToken)
    {
        try
        {
            await _reporter.ReportAsync(finding, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Do not terminate integrity monitoring because the reporting path failed.
        }
    }

    private sealed class EtwTargetState(EtwFunctionBaseline baseline)
    {
        public EtwFunctionBaseline Baseline { get; } = baseline;

        public bool Valid { get; set; } = true;

        public int ReadFailureCount { get; set; }

        public string? LastReportedCurrentSha256 { get; set; }
    }
}
