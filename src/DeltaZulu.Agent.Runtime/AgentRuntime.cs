using System.Runtime.ExceptionServices;
using DeltaZulu.Pipeline.Core;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Enrichment;

namespace DeltaZulu.Agent.Runtime;

public sealed class AgentRuntime
{
    private readonly IReadOnlyList<ProfileBinding> _bindings;
    private readonly IOutputWriter _sink;
    private readonly AgentObservationAccumulator? _observations;
    private readonly Action<string>? _warn;

    public AgentRuntime(
        IReadOnlyList<ProfileBinding> bindings,
        IOutputWriter sink,
        AgentObservationAccumulator? observations = null,
        Action<string>? warn = null)
    {
        _bindings = bindings;
        _sink = sink;
        _observations = observations;
        _warn = warn;
    }

    public AgentRuntimeResult Run(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        AgentRuntimeResult result;

        if (_bindings.Count == 1)
        {
            result = RunSingle(_bindings[0], warnings, cancellationToken);
        }
        else
        {
            result = RunMultiple(warnings, cancellationToken);
        }

        return warnings.Count > 0
            ? result with { Warnings = warnings }
            : result;
    }

    private AgentRuntimeResult RunSingle(ProfileBinding binding, List<string> warnings, CancellationToken cancellationToken)
    {
        using var completed = new ManualResetEventSlim(false);
        using var writer = new CompletionTrackingWriter(_sink, completed);
        using var reloadableExecutor = CreateReloadableExecutor(binding);
        var pipeline = new ResourcePipeline(
            binding.Input,
            source => ExecuteBinding(binding, reloadableExecutor, source, cancellationToken),
            writer,
            _observations,
            ResourceOutputEnricher.EnrichAfterFilter);

        try
        {
            using var subscription = pipeline.Start(cancellationToken);
            completed.Wait(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HandleSingleBindingFailure(binding, warnings, ex);
        }

        if (writer.Error is not null)
        {
            return HandleSingleBindingFailure(binding, warnings, writer.Error);
        }

        return new AgentRuntimeResult(true);
    }

    private static HotSwappableProfileExecutor? CreateReloadableExecutor(ProfileBinding binding) =>
        binding.ProfileReloads is null
            ? null
            : new HotSwappableProfileExecutor(binding.Executor, binding.ProfileReloads);

    private static IObservable<ResourceOutputRecord> ExecuteBinding(
        ProfileBinding binding,
        HotSwappableProfileExecutor? reloadableExecutor,
        IObservable<SourceEvent> source,
        CancellationToken cancellationToken) =>
        reloadableExecutor is null
            ? binding.Executor.Execute(source, binding.Profile, cancellationToken)
            : reloadableExecutor.Execute(source, cancellationToken);

    private AgentRuntimeResult HandleSingleBindingFailure(ProfileBinding binding, List<string> warnings, Exception exception)
    {
        var message = $"profile '{binding.Profile.Id}' failed: {exception.Message}";
        if (!binding.Profile.Mandatory)
        {
            message += " (skipped because mandatory is false)";
            _warn?.Invoke(message);
            warnings.Add(message);
            return new AgentRuntimeResult(true);
        }

        return new AgentRuntimeResult(false, exception);
    }

    private AgentRuntimeResult RunMultiple(List<string> warnings, CancellationToken cancellationToken)
    {
        using var mux = new ChannelOutputMultiplexer(_sink);
        var tasks = _bindings
            .Select(binding => Task.Run(() => RunBindingSafely(binding, warnings, mux, cancellationToken), cancellationToken))
            .ToArray();

        try
        {
            Task.WaitAll(tasks, cancellationToken);
        }
        catch (AggregateException ex)
        {
            return new AgentRuntimeResult(false, ex.InnerExceptions.Count == 1
                ? ex.InnerExceptions[0]
                : ex.Flatten());
        }

        mux.Complete();

        return mux.Error is not null
            ? new AgentRuntimeResult(false, mux.Error)
            : new AgentRuntimeResult(true);
    }

    private void RunBindingSafely(ProfileBinding binding, List<string> warnings, IOutputWriter sink, CancellationToken cancellationToken)
    {
        try
        {
            RunBinding(binding, sink, cancellationToken);
        }
        catch (Exception ex) when (!binding.Profile.Mandatory)
        {
            var message = $"profile '{binding.Profile.Id}' failed: {ex.Message} (skipped because mandatory is false)";
            _warn?.Invoke(message);
            lock (warnings)
            {
                warnings.Add(message);
            }
        }
    }

    private void RunBinding(ProfileBinding binding, IOutputWriter sink, CancellationToken cancellationToken)
    {
        using var completed = new ManualResetEventSlim(false);
        using var writer = new CompletionTrackingWriter(sink, completed, completeInner: false);
        using var reloadableExecutor = CreateReloadableExecutor(binding);
        var pipeline = new ResourcePipeline(
            binding.Input,
            source => ExecuteBinding(binding, reloadableExecutor, source, cancellationToken),
            writer,
            _observations,
            ResourceOutputEnricher.EnrichAfterFilter);

        using var subscription = pipeline.Start(cancellationToken);
        completed.Wait(cancellationToken);

        if (writer.Error is not null)
        {
            ExceptionDispatchInfo.Capture(writer.Error).Throw();
        }
    }
}