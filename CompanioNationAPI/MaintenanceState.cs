namespace CompanioNationAPI;

/// <summary>
/// In-memory, per-process mutual-exclusion flag: "the nightly maintenance job, or a
/// promotion, is writing to this process's database right now".
/// </summary>
/// <remarks>
/// This is the single source of truth for the maintenance/promotion handshake. The nightly
/// job (<see cref="MaintenanceEventService"/>) acquires it while it writes; the promotion
/// script acquires it on both slots (via POST /api/cutover/set-maintenance) while it copies
/// a database. ACCEPTING the claim is atomic, so two contenders can never both win it.
/// Deliberately NOT persisted, exactly like <see cref="CutoverState"/>: a database row or a
/// file would add a second source of truth that can go stale after a crash, whereas an
/// in-memory flag dies with its process and can never strand a slot. That also means the
/// cure for a flag left behind by a crashed holder is simply a restart, never manual SQL.
/// Releasing is NOT ownership-checked (there is no owner id to check, and inventing one
/// would be a second source of truth). Each caller is responsible for releasing only a
/// claim it made itself, in the same process it made it on; the requirement is enforced at
/// the call sites, not here.
/// </remarks>
public static class MaintenanceState
{
    private static int _held;

    /// <summary>True while some component holds the maintenance claim in this process.</summary>
    public static bool Active => Volatile.Read(ref _held) == 1;

    /// <summary>
    /// Atomically claims the flag. Returns true only to the single winner; everyone else
    /// gets false and must not proceed. The winner must call <see cref="Release"/> exactly
    /// once (release is idempotent, so a second call is harmless).
    /// </summary>
    public static bool TryAcquire() => Interlocked.CompareExchange(ref _held, 1, 0) == 0;

    /// <summary>Returns the claim. Idempotent and safe to call even when not held.</summary>
    public static void Release() => Interlocked.Exchange(ref _held, 0);
}

/// <summary>Body contract for POST /api/cutover/set-maintenance (operator-only).</summary>
public sealed record CutoverSetMaintenanceRequest(bool Active);
