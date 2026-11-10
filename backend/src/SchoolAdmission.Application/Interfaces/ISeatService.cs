using SchoolAdmission.Application.DTOs;

namespace SchoolAdmission.Application.Interfaces;

/// <summary>
/// Core contract for seat allocation and management business operations.
/// </summary>
public interface ISeatService
{
    Task<IReadOnlyList<ClassDto>> GetAllClassesAsync(CancellationToken cancellationToken = default);
    Task<ClassDto?> GetClassByIdAsync(Guid classId, CancellationToken cancellationToken = default);
    Task<ClassDto> CreateClassAsync(CreateClassRequest request, CancellationToken cancellationToken = default);
    
    Task<SeatDto> HoldSeatAsync(Guid classId, HoldSeatRequest request, CancellationToken cancellationToken = default);
    Task<SeatDto> ConfirmSeatAsync(Guid classId, ConfirmSeatRequest request, CancellationToken cancellationToken = default);
    Task<SeatDto> CancelHoldAsync(Guid classId, string studentId, CancellationToken cancellationToken = default);
}
