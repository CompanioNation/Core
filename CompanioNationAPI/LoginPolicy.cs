namespace CompanioNationAPI;

/// <summary>
/// Pure login/account-policy decisions so the security rules are unit-testable
/// without touching the database or a hub. The stored procedures
/// (cn_login, cn_login_failed) enforce the same rules server-side.
/// </summary>
internal static class LoginPolicy
{
    /// <summary>Failed-login count at which an account is temporarily locked.</summary>
    internal const int MaxFailedLogins = 5;

    /// <summary>
    /// Whether an OAuth sign-in may take over an existing account. OAuth sign-in
    /// may only attach to an account that was itself created through OAuth — a
    /// password account can only be claimed by someone who knows the password —
    /// unless the provider verified the email address.
    /// </summary>
    internal static bool OAuthCanTakeOver(bool isOAuthAccount, bool emailVerified)
        => isOAuthAccount || emailVerified;

    /// <summary>Whether the failed-login count has reached the lockout threshold.</summary>
    internal static bool IsLockedOut(int failedLogins)
        => failedLogins >= MaxFailedLogins;
}
