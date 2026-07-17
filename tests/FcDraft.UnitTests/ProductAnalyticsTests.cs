using System.Diagnostics.Metrics;
using FcDraft.Application.Common.Interfaces;
using FcDraft.Application.Features.Auth;
using FcDraft.Infrastructure.Observability;
using Xunit;

namespace FcDraft.UnitTests;

/// <summary>
/// PR-23 (PRD §15): the vendor-neutral product-analytics instruments and their privacy rule —
/// only low-cardinality facts (format/outcome/action) ever reach a tag; no ids, emails, or content.
/// </summary>
public sealed class ProductAnalyticsTests
{
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

    private sealed record Measurement(string Instrument, double Value, Dictionary<string, object?> Tags);

    private static (DraftRoomAnalytics Analytics, List<Measurement> Measurements, IDisposable Cleanup) Listen()
    {
        var meterFactory = new TestMeterFactory();
        var analytics = new DraftRoomAnalytics(meterFactory);
        var measurements = new List<Measurement>();

        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == DraftRoomMetrics.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, ToDictionary(tags))));
        listener.Start();

        return (analytics, measurements, new Disposer(() =>
        {
            listener.Dispose();
            meterFactory.Dispose();
        }));
    }

    private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            dictionary[tag.Key] = tag.Value;
        }

        return dictionary;
    }

    private sealed class Disposer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    [Fact]
    public void Every_15_event_publishes_its_instrument_with_low_cardinality_tags_only()
    {
        var (analytics, measurements, cleanup) = Listen();
        using (cleanup)
        {
            analytics.UserInvited();
            analytics.UserActivated();
            analytics.DraftCreated("1v1");
            analytics.DraftStarted("2v2");
            analytics.DraftEnded("2v2", "completed");
            analytics.FirstPick("1v1", 312.5);
            analytics.PickAccepted("2v2", auto: true, turnSeconds: 120);
            analytics.DraftIntervention("pause", byAdmin: true);
            analytics.HubJoined(reconnect: true);
            analytics.EmailDelivery("retry");
        }

        Assert.Contains(measurements, m => m.Instrument == "draftroom.users.invited" && m.Value == 1 && m.Tags.Count == 0);
        Assert.Contains(measurements, m => m.Instrument == "draftroom.users.activated" && m.Value == 1 && m.Tags.Count == 0);
        Assert.Contains(measurements, m => m.Instrument == "draftroom.drafts.created" && Equals(m.Tags["format"], "1v1"));
        Assert.Contains(measurements, m => m.Instrument == "draftroom.drafts.started" && Equals(m.Tags["format"], "2v2"));
        Assert.Contains(measurements, m => m.Instrument == "draftroom.drafts.ended"
            && Equals(m.Tags["format"], "2v2") && Equals(m.Tags["outcome"], "completed"));
        Assert.Contains(measurements, m => m.Instrument == "draftroom.drafts.time_to_first_pick"
            && m.Value == 312.5 && Equals(m.Tags["format"], "1v1"));
        Assert.Contains(measurements, m => m.Instrument == "draftroom.picks.accepted"
            && Equals(m.Tags["format"], "2v2") && Equals(m.Tags["auto"], true));
        Assert.Contains(measurements, m => m.Instrument == "draftroom.picks.turn_duration"
            && m.Value == 120 && Equals(m.Tags["auto"], true));
        Assert.Contains(measurements, m => m.Instrument == "draftroom.drafts.interventions"
            && Equals(m.Tags["action"], "pause") && Equals(m.Tags["actor"], "admin"));
        Assert.Contains(measurements, m => m.Instrument == "draftroom.hub.joins" && Equals(m.Tags["reconnect"], true));
        Assert.Contains(measurements, m => m.Instrument == "draftroom.email.delivery" && Equals(m.Tags["outcome"], "retry"));

        // §15 privacy proof at the seam: the only tag keys any instrument can carry are the
        // whitelisted low-cardinality facts — nothing shaped like an id, email, or content.
        var allowedTagKeys = new HashSet<string> { "format", "outcome", "auto", "action", "actor", "reconnect" };
        Assert.All(measurements, m => Assert.All(m.Tags.Keys, key => Assert.Contains(key, allowedTagKeys)));
    }

    [Fact]
    public void Pick_without_a_turn_clock_records_no_duration_sample()
    {
        var (analytics, measurements, cleanup) = Listen();
        using (cleanup)
        {
            analytics.PickAccepted("1v1", auto: false, turnSeconds: null);
        }

        Assert.Contains(measurements, m => m.Instrument == "draftroom.picks.accepted");
        Assert.DoesNotContain(measurements, m => m.Instrument == "draftroom.picks.turn_duration");
    }

    private sealed class RecordingAnalytics : IProductAnalytics
    {
        public int Activated;
        public List<(string Format, bool Auto, double? TurnSeconds)> Picks { get; } = [];
        public List<(string Format, string Outcome)> Ended { get; } = [];
        public List<(string Format, double Seconds)> FirstPicks { get; } = [];

        public void UserInvited() { }
        public void UserActivated() => Activated++;
        public void DraftCreated(string format) { }
        public void DraftStarted(string format) { }
        public void DraftEnded(string format, string outcome) => Ended.Add((format, outcome));
        public void FirstPick(string format, double secondsFromCreation) => FirstPicks.Add((format, secondsFromCreation));
        public void PickAccepted(string format, bool auto, double? turnSeconds) => Picks.Add((format, auto, turnSeconds));
        public void DraftIntervention(string action, bool byAdmin) { }
        public void HubJoined(bool reconnect) { }
        public void EmailDelivery(string outcome) { }
    }

    [Fact]
    public async Task Forced_first_password_change_records_activation_but_a_routine_change_does_not()
    {
        var sender = new RecordingInvitationEmailSender();
        var identity = new FcDraft.Infrastructure.Auth.InMemoryIdentityService(
            new FcDraft.Infrastructure.Email.DirectEmailQueue(
                sender,
                new RecordingPasswordResetEmailSender(),
                new RecordingDraftEmailSender(),
                new RecordingAnnouncementEmailSender(),
                new FcDraft.Infrastructure.Email.InMemoryEmailOutbox(TimeProvider.System),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FcDraft.Infrastructure.Email.DirectEmailQueue>.Instance),
            new FakePasswordHasher());
        var analytics = new RecordingAnalytics();
        var handler = new ChangePasswordCommandHandler(
            identity, new FakeTokenService(), new RecordingSecurityAuditService(), analytics);

        // A freshly invited account must change its password: that change IS the activation.
        var invited = await identity.CreateUserAsync(
            "Analytics Probe", "probe@draftroom.test", FcDraft.Domain.Entities.UserRole.Player, default);
        Assert.True(invited.MustChangePassword);
        var otp = sender.PasswordFor("probe@draftroom.test");
        await handler.Handle(new ChangePasswordCommand("probe@draftroom.test", otp, "Fresh@2026Pass", "Fresh@2026Pass"), default);
        Assert.Equal(1, analytics.Activated);

        // A later routine change from Profile is NOT another activation.
        await handler.Handle(new ChangePasswordCommand("probe@draftroom.test", "Fresh@2026Pass", "Later@2026Pass", "Later@2026Pass"), default);
        Assert.Equal(1, analytics.Activated);
    }

    [Fact]
    public void Null_analytics_sink_is_inert()
    {
        // The optional-parameter default used when handlers are constructed directly in tests.
        var sink = NullProductAnalytics.Instance;
        sink.UserInvited();
        sink.DraftEnded("1v1", "cancelled");
        sink.PickAccepted("2v2", auto: false, turnSeconds: 1.5); // must simply not throw
    }
}
