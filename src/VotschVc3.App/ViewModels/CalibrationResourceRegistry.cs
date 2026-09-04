namespace VotschVc3.App.ViewModels;

/// <summary>Prevents two independent calibration workspaces from opening the same hardware/API.</summary>
internal static class CalibrationResourceRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Reservation> Reservations = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryAcquire(string resourceKey, Guid ownerId, string ownerName, out string occupiedBy)
    {
        lock (Sync)
        {
            if (Reservations.TryGetValue(resourceKey, out Reservation? reservation))
            {
                if (reservation.OwnerId == ownerId)
                {
                    occupiedBy = string.Empty;
                    return true;
                }

                occupiedBy = reservation.OwnerName;
                return false;
            }

            Reservations[resourceKey] = new(ownerId, ownerName);
            occupiedBy = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// Returns true only for a live in-process reservation owned by another calibration workspace.
    /// Persistent WIKA assignment metadata is intentionally not considered here.
    /// </summary>
    public static bool IsReservedByOther(string resourceKey, Guid ownerId, out string occupiedBy)
    {
        lock (Sync)
        {
            if (Reservations.TryGetValue(resourceKey, out Reservation? reservation) && reservation.OwnerId != ownerId)
            {
                occupiedBy = reservation.OwnerName;
                return true;
            }

            occupiedBy = string.Empty;
            return false;
        }
    }

    public static void Release(string resourceKey, Guid ownerId)
    {
        lock (Sync)
        {
            if (Reservations.TryGetValue(resourceKey, out Reservation? reservation) && reservation.OwnerId == ownerId)
            {
                Reservations.Remove(resourceKey);
            }
        }
    }

    public static string F100Key(string portName) => $"f100:{portName.Trim().ToUpperInvariant()}";

    public static string PeakLoggerKey(string host, int port)
    {
        string normalizedHost = host.Trim().ToLowerInvariant();
        if (normalizedHost is "" or "." or "127.0.0.1" or "::1" ||
            normalizedHost == Environment.MachineName.ToLowerInvariant())
        {
            normalizedHost = "localhost";
        }

        return $"peaklogger:{normalizedHost}:{port}";
    }

    private sealed record Reservation(Guid OwnerId, string OwnerName);
}
