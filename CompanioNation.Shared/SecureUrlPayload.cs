using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CompanioNation.Shared
{
    /// <summary>
    /// Stateless, authenticated-encryption helper for carrying a small payload (typically a
    /// single value such as an email address) through an untrusted browser round-trip — for
    /// example a server endpoint that must hand data forward via a 302 redirect into a
    /// Blazor WebAssembly route.
    ///
    /// The payload is AES-GCM encrypted AND authenticated with a key derived (HKDF-SHA256)
    /// from a single shared base64 secret. Because the key is derived per <c>purpose</c>, any
    /// number of features can share ONE secret without being able to forge each other's
    /// payloads — the same domain-separation idea used by <see cref="EventBadgeQr"/>.
    ///
    /// Unlike <see cref="EventBadgeQr"/> (which only signs, because badge ids are not secret),
    /// this helper also ENCRYPTS, so the value cannot be read from browser history, proxy or
    /// server logs, or a Referer header. Output is base64url and safe to embed in a URL query
    /// string.
    ///
    /// No server-side state is required: any instance holding the shared secret can open a
    /// payload minted by any other instance, so the result is safe behind a load balancer and
    /// correct the moment the service scales out.
    /// </summary>
    public static class SecureUrlPayload
    {
        /// <summary>
        /// Environment variable holding the base64 shared secret. Shared by all URL-payload
        /// features (LINK, event badges, the Apple login handoff, ...); the per-purpose key
        /// derivation keeps them cryptographically independent.
        /// </summary>
        public const string SecretEnvironmentVariable = "COMPANIONATION_LINK_SECRET";

        private const byte FormatVersion = 1;
        private const int NonceSize = 12;   // AES-GCM standard nonce length
        private const int TagSize = 16;     // AES-GCM standard tag length
        private const int KeySize = 32;     // AES-256
        private const int TimestampSize = 8; // big-endian Int64 unix seconds

        /// <summary>Default validity window enforced when opening a payload.</summary>
        public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Encrypts and authenticates <paramref name="payload"/>, returning a base64url blob
        /// safe to place in a URL query string.
        /// </summary>
        /// <param name="payload">The value to protect (e.g. an email address).</param>
        /// <param name="purpose">
        /// Domain-separation label (e.g. <c>"APPLE_LOGIN_HANDOFF|v1"</c>). Mixed into the key
        /// derivation and the authentication tag, so a payload minted for one purpose can
        /// never be opened as another.
        /// </param>
        /// <param name="base64Secret">Base64 shared secret (see <see cref="SecretEnvironmentVariable"/>).</param>
        /// <param name="associatedData">
        /// Optional extra value that is authenticated but not encrypted (e.g. a one-time code).
        /// A blob is only accepted alongside the exact value it was minted for.
        /// </param>
        public static string Create(
            string payload,
            string purpose,
            string base64Secret,
            string? associatedData = null)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (string.IsNullOrWhiteSpace(purpose)) throw new ArgumentException("Purpose is required.", nameof(purpose));
            if (string.IsNullOrWhiteSpace(base64Secret)) throw new ArgumentException("Secret is required.", nameof(base64Secret));

            // Envelope plaintext: [8-byte big-endian unix timestamp | UTF-8 payload].
            // Deliberately hand-packed instead of JSON: no reflection-based serialization,
            // so nothing here can trip the Blazor WebAssembly trimmer / AOT analyzer.
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
            byte[] plaintext = new byte[TimestampSize + payloadBytes.Length];
            BinaryPrimitives.WriteInt64BigEndian(plaintext, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            payloadBytes.CopyTo(plaintext, TimestampSize);

            byte[] key = DeriveKey(base64Secret, purpose);
            byte[] aad = BuildAssociatedData(purpose, associatedData);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            }

            // Layout: [version | nonce | ciphertext | tag]
            byte[] blob = new byte[1 + NonceSize + ciphertext.Length + TagSize];
            blob[0] = FormatVersion;
            nonce.CopyTo(blob, 1);
            ciphertext.CopyTo(blob, 1 + NonceSize);
            tag.CopyTo(blob, 1 + NonceSize + ciphertext.Length);

            return Base64UrlEncode(blob);
        }

        /// <summary>
        /// Verifies and decrypts a blob produced by <see cref="Create"/>. Returns false (and
        /// sets <paramref name="payload"/> to null) for malformed, tampered, wrong-purpose,
        /// wrong-associated-data, or expired blobs.
        /// </summary>
        public static bool TryOpen(
            string? blob,
            string purpose,
            string base64Secret,
            out string? payload,
            string? associatedData = null,
            TimeSpan? lifetime = null,
            DateTime? utcNow = null)
        {
            payload = null;

            if (string.IsNullOrWhiteSpace(blob)) return false;
            if (string.IsNullOrWhiteSpace(purpose)) return false;
            if (string.IsNullOrWhiteSpace(base64Secret)) return false;

            byte[] raw;
            byte[] key;
            byte[] aad;
            try
            {
                // Decoding and key derivation can BOTH throw (malformed blob; a shared
                // secret that isn't valid base64). Fail closed instead of letting a
                // configuration error bubble into the login path as an exception.
                raw = Base64UrlDecode(blob);
                key = DeriveKey(base64Secret, purpose);
                aad = BuildAssociatedData(purpose, associatedData);
            }
            catch { return false; }

            if (raw.Length < 1 + NonceSize + TagSize) return false;
            if (raw[0] != FormatVersion) return false;

            int cipherLength = raw.Length - 1 - NonceSize - TagSize;
            ReadOnlySpan<byte> nonce = raw.AsSpan(1, NonceSize);
            ReadOnlySpan<byte> ciphertext = raw.AsSpan(1 + NonceSize, cipherLength);
            ReadOnlySpan<byte> tag = raw.AsSpan(1 + NonceSize + cipherLength, TagSize);

            byte[] plaintext = new byte[cipherLength];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            }
            catch
            {
                // CryptographicException = tampered / wrong key / wrong purpose / wrong
                // associated data. Anything else (e.g. a platform without AES-GCM) must
                // still fail closed rather than throw into the caller.
                return false;
            }

            // Envelope plaintext: [8-byte big-endian unix timestamp | UTF-8 payload]
            if (plaintext.Length < TimestampSize) return false;
            long timestamp = BinaryPrimitives.ReadInt64BigEndian(plaintext);
            string text = Encoding.UTF8.GetString(plaintext, TimestampSize, plaintext.Length - TimestampSize);

            DateTime now = utcNow ?? DateTime.UtcNow;
            if (now.Kind != DateTimeKind.Utc) now = DateTime.SpecifyKind(now, DateTimeKind.Utc);

            long maxAgeSeconds = (long)(lifetime ?? DefaultLifetime).TotalSeconds;
            long age = new DateTimeOffset(now).ToUnixTimeSeconds() - timestamp;
            // Reject stale payloads, and payloads stamped unreasonably far in the future.
            if (age > maxAgeSeconds || age < -60) return false;

            payload = text;
            return true;
        }

        private static byte[] DeriveKey(string base64Secret, string purpose)
        {
            byte[] ikm = Convert.FromBase64String(base64Secret);
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, KeySize, salt: null, info: Encoding.UTF8.GetBytes(purpose));
        }

        private static byte[] BuildAssociatedData(string purpose, string? associatedData)
            => Encoding.UTF8.GetBytes(purpose + "|" + (associatedData ?? string.Empty));

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
