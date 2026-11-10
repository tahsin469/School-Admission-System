using SchoolAdmission.Application.DTOs;
using SchoolAdmission.Application.Validators;
using Xunit;

namespace SchoolAdmission.Tests.Application;

public class ValidatorsTests
{
    private readonly HoldSeatRequestValidator _holdValidator = new();
    private readonly CreateClassRequestValidator _classValidator = new();

    [Theory]
    [InlineData("20051234567890123", true)] // Valid 17 digits
    [InlineData("12345678901234567", true)] // Valid 17 digits
    [InlineData("12345", false)]             // Too short
    [InlineData("2005123456789012345", false)] // Too long
    [InlineData("2005123456789012A", false)] // Contains non-digits
    [InlineData("", false)]                  // Empty
    public void HoldSeatRequestValidator_ValidatesStudentIdPattern(string studentId, bool expectedValid)
    {
        var request = new HoldSeatRequest(studentId);
        var result = _holdValidator.Validate(request);
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("Class 1", 30, 2000, true)]
    [InlineData("", 30, 2000, false)]          // Empty name
    [InlineData("Class 1", 0, 2000, false)]       // 0 seats
    [InlineData("Class 1", -1, 2000, false)]      // Negative seats
    [InlineData("Class 1", 1000, 2000, false)]    // Exceeds max seats (500)
    [InlineData("Class 1", 30, -50, false)]       // Negative fee
    public void CreateClassRequestValidator_ValidatesClassInputs(string name, int totalSeats, decimal fee, bool expectedValid)
    {
        var request = new CreateClassRequest(name, totalSeats, fee);
        var result = _classValidator.Validate(request);
        Assert.Equal(expectedValid, result.IsValid);
    }
}
