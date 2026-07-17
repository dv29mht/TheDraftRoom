using System.Diagnostics.Metrics;
using FcDraft.Application.Common.Behaviors;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Infrastructure.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>PR-22: the MediatR logging/metrics behavior and the vendor-neutral metric instruments.</summary>
public sealed class ObservabilityTests
{
    private sealed record PingRequest;

    private sealed class FixedCorrelation(string? id) : ICorrelationIdAccessor
    {
        public string? CorrelationId => id;
    }

    private sealed class RecordingMetrics : IOperationalMetrics
    {
        public List<(string Request, double ElapsedMs, bool Succeeded)> Requests { get; } = [];
        public List<string> UnhandledErrors { get; } = [];

        public void RecordRequest(string requestName, double elapsedMs, bool succeeded) =>
            Requests.Add((requestName, elapsedMs, succeeded));

        public void RecordUnhandledError(string source) => UnhandledErrors.Add(source);
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose() => _meters.ForEach(meter => meter.Dispose());
    }

    [Fact]
    public async Task Logging_behavior_records_a_successful_request()
    {
        var metrics = new RecordingMetrics();
        var behavior = new RequestLoggingBehavior<PingRequest, string>(
            NullLogger<RequestLoggingBehavior<PingRequest, string>>.Instance,
            new FixedCorrelation("corr-1234"),
            metrics);

        var response = await behavior.Handle(new PingRequest(), () => Task.FromResult("pong"), default);

        Assert.Equal("pong", response);
        var sample = Assert.Single(metrics.Requests);
        Assert.Equal("PingRequest", sample.Request);
        Assert.True(sample.Succeeded);
        Assert.True(sample.ElapsedMs >= 0);
    }

    [Fact]
    public async Task Logging_behavior_records_a_failure_and_rethrows()
    {
        var metrics = new RecordingMetrics();
        var behavior = new RequestLoggingBehavior<PingRequest, string>(
            NullLogger<RequestLoggingBehavior<PingRequest, string>>.Instance,
            new FixedCorrelation(null),
            metrics);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new PingRequest(), () => throw new InvalidOperationException("boom"), default));

        var sample = Assert.Single(metrics.Requests);
        Assert.False(sample.Succeeded);
    }

    [Fact]
    public void Draft_room_metrics_publish_duration_and_failure_instruments()
    {
        using var meterFactory = new TestMeterFactory();
        var metrics = new DraftRoomMetrics(meterFactory);

        var durations = new List<double>();
        long failures = 0;
        long unhandled = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == DraftRoomMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => durations.Add(value));
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "draftroom.request.failures")
            {
                failures += value;
            }
            if (instrument.Name == "draftroom.errors.unhandled")
            {
                unhandled += value;
            }
        });
        listener.Start();

        metrics.RecordRequest("AcceptPickCommand", 42.5, succeeded: true);
        metrics.RecordRequest("AcceptPickCommand", 7.0, succeeded: false);
        metrics.RecordUnhandledError("/api/drafts");

        Assert.Equal([42.5, 7.0], durations);
        Assert.Equal(1, failures);
        Assert.Equal(1, unhandled);
    }

    [Fact]
    public void Correlation_accessor_flows_within_and_isolates_between_async_contexts()
    {
        var accessor = new CorrelationIdAccessor();
        accessor.Set("outer-correlation-id");

        Assert.Equal("outer-correlation-id", accessor.CorrelationId);

        // A sibling execution context (Task.Run without inheriting the set value beforehand)
        // must not leak ids INTO the outer flow when it sets its own.
        Task.Run(() => accessor.Set("sibling-correlation-id")).GetAwaiter().GetResult();
        Assert.Equal("outer-correlation-id", accessor.CorrelationId);
    }
}
