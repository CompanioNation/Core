using Microsoft.AspNetCore.SignalR;

namespace CompanioNationAPI;

/// <summary>
/// Logs any exception that escapes a hub method so the real server-side error is
/// captured. Hub methods that already catch and return a failure wrapper never
/// reach this filter, so it only fires for genuinely unhandled failures.
///
/// While the invocation runs, method/connection/client metadata is placed in
/// <see cref="RequestContext"/> so that ANY error logged during the invocation (including
/// deep inside the Database layer) is attributed to a method, connection, and client IP —
/// the context that was previously missing from server error logs.
/// </summary>
public sealed class ErrorLoggingHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        using (RequestContext.Capture(BuildState(invocationContext)))
        {
            try
            {
                return await next(invocationContext);
            }
            catch (Exception ex)
            {
                await ErrorLog.LogErrorException(ex, $"Hub method {invocationContext.HubMethodName} failed.");
                throw;
            }
        }
    }

    private static RequestContext.State BuildState(HubInvocationContext ctx)
    {
        var httpContext = ctx.Context.GetHttpContext();

        return new RequestContext.State
        {
            HubMethod = ctx.HubMethodName,
            ConnectionId = ctx.Context.ConnectionId,
            ClientIp = httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "(unknown)",
        };
    }
}
