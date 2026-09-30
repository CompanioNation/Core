using System.Collections.Concurrent;
using System.Reflection;
using CompanioNation.Shared;
using Microsoft.AspNetCore.SignalR;

namespace CompanioNationAPI;

/// <summary>
/// Centralized cutover gate for the hub. While <see cref="CutoverState.Active"/> (a
/// staging→production promotion is freezing the server), every hub method that returns
/// a <see cref="ResponseWrapper{T}"/> is short-circuited with
/// <see cref="ErrorCodes.ServiceUnavailable"/> instead of being executed. The client
/// sees that code and shows the branded maintenance overlay — it never runs a write
/// against the old production database that the promotion is about to replace.
/// </summary>
/// <remarks>
/// This is the single hub-level intercept (mirrors <see cref="ErrorLoggingHubFilter"/>)
/// so a new hub method can never forget the check. Only methods returning a
/// <see cref="ResponseWrapper{T}"/> are gated — that type is the only channel that can
/// carry an error code back to the client. Non-wrapper methods are passed through:
/// <c>GetCurrentVersion</c> must keep answering so the reconnect handshake still
/// completes (and so a reconnected client can be told the gate is on), and the log
/// methods are best-effort side channels. A <c>void</c> method cannot report the gate,
/// so gating it would silently drop the call; passing it through lets the client's
/// normal traffic surface the gate via any wrapper-returning call.
/// </remarks>
public sealed class CutoverGateHubFilter : IHubFilter
{
    // Per hub-method factory that builds the typed ResponseWrapper<T>.Fail(...) for that
    // method's return type, or null when the method has no wrapper to carry a code.
    // Hub methods are a fixed set, so this is computed once per method.
    private static readonly ConcurrentDictionary<MethodInfo, Func<int, string, object>?> FailFactories = new();

    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (!CutoverState.Active)
            return next(invocationContext);

        var failFactory = FailFactories.GetOrAdd(invocationContext.HubMethod, BuildFailFactory);
        if (failFactory is null)
            return next(invocationContext);

        // Short-circuit: hand the client a failed wrapper carrying the gate's message.
        // The ResponseWrapper-returning methods all inspect IsSuccess/ErrorCode, so this
        // is indistinguishable from the method itself returning the failure.
        object failure = failFactory(ErrorCodes.ServiceUnavailable, CutoverState.Message);
        return new ValueTask<object?>(failure);
    }

    private static Func<int, string, object>? BuildFailFactory(MethodInfo hubMethod)
    {
        // The dispatcher invokes hub methods through its async executor, so the filter
        // sees the UNWRAPPED return value. Unwrap Task<T> / ValueTask<T> to find the real
        // return type before looking for ResponseWrapper<T>.
        Type? wrapped = hubMethod.ReturnType;
        if (wrapped.IsGenericType)
        {
            Type definition = wrapped.GetGenericTypeDefinition();
            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
                wrapped = wrapped.GetGenericArguments()[0];
        }

        if (wrapped is null ||
            !wrapped.IsGenericType ||
            wrapped.GetGenericTypeDefinition() != typeof(ResponseWrapper<>))
        {
            return null;
        }

        // ResponseWrapper<T>.Fail(int errorCode, string message) — the two-argument
        // overload (there is also a three-argument one carrying data).
        MethodInfo fail = wrapped.GetMethod(nameof(ResponseWrapper<object>.Fail), new[] { typeof(int), typeof(string) })!;

        return (errorCode, message) => fail.Invoke(null, new object[] { errorCode, message })!;
    }
}
