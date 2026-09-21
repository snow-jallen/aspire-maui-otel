using System.Diagnostics;
using PublicOtel.ApiService.Actors;
using PublicOtel.ApiService.Telemetry;

namespace PublicOtel.ClientTests.Actors;

/// <summary>
/// TraceEnvelope is what an endpoint uses to put the caller's trace context inside an actor
/// message. Nothing propagates context across a mailbox on its own, so if this returns the
/// wrong thing the waterfall breaks and every actor span becomes a root.
/// </summary>
public class TraceEnvelopeTests
{
	/// <summary>
	/// Activity.Current is null unless something is listening, so a test that wants a real
	/// activity has to register a listener that samples.
	/// </summary>
	private static ActivityListener ListenTo(string sourceName)
	{
		var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == sourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
		};

		ActivitySource.AddActivityListener(listener);
		return listener;
	}

	[Fact]
	public void Current_returns_the_ambient_activity_context()
	{
		using var listener = ListenTo(ApiTelemetry.ActivitySourceName);
		using var activity = ApiTelemetry.Source.StartActivity("test");

		activity.ShouldNotBeNull();

		var captured = TraceEnvelope.Current();

		captured.TraceId.ShouldBe(activity.TraceId);
		captured.SpanId.ShouldBe(activity.SpanId);
	}

	[Fact]
	public void Current_returns_default_when_nothing_is_listening()
	{
		// No listener registered, so StartActivity returns null and Activity.Current stays
		// null. This must not throw: sampling decisions are not the caller's problem.
		Activity.Current.ShouldBeNull();

		TraceEnvelope.Current().ShouldBe(default(ActivityContext));
	}

	[Fact]
	public void A_child_activity_started_from_a_captured_context_has_that_context_as_parent()
	{
		using var listener = ListenTo(ApiTelemetry.ActivitySourceName);

		ActivityContext captured;
		using (var caller = ApiTelemetry.Source.StartActivity("caller"))
		{
			captured = TraceEnvelope.Current();
		}

		// This is exactly what the actor does on the far side of the mailbox.
		using var child = ApiTelemetry.Source.StartActivity(
			"actor", ActivityKind.Consumer, parentContext: captured);

		child.ShouldNotBeNull();
		child.ParentSpanId.ShouldBe(captured.SpanId);
		child.TraceId.ShouldBe(captured.TraceId);
	}
}
