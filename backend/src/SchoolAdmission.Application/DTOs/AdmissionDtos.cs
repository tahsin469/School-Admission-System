using SchoolAdmission.Domain.Entities;

namespace SchoolAdmission.Application.DTOs;

public record SeatDto(
    Guid Id,
    int SeatNumber,
    SeatStatus Status,
    string? CurrentStudentId,
    DateTime? HeldAtUtc,
    DateTime? ExpiresAtUtc,
    int RemainingSeconds
);

public record ClassDto(
    Guid Id,
    string Name,
    int TotalSeats,
    int AvailableSeats,
    int HeldSeats,
    int ConfirmedSeats,
    decimal AdmissionFee,
    IReadOnlyList<SeatDto> Seats
);

public record CreateClassRequest(
    string Name,
    int TotalSeats,
    decimal AdmissionFee
);

public record HoldSeatRequest(
    string StudentId
);

public record ConfirmSeatRequest(
    string StudentId
);
