using System.Data;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Application.DTOs;
using SchoolAdmission.Application.Exceptions;
using SchoolAdmission.Application.Interfaces;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Infrastructure.Persistence;

namespace SchoolAdmission.Infrastructure.Services;

public class SeatService : ISeatService
{
    private readonly SchoolDbContext _context;
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(2);

    public SeatService(SchoolDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ClassDto>> GetAllClassesAsync(CancellationToken cancellationToken = default)
    {
        var classes = await _context.Classes
            .AsNoTracking()
            .Include(c => c.Seats)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        return classes.Select(c => MapToClassDto(c, now)).ToList();
    }

    public async Task<ClassDto?> GetClassByIdAsync(Guid classId, CancellationToken cancellationToken = default)
    {
        var schoolClass = await _context.Classes
            .AsNoTracking()
            .Include(c => c.Seats)
            .FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);

        if (schoolClass == null) return null;

        return MapToClassDto(schoolClass, DateTime.UtcNow);
    }

    public async Task<ClassDto> CreateClassAsync(CreateClassRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Classes
            .AsNoTracking()
            .AnyAsync(c => c.Name.ToLower() == request.Name.ToLower(), cancellationToken);

        if (existing)
        {
            throw new BusinessRuleException($"A class with the name '{request.Name}' already exists.");
        }

        var schoolClass = Class.Create(request.Name, request.TotalSeats, request.AdmissionFee);
        _context.Classes.Add(schoolClass);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToClassDto(schoolClass, DateTime.UtcNow);
    }

    public async Task<SeatDto> HoldSeatAsync(Guid classId, HoldSeatRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Verify class existence
        var classExists = await _context.Classes.AnyAsync(c => c.Id == classId, cancellationToken);
        if (!classExists)
        {
            throw new NotFoundException($"Class with ID '{classId}' was not found.");
        }

        // 2. Rule: Single active seat per student in the same class
        var alreadyHolding = await _context.Seats
            .AnyAsync(s => s.ClassId == classId 
                        && s.CurrentStudentId == request.StudentId 
                        && (s.Status == SeatStatus.Held || s.Status == SeatStatus.Confirmed), 
                      cancellationToken);

        if (alreadyHolding)
        {
            throw new SeatConflictException($"Student '{request.StudentId}' already holds or has confirmed a seat in this class.");
        }

        // 3. Concurrency Lock: Database-level row locking
        var isPostgres = _context.Database.IsNpgsql();

        Seat? seatToHold = null;

        if (isPostgres)
        {
            // PostgreSQL native FOR UPDATE SKIP LOCKED ensures race-free allocation under high load
            using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            seatToHold = await _context.Seats
                .FromSqlInterpolated($@"
                    SELECT * FROM ""Seats""
                    WHERE ""ClassId"" = {classId} AND ""Status"" = {(int)SeatStatus.Available}
                    ORDER BY ""SeatNumber"" ASC
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED")
                .FirstOrDefaultAsync(cancellationToken);

            if (seatToHold == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new SeatConflictException("No available seats left in this class.");
            }

            seatToHold.Hold(request.StudentId, HoldDuration);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            // Fallback for Unit/Integration In-Memory or SQLite testing
            seatToHold = await _context.Seats
                .Where(s => s.ClassId == classId && s.Status == SeatStatus.Available)
                .OrderBy(s => s.SeatNumber)
                .FirstOrDefaultAsync(cancellationToken);

            if (seatToHold == null)
            {
                throw new SeatConflictException("No available seats left in this class.");
            }

            seatToHold.Hold(request.StudentId, HoldDuration);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return MapToSeatDto(seatToHold, DateTime.UtcNow);
    }

    public async Task<SeatDto> ConfirmSeatAsync(Guid classId, ConfirmSeatRequest request, CancellationToken cancellationToken = default)
    {
        var seat = await _context.Seats
            .FirstOrDefaultAsync(s => s.ClassId == classId && s.CurrentStudentId == request.StudentId, cancellationToken);

        if (seat == null)
        {
            throw new NotFoundException($"No seat hold found for student '{request.StudentId}' in class '{classId}'.");
        }

        if (seat.Status == SeatStatus.Confirmed)
        {
            return MapToSeatDto(seat, DateTime.UtcNow);
        }

        if (seat.Status != SeatStatus.Held)
        {
            throw new BusinessRuleException($"Seat cannot be confirmed because current status is '{seat.Status}'.");
        }

        if (seat.IsExpired(DateTime.UtcNow))
        {
            seat.Release();
            await _context.SaveChangesAsync(cancellationToken);
            throw new BusinessRuleException("The seat hold has expired and has been released. Please hold again.");
        }

        seat.Confirm(request.StudentId);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToSeatDto(seat, DateTime.UtcNow);
    }

    public async Task<SeatDto> CancelHoldAsync(Guid classId, string studentId, CancellationToken cancellationToken = default)
    {
        var seat = await _context.Seats
            .FirstOrDefaultAsync(s => s.ClassId == classId && s.CurrentStudentId == studentId && s.Status == SeatStatus.Held, cancellationToken);

        if (seat == null)
        {
            throw new NotFoundException($"No active held seat found for student '{studentId}' in class '{classId}'.");
        }

        seat.Release();
        await _context.SaveChangesAsync(cancellationToken);

        return MapToSeatDto(seat, DateTime.UtcNow);
    }

    // Mapping Helpers
    private static ClassDto MapToClassDto(Class c, DateTime now)
    {
        var seats = c.Seats
            .OrderBy(s => s.SeatNumber)
            .Select(s => MapToSeatDto(s, now))
            .ToList();

        var available = seats.Count(s => s.Status == SeatStatus.Available);
        var held = seats.Count(s => s.Status == SeatStatus.Held);
        var confirmed = seats.Count(s => s.Status == SeatStatus.Confirmed);

        return new ClassDto(
            c.Id,
            c.Name,
            c.TotalSeats,
            available,
            held,
            confirmed,
            c.AdmissionFee,
            seats
        );
    }

    private static SeatDto MapToSeatDto(Seat s, DateTime now)
    {
        var remainingSeconds = 0;
        if (s.Status == SeatStatus.Held && s.ExpiresAtUtc.HasValue)
        {
            var diff = (int)(s.ExpiresAtUtc.Value - now).TotalSeconds;
            remainingSeconds = diff > 0 ? diff : 0;
        }

        return new SeatDto(
            s.Id,
            s.SeatNumber,
            s.Status,
            s.CurrentStudentId,
            s.HeldAtUtc,
            s.ExpiresAtUtc,
            remainingSeconds
        );
    }
}
