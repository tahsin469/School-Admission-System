using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SchoolAdmission.Application.Common;
using SchoolAdmission.Application.DTOs;
using SchoolAdmission.Application.Interfaces;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
[Route("admin/classes")]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly ISeatService _seatService;
    private readonly IValidator<CreateClassRequest> _validator;

    public AdminController(ISeatService seatService, IValidator<CreateClassRequest> validator)
    {
        _seatService = seatService;
        _validator = validator;
    }

    /// <summary>
    /// Creates a new class with configured seat capacity and admission fee.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ClassDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateClass([FromBody] CreateClassRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var createdClass = await _seatService.CreateClassAsync(request, cancellationToken);
        var response = ApiResponse<ClassDto>.Ok(createdClass, "Class created successfully.");

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
