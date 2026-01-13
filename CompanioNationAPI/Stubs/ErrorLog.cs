namespace CompanioNationAPI;

/// <summary>
/// Delegates logging to an injectable implementation. The default logger writes to the console
/// </summary>
public static class ErrorLog
{
    public static IErrorLogger Implementation { get; set; } = new DefaultErrorLogger();

    public static Task LogError(DateTime timestamp, string message, string version) => Implementation.LogError(timestamp, message, version);

    public static Task LogErrorException(Exception ex, string context = "") => Implementation.LogErrorException(ex, context);

    public static Task LogErrorMessage(string message) => Implementation.LogErrorMessage(message);

    public static Task LogInfo(string message) => Implementation.LogInfo(message);
}

public interface IErrorLogger
{
    Task LogError(DateTime timestamp, string message, string version);
    Task LogErrorException(Exception ex, string context = "");
    Task LogErrorMessage(string message);
    Task LogInfo(string message);
}

internal sealed class DefaultErrorLogger : IErrorLogger
{
    public Task LogError(DateTime timestamp, string message, string version)
    {
        Console.WriteLine($"[{timestamp:u}] v{version} ERROR: {message}");
        return Task.CompletedTask;
    }

    public Task LogErrorException(Exception ex, string context = "")
    {
        Console.WriteLine($"EXCEPTION ({context}): {ex}");
        return Task.CompletedTask;
    }

    public Task LogErrorMessage(string message)
    {
        Console.WriteLine($"ERROR: {message}");
        return Task.CompletedTask;
    }

    public Task LogInfo(string message)
    {
        Console.WriteLine($"INFO: {message}");
        return Task.CompletedTask;
    }
}
