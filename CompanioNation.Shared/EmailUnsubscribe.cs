using System;

namespace CompanioNation.Shared
{
    /// <summary>
    /// Stateless helpers for email unsubscription links. A single URL-safe token (minted
    /// with <see cref="SecureUrlPayload"/>) carries the recipient address through the
    /// browser so the unsubscribe endpoint/deep link can opt the user out without a login
    /// token. Tokens are domain-separated by <see cref="Purpose"/> and expire after
    /// <see cref="TokenLifetime"/>.
    /// </summary>
    public static class EmailUnsubscribe
    {
        /// <summary>Domain-separation label for unsubscribe tokens.</summary>
        public const string Purpose = "EMAIL_UNSUBSCRIBE|v1";

        /// <summary>How long an emailed unsubscribe link remains valid.</summary>
        public static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);

        /// <summary>
        /// Mints an opaque, authenticated unsubscribe token for a recipient address.
        /// Returns null when the address is invalid so callers skip the header/footer.
        /// </summary>
        public static string? CreateToken(string email, string base64Secret)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(base64Secret))
                return null;

            return SecureUrlPayload.Create(email, Purpose, base64Secret);
        }

        /// <summary>
        /// Opens and validates an unsubscribe token, returning the recipient address when
        /// the token is genuine and unexpired.
        /// </summary>
        public static bool TryOpenToken(string? token, string base64Secret, out string? email)
            => SecureUrlPayload.TryOpen(
                token, Purpose, base64Secret, out email, lifetime: TokenLifetime);

        /// <summary>The one-click (HTTP POST) unsubscribe URL for Gmail/other clients.</summary>
        public static string OneClickUrl(string baseUrl, string token)
            => $"{baseUrl.TrimEnd('/')}/api/unsubscribe/{Uri.EscapeDataString(token)}";

        /// <summary>The human-visible unsubscribe deep link rendered in the email footer.</summary>
        public static string FooterUrl(string baseUrl, string token)
            => $"{baseUrl.TrimEnd('/')}/Unsubscribe?token={Uri.EscapeDataString(token)}";
    }
}
