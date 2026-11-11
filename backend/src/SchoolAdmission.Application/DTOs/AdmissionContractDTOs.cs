namespace SchoolAdmission.Application.DTOs;

public record AdmissionClassResponse(
    string id,
    string name,
    int totalSeats,
    int heldSeats,
    int confirmedSeats,
    int availableSeats
);

public record AdmissionCreateClassRequest(
    string name,
    int totalSeats
);

public record AdmissionHoldResponse(
    string holdId,
    string classId,
    string studentId,
    string status,
    DateTime? expiresAt
);

public record AdmissionHoldStatusResponse(
    string holdId,
    string classId,
    string status,
    DateTime? expiresAt
);

public record AdmissionConfirmResponse(
    string holdId,
    string status
);

public record AdmissionMyHoldItem(
    string holdId,
    string classId,
    string className,
    string status,
    DateTime? expiresAt
);
