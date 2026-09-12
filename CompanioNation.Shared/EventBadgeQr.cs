using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompanioNation.Shared
{
    /// <summary>
    /// Stateless, HMAC-SHA256-signed QR payload for badge acquisition and propagation.
    /// Shares the secret with the LINK feature but namespaces the signed string with a
    /// "BADGE|" prefix, so a LINK token can never validate as a badge token (or vice versa).
    /// The payload is base64url(JSON) so it is safe to embed in a URL query string.
    /// </summary>
    public static class EventBadgeQr
    {
        /// <summary>How long a freshly minted code stays valid.</summary>
        public const int ValiditySeconds = 180;

        // Mixed into the signed material so signatures are domain-separated from the
        // LINK feature even though both use the same secret.
        private const string SignatureNamespace = "BADGE";

        public sealed record Payload
        {
            [JsonPropertyName("bid")] public int BadgeId { get; init; }
            [JsonPropertyName("uid")] public int IssuerUserId { get; init; }
            [JsonPropertyName("ts")] public long Timestamp { get; init; }
            [JsonPropertyName("sig")] public string Signature { get; init; } = string.Empty;
        }

        /// <summary>Creates a signed code for a badge, stamped with the current UTC time.</summary>
        public static string Create(int badgeId, int issuerUserId, string base64Secret)
            => Create(badgeId, issuerUserId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), base64Secret);

        /// <summary>Creates a signed code for a badge at an explicit unix timestamp (testable).</summary>
        public static string Create(int badgeId, int issuerUserId, long unixSeconds, string base64Secret)
        {
            var payload = new Payload
            {
                BadgeId = badgeId,
                IssuerUserId = issuerUserId,
                Timestamp = unixSeconds,
                Signature = ComputeSignature(badgeId, issuerUserId, unixSeconds, base64Secret)
            };

            string json = JsonSerializer.Serialize(payload);
            return Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Validates a signed code and, on success, yields the badge id and issuer user id.
        /// Returns false and sets <paramref name="errorCode"/> on malformed, tampered, or
        /// expired codes (<see cref="ErrorCodes.BadgeQrInvalid"/> / <see cref="ErrorCodes.BadgeQrExpired"/>).
        /// </summary>
        public static bool TryDecode(string? code, string base64Secret, DateTime utcNow,
            out int badgeId, out int issuerUserId, out int errorCode)
        {
            badgeId = 0;
            issuerUserId = 0;
            errorCode = ErrorCodes.BadgeQrInvalid;

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(base64Secret))
                return false;

            Payload? payload;
            try
            {
                string json = Encoding.UTF8.GetString(Base64UrlDecode(code));
                payload = JsonSerializer.Deserialize<Payload>(json);
            }
            catch
            {
                return false;
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.Signature))
                return false;

            // Expiry is checked before the signature so an old code reads as "expired"
            // rather than "invalid" — the friendlier message.
            long now = new DateTimeOffset(utcNow).ToUnixTimeSeconds();
            if (Math.Abs(now - payload.Timestamp) > ValiditySeconds)
            {
                errorCode = ErrorCodes.BadgeQrExpired;
                return false;
            }

            string expected = ComputeSignature(payload.BadgeId, payload.IssuerUserId, payload.Timestamp, base64Secret);
            if (!FixedTimeEquals(payload.Signature, expected))
                return false;

            badgeId = payload.BadgeId;
            issuerUserId = payload.IssuerUserId;
            errorCode = ErrorCodes.Success;
            return true;
        }

        private static string ComputeSignature(int badgeId, int issuerUserId, long unixSeconds, string base64Secret)
        {
            string dataToSign = $"{SignatureNamespace}|{badgeId}|{issuerUserId}|{unixSeconds}";
            byte[] keyBytes = Convert.FromBase64String(base64Secret);
            using var hmac = new HMACSHA256(keyBytes);
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            byte[] ba = Encoding.UTF8.GetBytes(a);
            byte[] bb = Encoding.UTF8.GetBytes(b);
            return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
        }

        private static string Base64UrlEncode(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
