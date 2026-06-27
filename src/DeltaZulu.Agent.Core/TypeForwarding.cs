using System.Runtime.CompilerServices;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Core.Observability;

[assembly: TypeForwardedTo(typeof(SourceEvent))]
[assembly: TypeForwardedTo(typeof(ResourceMetadata))]
[assembly: TypeForwardedTo(typeof(ResourceOutputRecord))]
[assembly: TypeForwardedTo(typeof(DictionaryCoercion))]
[assembly: TypeForwardedTo(typeof(CollectorObservationMetadata))]
[assembly: TypeForwardedTo(typeof(LogTelemetryKey))]
[assembly: TypeForwardedTo(typeof(PipelineCountsObservation))]
[assembly: TypeForwardedTo(typeof(FilterSummaryObservation))]
[assembly: TypeForwardedTo(typeof(SourceHealthObservation))]
