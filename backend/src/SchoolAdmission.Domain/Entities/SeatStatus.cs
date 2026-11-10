namespace SchoolAdmission.Domain.Entities;

/// <summary>
/// Represents the lifecycle states of a classroom seat.
/// </summary>
public enum SeatStatus
{
    /// <summary>
    /// The seat is open and can be held by any student.
    /// </summary>
    Available = 0,

    /// <summary>
    /// The seat is temporarily held for a student pending payment (2-minute window).
    /// </summary>
    Held = 1,

    /// <summary>
    /// The seat is permanently confirmed and paid for.
    /// </summary>
    Confirmed = 2
}
