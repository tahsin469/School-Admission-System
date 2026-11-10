using FluentValidation;
using SchoolAdmission.Application.DTOs;

namespace SchoolAdmission.Application.Validators;

public class CreateClassRequestValidator : AbstractValidator<CreateClassRequest>
{
    public CreateClassRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Class name is required.")
            .MaximumLength(100).WithMessage("Class name must not exceed 100 characters.");

        RuleFor(x => x.TotalSeats)
            .GreaterThan(0).WithMessage("Total seats must be greater than zero.")
            .LessThanOrEqualTo(500).WithMessage("Total seats cannot exceed 500 seats per class.");

        RuleFor(x => x.AdmissionFee)
            .GreaterThanOrEqualTo(0).WithMessage("Admission fee must be a non-negative amount.");
    }
}

public class HoldSeatRequestValidator : AbstractValidator<HoldSeatRequest>
{
    public HoldSeatRequestValidator()
    {
        RuleFor(x => x.StudentId)
            .NotEmpty().WithMessage("Student ID (Birth Registration Number) is required.")
            .Matches(@"^[0-9]{17}$")
            .WithMessage("Student ID must be a valid 17-digit Birth Registration Number (digits only).");
    }
}

public class ConfirmSeatRequestValidator : AbstractValidator<ConfirmSeatRequest>
{
    public ConfirmSeatRequestValidator()
    {
        RuleFor(x => x.StudentId)
            .NotEmpty().WithMessage("Student ID is required.")
            .Matches(@"^[0-9]{17}$")
            .WithMessage("Student ID must be a valid 17-digit Birth Registration Number.");
    }
}
