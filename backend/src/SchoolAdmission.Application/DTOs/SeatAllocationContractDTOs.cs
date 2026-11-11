namespace SchoolAdmission.Application.DTOs;

public record SeatAllocationClassResponse(
    string id,
    string name,
    int totalSeats,
    int heldSeats,
    int confirmedSeats,
    int availableSeats
);

public record SeatAllocationCreateClassRequest(
    string name,
    int totalSeats
);

public record SeatAllocationHoldResponse(
    string holdId,
    string classId,
    string studentId,
    string status,
    DateTime? expiresAt
);

public record SeatAllocationHoldStatusResponse(
    string holdId,
    string classId,
    string status,
    DateTime? expiresAt
);

public record SeatAllocationConfirmResponse(
    string holdId,
    string status
);

public record SeatAllocationMyHoldItem(
    string holdId,
    string classId,
    string className,
    string status,
    DateTime? expiresAt
);
