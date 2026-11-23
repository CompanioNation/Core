using Microsoft.Data.SqlClient;
using System.Data;

namespace CompanioNationAPI
{
    /// <summary>One queued outbound email, already fully rendered and ready to send.</summary>
    public sealed class QueuedEmail
    {
        public long EmailId { get; set; }
        public string Recipient { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BodyText { get; set; } = string.Empty;
        public string? BodyHtml { get; set; }
        public string? Headers { get; set; }

        /// <summary>Attempts already completed before this claimed one.</summary>
        public int AttemptCount { get; set; }
        public DateTime ClaimUntil { get; set; }
    }

    /// <summary>
    /// The durable email queue. Four operations, one per step of an email's life:
    /// enqueue it, claim the next due one, record the attempt's outcome, and purge old
    /// rows. Everything else about email (rendering, opt-out, provider) lives elsewhere.
    /// </summary>
    public partial class Database
    {
        /// <summary>
        /// Adds one fully rendered email to the queue. This is the only database work a
        /// request thread does for an email; the daemon does the sending. The new row id is
        /// deliberately not returned — no caller uses it.
        /// </summary>
        public async Task EnqueueEmailAsync(string recipient, string subject, string bodyText, string? bodyHtml, string? headersJson)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("cn_email_enqueue", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@recipient", recipient.Trim());
                    cmd.Parameters.AddWithValue("@subject", subject);
                    cmd.Parameters.AddWithValue("@body_text", bodyText);
                    cmd.Parameters.AddWithValue("@body_html", (object?)bodyHtml ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@headers", (object?)headersJson ?? DBNull.Value);

                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Claims the next due email, or returns null when the queue is idle. The claim
        /// reserves the row for <paramref name="leaseSeconds"/>; if this process dies
        /// mid-send the row becomes due again on its own once that window passes.
        /// </summary>
        public async Task<QueuedEmail?> ClaimEmailAsync(int leaseSeconds, CancellationToken cancellationToken = default)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(cancellationToken);
                using (var cmd = new SqlCommand("cn_email_claim", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@lease_seconds", leaseSeconds);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        if (!await reader.ReadAsync(cancellationToken))
                        {
                            return null;
                        }

                        return new QueuedEmail
                        {
                            EmailId = reader.GetInt64(reader.GetOrdinal("email_id")),
                            Recipient = reader.GetString(reader.GetOrdinal("recipient")),
                            Subject = reader.GetString(reader.GetOrdinal("subject")),
                            BodyText = reader.GetString(reader.GetOrdinal("body_text")),
                            BodyHtml = reader.IsDBNull(reader.GetOrdinal("body_html")) ? null : reader.GetString(reader.GetOrdinal("body_html")),
                            Headers = reader.IsDBNull(reader.GetOrdinal("headers")) ? null : reader.GetString(reader.GetOrdinal("headers")),
                            AttemptCount = reader.GetInt32(reader.GetOrdinal("attempt_count")),
                            ClaimUntil = reader.GetDateTime(reader.GetOrdinal("next_attempt_at"))
                        };
                    }
                }
            }
        }

        /// <summary>
        /// Records an outcome only if this claim is still current. Returns null for a
        /// stale claim, true for dead-letter, and false for a send or scheduled retry.
        /// </summary>
        public async Task<bool?> CompleteEmailAsync(long emailId, DateTime claimUntil, bool sent, string? error, CancellationToken cancellationToken = default)
        {
            if (error is not null && error.Length > 512)
            {
                error = error.Substring(0, 512);
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(cancellationToken);
                using (var cmd = new SqlCommand("cn_email_complete", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@email_id", emailId);
                    cmd.Parameters.Add("@claim_until", SqlDbType.DateTime).Value = claimUntil;
                    cmd.Parameters.AddWithValue("@sent", sent);
                    cmd.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);

                    object? result = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (result is null or DBNull)
                        throw new InvalidOperationException("Email completion returned no result.");
                    int status = Convert.ToInt32(result);
                    return status switch { -1 => null, 0 => false, 1 => true, _ => throw new InvalidOperationException($"Unexpected email completion result: {status}.") };
                }
            }
        }

        /// <summary>
        /// Retention: deletes resolved queue rows older than the given windows. Pending
        /// rows are never touched, so a purge can never lose mail. Returns the row count.
        /// </summary>
        public async Task<int> PurgeEmailQueueAsync(int sentRetentionDays, int deadLetterRetentionDays, CancellationToken cancellationToken = default)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync(cancellationToken);
                using (var cmd = new SqlCommand("cn_email_purge", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@sent_days", sentRetentionDays);
                    cmd.Parameters.AddWithValue("@dead_letter_days", deadLetterRetentionDays);

                    object? result = await cmd.ExecuteScalarAsync(cancellationToken);
                    return result is null or DBNull ? 0 : Convert.ToInt32(result);
                }
            }
        }
    }
}
