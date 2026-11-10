namespace SchoolAdmission.Domain.Entities;

/// <summary>
/// Domain Entity representing a specific seat within a class.
/// Contains rich state-machine business methods for hold, confirm, and expiry.
/// </summary>
public sealed class Seat
{
    public Guid Id { get; private set; }
    public Guid ClassId { get; private set; }
    public int SeatNumber { get; private set; }
    public SeatStatus Status { get; private set; }
    public string? CurrentStudentId { get; private set; }
    public DateTime? HeldAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public int Version { get; private set; }

    // Navigation property
    public Class? Class { get; private set; }

    private Seat() { }

    public static Seat Create(Guid classId, int seatNumber)
    {
        if (seatNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(seatNumber), "Seat number must be positive.");

        return new Seat
        {
            Id = Guid.NewGuid(),
            ClassId = classId,
            SeatNumber = seatNumber,
            Status = SeatStatus.Available,
            CurrentStudentId = null,
            HeldAtUtc = null,
            ExpiresAtUtc = null,
            Version = 1
        };
    }

    /// <summary>
    /// Holds the seat for a specific student for a temporary duration (default: 2 minutes).
    /// </summary>
    public void Hold(string studentId, TimeSpan holdDuration)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("Student ID cannot be empty.", nameof(studentId));

        if (Status == SeatStatus.Confirmed)
            throw new InvalidOperationException("Seat is already confirmed and cannot be held.");

        if (Status == SeatStatus.Held && !IsExpired(DateTime.UtcNow))
            throw new InvalidOperationException("Seat is currently held by another student.");

        Status = SeatStatus.Held;
        CurrentStudentId = studentId.Trim();
        HeldAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = HeldAtUtc.Value.Add(holdDuration);
        Version++;
    }

    /// <summary>
    /// Confirms admission by purchasing this held seat.
    /// </summary>
    public void Confirm(string studentId)
    {
        if (Status != SeatStatus.Held)
            throw new InvalidOperationException("Only a held seat can be confirmed.");

        if (CurrentStudentId != studentId)
            throw new InvalidOperationException("Cannot confirm a seat held by another student.");

        if (IsExpired(DateTime.UtcNow))
            throw new InvalidOperationException("Hold on this seat has expired.");

        Status = SeatStatus.Confirmed;
        ExpiresAtUtc = null; // Confirmed seats never expire
        Version++;
    }

    /// <summary>
    /// Releases the hold back to Available status (called by background sweeper or cancel API).
    /// </summary>
    public void Release()
    {
        if (Status == SeatStatus.Confirmed)
            throw new InvalidOperationException("Cannot release a permanently confirmed seat.");

        Status = SeatStatus.Available;
        CurrentStudentId = null;
        HeldAtUtc = null;
        ExpiresAtUtc = null;
        Version++;
    }

    /// <summary>
    /// Checks whether the temporary hold has expired against current UTC time.
    /// </summary>
    public bool IsExpired(DateTime currentUtcTime)
    {
        return Status == SeatStatus.Held && ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= currentUtcTime;
    }
}
