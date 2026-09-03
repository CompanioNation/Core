using System.Text;

namespace CompanioNation.Shared
{
    /// <summary>
    /// Renders exceptions into the diagnostic text used by error reports. This is the ONE
    /// implementation of exception-chain formatting, shared by the browser client
    /// (CompanioNationPWA) and the server error pipeline (CompanioNationServices), so the
    /// two sides can never drift on how a chain of inner exceptions is written out.
    /// </summary>
    public static class ExceptionFormatter
    {
        /// <summary>
        /// Appends <paramref name="exception"/> and its ENTIRE inner-exception chain to
        /// <paramref name="sb"/> as labeled text, outer exception first. Each deeper level
        /// is introduced with "-- Inner Exception (n) --" because the real root cause is
        /// frequently wrapped several layers deep (e.g. by hub dispatch or an SDK).
        /// </summary>
        public static void AppendChain(StringBuilder sb, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(sb);
            ArgumentNullException.ThrowIfNull(exception);

            int depth = 0;
            for (Exception? current = exception; current is not null; current = current.InnerException, depth++)
            {
                if (depth > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"-- Inner Exception ({depth}) --");
                }

                sb.AppendLine($"ExceptionType: {current.GetType().FullName}");
                sb.AppendLine($"HResult: {current.HResult} (0x{current.HResult:X8})");
                sb.AppendLine($"Message: {current.Message}");
                if (!string.IsNullOrWhiteSpace(current.StackTrace))
                {
                    sb.AppendLine($"StackTrace: {current.StackTrace}");
                }
            }
        }
    }
}
