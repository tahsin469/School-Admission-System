using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Application.DTOs;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Infrastructure.Persistence;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
[Produces("application/json")]
public class SeatAllocationContractController : ControllerBase
{
    private readonly SchoolDbContext _context;
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(600); // 10 minutes default as per spec

    public SeatAllocationContractController(SchoolDbContext context)
    {
        _context = context;
    }

    private static string GetStatusString(Seat seat, DateTime now)
    {
        if (seat.Status == SeatStatus.Confirmed) return "CONFIRMED";
        if (seat.Status == SeatStatus.Held)
        {
            if (seat.ExpiresAtUtc.HasValue && seat.ExpiresAtUtc.Value <= now)
                return "EXPIRED";
            return "HELD";
        }
        return "AVAILABLE";
    }

    /// <summary>
    /// 1. POST /classes - Create new class
    /// </summary>
    [HttpPost("classes")]
    public async Task<IActionResult> CreateClass([FromBody] SeatAllocationCreateClassRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.name))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = "Class name is required."
            });
        }

        if (request.totalSeats <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = "Total seats must be greater than 0."
            });
        }

        var schoolClass = Class.Create(request.name, request.totalSeats, 2000m);
        _context.Classes.Add(schoolClass);
        await _context.SaveChangesAsync(cancellationToken);

        var response = new
        {
            id = schoolClass.Id.ToString(),
            name = schoolClass.Name,
            totalSeats = schoolClass.TotalSeats
        };

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// 2. GET /classes - List all classes with seat counts
    /// </summary>
    [HttpGet("classes")]
    public async Task<IActionResult> GetAllClasses(CancellationToken cancellationToken)
    {
        var classes = await _context.Classes
            .AsNoTracking()
            .Include(c => c.Seats)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var result = classes.Select(c =>
        {
            var held = c.Seats.Count(s => s.Status == SeatStatus.Held && s.ExpiresAtUtc > now);
            var confirmed = c.Seats.Count(s => s.Status == SeatStatus.Confirmed);
            var available = c.TotalSeats - (held + confirmed);

            return new SeatAllocationClassResponse(
                c.Id.ToString(),
                c.Name,
                c.TotalSeats,
                held,
                confirmed,
                Math.Max(0, available)
            );
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// 3. GET /classes/{classId} - Get class details and seat counts
    /// </summary>
    [HttpGet("classes/{classId}")]
    public async Task<IActionResult> GetClassById(string classId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(classId, out var classGuid))
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = $"Class with ID '{classId}' was not found."
            });
        }

        var schoolClass = await _context.Classes
            .AsNoTracking()
            .Include(c => c.Seats)
            .FirstOrDefaultAsync(c => c.Id == classGuid, cancellationToken);

        if (schoolClass == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = $"Class with ID '{classId}' was not found."
            });
        }

        var now = DateTime.UtcNow;
        var held = schoolClass.Seats.Count(s => s.Status == SeatStatus.Held && s.ExpiresAtUtc > now);
        var confirmed = schoolClass.Seats.Count(s => s.Status == SeatStatus.Confirmed);
        var available = schoolClass.TotalSeats - (held + confirmed);

        var result = new SeatAllocationClassResponse(
            schoolClass.Id.ToString(),
            schoolClass.Name,
            schoolClass.TotalSeats,
            held,
            confirmed,
            Math.Max(0, available)
        );

        return Ok(result);
    }

    /// <summary>
    /// 4. POST /classes/{classId}/holds - Hold a seat (Header: X-Student-Id)
    /// </summary>
    [HttpPost("classes/{classId}/holds")]
    public async Task<IActionResult> HoldSeat(
        string classId,
        [FromHeader(Name = "X-Student-Id")] string? studentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing Header",
                Detail = "X-Student-Id header is required."
            });
        }

        // Strict 17-digit regulatory validation
        if (!System.Text.RegularExpressions.Regex.IsMatch(studentId.Trim(), @"^[0-9]{17}$"))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = "Student ID must be an exact 17-digit Birth Registration Number."
            });
        }

        if (!Guid.TryParse(classId, out var classGuid))
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = $"Class with ID '{classId}' was not found."
            });
        }

        var schoolClass = await _context.Classes.FindAsync([classGuid], cancellationToken);
        if (schoolClass == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = $"Class with ID '{classId}' was not found."
            });
        }

        var now = DateTime.UtcNow;

        // Rule: Single active hold per student in the same class
        var alreadyActive = await _context.Seats.AnyAsync(s =>
            s.ClassId == classGuid &&
            s.CurrentStudentId == studentId &&
            ((s.Status == SeatStatus.Held && s.ExpiresAtUtc > now) || s.Status == SeatStatus.Confirmed),
            cancellationToken);

        if (alreadyActive)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Seat Conflict",
                Detail = $"Student '{studentId}' already has an active hold or confirmed seat in this class."
            });
        }

        // Concurrency Lock: Row-level lock via Postgres or fallback
        Seat? seatToHold = null;
        var isPostgres = _context.Database.IsNpgsql();

        if (isPostgres)
        {
            using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);

            seatToHold = await _context.Seats
                .FromSqlInterpolated($@"
                    SELECT * FROM ""Seats""
                    WHERE ""ClassId"" = {classGuid} AND ""Status"" = {(int)SeatStatus.Available}
                    ORDER BY ""SeatNumber"" ASC
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED")
                .FirstOrDefaultAsync(cancellationToken);

            if (seatToHold == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Seat Unavailable",
                    Detail = "No available seats left in this class."
                });
            }

            seatToHold.Hold(studentId, HoldDuration);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            seatToHold = await _context.Seats
                .Where(s => s.ClassId == classGuid && s.Status == SeatStatus.Available)
                .OrderBy(s => s.SeatNumber)
                .FirstOrDefaultAsync(cancellationToken);

            if (seatToHold == null)
            {
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Seat Unavailable",
                    Detail = "No available seats left in this class."
                });
            }

            seatToHold.Hold(studentId, HoldDuration);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var response = new SeatAllocationHoldResponse(
            seatToHold.Id.ToString(),
            schoolClass.Id.ToString(),
            studentId,
            "HELD",
            seatToHold.ExpiresAtUtc
        );

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// 5. GET /holds/{holdId} - Get hold status
    /// </summary>
    [HttpGet("holds/{holdId}")]
    public async Task<IActionResult> GetHoldById(string holdId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(holdId, out var holdGuid))
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = $"Hold with ID '{holdId}' was not found."
            });
        }

        var seat = await _context.Seats.FindAsync([holdGuid], cancellationToken);
        if (seat == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = $"Hold with ID '{holdId}' was not found."
            });
        }

        var now = DateTime.UtcNow;
        var statusStr = GetStatusString(seat, now);

        var response = new SeatAllocationHoldStatusResponse(
            seat.Id.ToString(),
            seat.ClassId.ToString(),
            statusStr,
            seat.ExpiresAtUtc
        );

        return Ok(response);
    }

    /// <summary>
    /// 6. POST /holds/{holdId}/confirm - Confirm admission payment (Header: X-Student-Id)
    /// </summary>
    [HttpPost("holds/{holdId}/confirm")]
    public async Task<IActionResult> ConfirmHold(
        string holdId,
        [FromHeader(Name = "X-Student-Id")] string? studentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing Header",
                Detail = "X-Student-Id header is required."
            });
        }

        // Strict 17-digit regulatory validation
        if (!System.Text.RegularExpressions.Regex.IsMatch(studentId.Trim(), @"^[0-9]{17}$"))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = "Student ID must be an exact 17-digit Birth Registration Number."
            });
        }

        if (!Guid.TryParse(holdId, out var holdGuid))
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = $"Hold with ID '{holdId}' was not found."
            });
        }

        var seat = await _context.Seats.FindAsync([holdGuid], cancellationToken);
        if (seat == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = $"Hold with ID '{holdId}' was not found."
            });
        }

        // Idempotency: Already confirmed
        if (seat.Status == SeatStatus.Confirmed)
        {
            return Ok(new SeatAllocationConfirmResponse(seat.Id.ToString(), "CONFIRMED"));
        }

        // Rule: Only the holding student can confirm
        if (seat.CurrentStudentId != studentId)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Operation",
                Detail = "Cannot confirm a hold belonging to another student."
            });
        }

        var now = DateTime.UtcNow;
        if (seat.IsExpired(now))
        {
            seat.Release();
            await _context.SaveChangesAsync(cancellationToken);

            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Hold Expired",
                Detail = "Seat hold has expired and has been released."
            });
        }

        seat.Confirm(studentId);
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new SeatAllocationConfirmResponse(seat.Id.ToString(), "CONFIRMED"));
    }

    /// <summary>
    /// 7. DELETE /holds/{holdId} - Cancel hold (Header: X-Student-Id) -> 204 NoContent
    /// </summary>
    [HttpDelete("holds/{holdId}")]
    public async Task<IActionResult> CancelHold(
        string holdId,
        [FromHeader(Name = "X-Student-Id")] string? studentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing Header",
                Detail = "X-Student-Id header is required."
            });
        }

        // Strict 17-digit regulatory validation
        if (!System.Text.RegularExpressions.Regex.IsMatch(studentId.Trim(), @"^[0-9]{17}$"))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = "Student ID must be an exact 17-digit Birth Registration Number."
            });
        }

        if (!Guid.TryParse(holdId, out var holdGuid))
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = $"Hold with ID '{holdId}' was not found."
            });
        }

        var seat = await _context.Seats.FindAsync([holdGuid], cancellationToken);
        if (seat == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = $"Hold with ID '{holdId}' was not found."
            });
        }

        if (seat.CurrentStudentId != studentId)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Operation",
                Detail = "Cannot cancel a hold belonging to another student."
            });
        }

        if (seat.Status == SeatStatus.Confirmed)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Operation",
                Detail = "Cannot cancel a confirmed seat."
            });
        }

        seat.Release();
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent(); // 204 as per specification
    }

    /// <summary>
    /// 8. GET /me/holds - List all holds for student (Header: X-Student-Id)
    /// </summary>
    [HttpGet("me/holds")]
    public async Task<IActionResult> GetMyHolds(
        [FromHeader(Name = "X-Student-Id")] string? studentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing Header",
                Detail = "X-Student-Id header is required."
            });
        }

        // Strict 17-digit regulatory validation
        if (!System.Text.RegularExpressions.Regex.IsMatch(studentId.Trim(), @"^[0-9]{17}$"))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = "Student ID must be an exact 17-digit Birth Registration Number."
            });
        }

        var seats = await _context.Seats
            .AsNoTracking()
            .Include(s => s.Class)
            .Where(s => s.CurrentStudentId == studentId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        var result = seats.Select(s => new SeatAllocationMyHoldItem(
            s.Id.ToString(),
            s.ClassId.ToString(),
            s.Class?.Name ?? "",
            GetStatusString(s, now),
            s.ExpiresAtUtc
        )).ToList();

        return Ok(result);
    }
}
