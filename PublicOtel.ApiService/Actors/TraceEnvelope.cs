using System.Diagnostics;

namespace PublicOtel.ApiService.Actors;

/// <summary>
/// Captures the ambient trace context so it can be carried inside an actor message.
/// </summary>
public static class TraceEnvelope
{
    /// <summary>
    /// The current activity's context, or <c>default</c> when there is no current activity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never write this as <c>Activity.Current!.Context</c>. Activity.Current is null whenever
    /// no listener is registered or the trace was not sampled, and a NullReferenceException on
    /// a sampling decision is a genuinely miserable bug to find.
    /// </para>
    /// <para>
    /// A default ActivityContext is harmless downstream: StartActivity treats it as "no
    /// parent" and falls back to the ambient context, which is exactly the behaviour you want.
    /// </para>
    /// </remarks>
    public static ActivityContext Current() => Activity.Current?.Context ?? default;
}
