namespace CompanioNationAPI;

/// <summary>
/// AsyncLocal-scoped diagnostics for the hub invocation currently being processed.
///
/// The hub filter captures method/connection/client metadata at the boundary, and the
/// token-validation path (Database.GetUserAsync) attaches the resolved account identity
/// once a user is known. Any error logged during that invocation — including deep inside
/// the Database layer — can then be attributed to a method, connection, client, and user
/// without threading context through every call site.
///
/// The login token itself is intentionally NEVER stored here: only its presence, plus the
/// non-secret account identity (UserId/Email) that the token resolves to.
/// </summary>
public static class RequestContext
{
    private static readonly AsyncLocal<State?> Current = new();

    public sealed class State
    {
        public string HubMethod { get; set; } = "(unknown)";
        public string ConnectionId { get; set; } = "(unknown)";
        public string ClientIp { get; set; } = "(unknown)";
        public int? UserId { get; set; }
        public string? Email { get; set; }
    }

    public static State? Value => Current.Value;

    /// <summary>Sets the request state for the remainder of the current async flow.</summary>
    public static IDisposable Capture(State state)
    {
        var previous = Current.Value;
        Current.Value = state;
        return new Restore(previous);
    }

    /// <summary>
    /// Records the resolved account identity on the current request state, if any. Called by
    /// the token-validation path once a user is known. Safe to call when no request context
    /// exists (e.g. a background job) — it is then a no-op.
    /// </summary>
    public static void SetUser(int userId, string? email)
    {
        var state = Current.Value;
        if (state is null)
            return;

        state.UserId = userId;
        // Never lose an email already resolved for this request: some paths recover only
        // the user id from cn_validate_login_token, and a later partial call must not
        // blank the identity an earlier full load established.
        if (!string.IsNullOrWhiteSpace(email))
            state.Email = email;
    }

    public static string Format(State s) =>
        $"Hub={s.HubMethod}, ConnectionId={s.ConnectionId}, IP={s.ClientIp}, " +
        $"UserId={s.UserId?.ToString() ?? "(unknown)"}, Email={s.Email ?? "(unknown)"}";

    private sealed class Restore : IDisposable
    {
        private readonly State? _previous;

        public Restore(State? previous) => _previous = previous;

        public void Dispose() => Current.Value = _previous;
    }
}
