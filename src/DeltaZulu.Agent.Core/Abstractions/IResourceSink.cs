using DeltaZulu.Agent.Application.Abstractions;
using DeltaZulu.Agent.Core.Events;

namespace DeltaZulu.Agent.Core.Abstractions;

[Obsolete("Use IOutputWriter from DeltaZulu.Agent.Application.Abstractions instead.")]
public interface IResourceSink : IOutputWriter
{
}
