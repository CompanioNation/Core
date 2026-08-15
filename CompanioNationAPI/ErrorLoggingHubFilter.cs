using Microsoft.AspNetCore.SignalR;

namespace CompanioNationAPI;

/// <summary>
/// Logs any exception that escapes a hub method so the real server-side error is
/// captured. Hub methods that already catch and return a failure wrapper never
/// reach this filter, so it only fires for genuinely unhandled failures.
/// </summary>
public sealed class ErrorLoggingHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
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
