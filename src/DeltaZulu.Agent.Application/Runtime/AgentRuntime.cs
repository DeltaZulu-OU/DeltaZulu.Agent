using System.Runtime.ExceptionServices;
using DeltaZulu.Agent.Application.Abstractions;
using DeltaZulu.Agent.Application.Pipelines;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Profiles;

namespace DeltaZulu.Agent.Application.Runtime;

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
        if (_bindings.Count == 1)
        {
            return RunSingle(_bindings[0], cancellationToken);
        }

        return RunMultiple(cancellationToken);
    }

    private AgentRuntimeResult RunSingle(ProfileBinding binding, CancellationToken cancellationToken)
    {
        using var completed = new ManualResetEventSlim(false);
        using var writer = new CompletionTrackingWriter(_sink, completed);
        var pipeline = new ResourcePipeline(
            binding.Input,
            source => binding.Executor.Execute(source, binding.Profile, cancellationToken),
            writer,
            _observations);

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
            return HandleSingleBindingFailure(binding, ex);
        }

        if (writer.Error is not null)
        {
            return HandleSingleBindingFailure(binding, writer.Error);
        }

        return new AgentRuntimeResult(true);
    }

    private AgentRuntimeResult HandleSingleBindingFailure(ProfileBinding binding, Exception exception)
    {
        if (!binding.Profile.Mandatory)
        {
            _warn?.Invoke($"profile '{binding.Profile.Id}' failed and will be skipped because mandatory is false: {exception.Message}");
            return new AgentRuntimeResult(true);
        }

        return new AgentRuntimeResult(false, exception);
    }

    private AgentRuntimeResult RunMultiple(CancellationToken cancellationToken)
    {
        using var mux = new ChannelOutputMultiplexer(_sink);
        var tasks = _bindings
            .Select(binding => Task.Run(() => RunBindingSafely(binding, mux, cancellationToken), cancellationToken))
            .ToArray();

        try
        {
            Task.WaitAll(tasks, cancellationToken);
        }
        catch (AggregateException ex)
        {
            mux.Complete();
            return new AgentRuntimeResult(false, ex.InnerExceptions.Count == 1
                ? ex.InnerExceptions[0]
                : ex.Flatten());
        }

        mux.Complete();

        return mux.Error is not null
            ? new AgentRuntimeResult(false, mux.Error)
            : new AgentRuntimeResult(true);
    }

    private void RunBindingSafely(ProfileBinding binding, IOutputWriter sink, CancellationToken cancellationToken)
    {
        try
        {
            RunBinding(binding, sink, cancellationToken);
        }
        catch (Exception ex) when (!binding.Profile.Mandatory)
        {
            _warn?.Invoke($"profile '{binding.Profile.Id}' failed and will be skipped because mandatory is false: {ex.Message}");
        }
    }

    private void RunBinding(ProfileBinding binding, IOutputWriter sink, CancellationToken cancellationToken)
    {
        using var completed = new ManualResetEventSlim(false);
        using var writer = new CompletionTrackingWriter(sink, completed, completeInner: false);
        var pipeline = new ResourcePipeline(
            binding.Input,
            source => binding.Executor.Execute(source, binding.Profile, cancellationToken),
            writer,
            _observations);

        using var subscription = pipeline.Start(cancellationToken);
        completed.Wait(cancellationToken);

        if (writer.Error is not null)
        {
            ExceptionDispatchInfo.Capture(writer.Error).Throw();
        }
    }
}
