using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Application.Common;
using SchoolAdmission.Application.DTOs;
using SchoolAdmission.Application.Interfaces;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
[Route("api/classes")]
[Produces("application/json")]
public class ClassesController : ControllerBase
{
    private readonly ISeatService _seatService;
    private readonly IValidator<HoldSeatRequest> _holdValidator;
    private readonly IValidator<ConfirmSeatRequest> _confirmValidator;

    public ClassesController(
        ISeatService seatService,
        IValidator<HoldSeatRequest> holdValidator,
        IValidator<ConfirmSeatRequest> confirmValidator)
    {
        _seatService = seatService;
        _holdValidator = holdValidator;
        _confirmValidator = confirmValidator;
    }

    /// <summary>
    /// Gets all classes with real-time seat availability counts (short polling support).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ClassDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllClasses(CancellationToken cancellationToken)
    {
        var classes = await _seatService.GetAllClassesAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ClassDto>>.Ok(classes));
    }

    /// <summary>
    /// Gets details of a specific class including individual seat states.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ClassDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClassById(Guid id, CancellationToken cancellationToken)
    {
        var schoolClass = await _seatService.GetClassByIdAsync(id, cancellationToken);
        if (schoolClass == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Class Not Found",
                Detail = $"Class with ID '{id}' was not found."
            });
        }

        return Ok(ApiResponse<ClassDto>.Ok(schoolClass));
    }

    /// <summary>
    /// Holds a seat temporarily for 120 seconds for the specified student.
    /// Employs native row-level database locking to prevent overbooking.
    /// </summary>
    [HttpPost("{id:guid}/hold")]
    [ProducesResponseType(typeof(ApiResponse<SeatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> HoldSeat(
        Guid id,
        [FromBody] HoldSeatRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _holdValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var heldSeat = await _seatService.HoldSeatAsync(id, request, cancellationToken);
        return Ok(ApiResponse<SeatDto>.Ok(heldSeat, "Seat held successfully for 120 seconds."));
    }

    /// <summary>
    /// Confirms a held seat permanently upon fee payment completion.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType(typeof(ApiResponse<SeatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmSeat(
        Guid id,
        [FromBody] ConfirmSeatRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _confirmValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var confirmedSeat = await _seatService.ConfirmSeatAsync(id, request, cancellationToken);
        return Ok(ApiResponse<SeatDto>.Ok(confirmedSeat, "Seat confirmed successfully."));
    }

    /// <summary>
    /// Cancels an active seat hold, releasing it back to available status immediately.
    /// </summary>
    [HttpDelete("{id:guid}/hold")]
    [ProducesResponseType(typeof(ApiResponse<SeatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelHold(
        Guid id,
        [FromQuery] string studentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing Parameter",
                Detail = "studentId query parameter is required."
            });
        }

        var releasedSeat = await _seatService.CancelHoldAsync(id, studentId, cancellationToken);
        return Ok(ApiResponse<SeatDto>.Ok(releasedSeat, "Seat hold cancelled successfully."));
    }
}
