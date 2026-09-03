using System.Globalization;

namespace CompanioNation.Shared
{
    /// <summary>
    /// Version comparison for client/hub contract enforcement. Version strings look like
    /// "26.9.2.4" and MUST be compared numerically by segment — never by ordinal string
    /// comparison, because "26.9.2.10" would otherwise sort as older than "26.9.2.4".
    /// </summary>
    public static class ClientVersion
    {
        /// <summary>
        /// True when <paramref name="actual"/> is strictly older than
        /// <paramref name="minimum"/>. Missing, whitespace, or unparsable versions are
        /// treated as the oldest possible client, so they always require an upgrade
        /// (fail-closed rather than silently allowing an unknown contract).
        /// </summary>
        public static bool IsOlderThan(string? actual, string? minimum)
        {
            if (!TryParse(actual, out int[]? actualParts))
                return true;

            if (!TryParse(minimum, out int[]? minimumParts))
                return false;

            int length = Math.Max(actualParts!.Length, minimumParts!.Length);
            for (int i = 0; i < length; i++)
            {
                int a = i < actualParts.Length ? actualParts[i] : 0;
                int m = i < minimumParts.Length ? minimumParts[i] : 0;

                if (a != m)
                    return a < m;
            }

            return false; // Equal.
        }

        private static bool TryParse(string? version, out int[]? parts)
        {
            parts = null;

            if (string.IsNullOrWhiteSpace(version))
                return false;

            string[] segments = version.Split('.');
            int[] parsed = new int[segments.Length];

            for (int i = 0; i < segments.Length; i++)
            {
                // Trim each segment so trailing build metadata / whitespace never breaks parsing.
                if (!int.TryParse(segments[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    return false;

                parsed[i] = value;
            }

            parts = parsed;
            return true;
        }
    }
}
