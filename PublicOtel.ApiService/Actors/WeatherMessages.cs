using System.Diagnostics;

namespace PublicOtel.ApiService.Actors;

// These are records, and that is not decoration. After Tell returns, the sender still holds a
// reference to the message it just sent. If the message were mutable, two actors would be
// sharing mutable state across a thread boundary - the exact thing the actor model exists to
// remove. Immutable message, no shared state, no lock.
//
// Every inbound message carries an ActivityContext. Auto-instrumentation propagates trace
// context across an HTTP hop for free, via the W3C traceparent header. Nothing propagates it
// across a mailbox: Tell returns immediately and the actor handles the message later, on
// another thread, with no ambient Activity. Carrying the context explicitly is the whole
// trick, and it is the difference between one waterfall and a pile of orphan spans.

/// <summary>Report a new reading for a station. Sent with <c>Tell</c>; there is no reply.</summary>
public sealed record ReportReading(string Station, int TemperatureC, ActivityContext TraceContext);

/// <summary>Ask a station for its most recent reading.</summary>
public sealed record GetLatestReading(string Station, ActivityContext TraceContext);

/// <summary>
/// A station's current reading. Both the reply to <see cref="GetLatestReading"/> and the
/// payload broadcast to SignalR clients.
/// </summary>
public sealed record StationReading(string Station, int TemperatureC, DateTimeOffset ObservedAt);

/// <summary>Reply when a station has not reported anything yet.</summary>
public sealed record NoReadingYet(string Station);
