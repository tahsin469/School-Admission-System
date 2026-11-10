using SchoolAdmission.Domain.Entities;
using Xunit;

namespace SchoolAdmission.Tests.Domain;

public class ClassDomainTests
{
    [Fact]
    public void Create_WithValidParameters_InitializesClassAndSeatsCorrectly()
    {
        // Arrange
        const string className = "Class 1";
        const int totalSeats = 10;
        const decimal fee = 2500m;

        // Act
        var schoolClass = Class.Create(className, totalSeats, fee);

        // Assert
        Assert.NotEqual(Guid.Empty, schoolClass.Id);
        Assert.Equal(className, schoolClass.Name);
        Assert.Equal(totalSeats, schoolClass.TotalSeats);
        Assert.Equal(fee, schoolClass.AdmissionFee);
        Assert.Equal(totalSeats, schoolClass.Seats.Count);

        // Verify individual seat generation
        for (int i = 1; i <= totalSeats; i++)
        {
            var seat = schoolClass.Seats.FirstOrDefault(s => s.SeatNumber == i);
            Assert.NotNull(seat);
            Assert.Equal(SeatStatus.Available, seat.Status);
            Assert.Null(seat.CurrentStudentId);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithInvalidName_ThrowsArgumentException(string? invalidName)
    {
        Assert.Throws<ArgumentException>(() => Class.Create(invalidName!, 10, 1000m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_WithInvalidTotalSeats_ThrowsArgumentException(int invalidSeats)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Class.Create("Class 1", invalidSeats, 1000m));
    }

    [Fact]
    public void Seat_Hold_TransitionsStatusToHeldWithExpiry()
    {
        // Arrange
        var schoolClass = Class.Create("Class 1", 5, 1000m);
        var seat = schoolClass.Seats.First();
        const string studentId = "20051234567890123";

        // Act
        seat.Hold(studentId, TimeSpan.FromMinutes(2));

        // Assert
        Assert.Equal(SeatStatus.Held, seat.Status);
        Assert.Equal(studentId, seat.CurrentStudentId);
        Assert.NotNull(seat.HeldAtUtc);
        Assert.NotNull(seat.ExpiresAtUtc);
        Assert.True(seat.ExpiresAtUtc > seat.HeldAtUtc);
    }

    [Fact]
    public void Seat_Confirm_TransitionsStatusToConfirmed()
    {
        // Arrange
        var schoolClass = Class.Create("Class 1", 5, 1000m);
        var seat = schoolClass.Seats.First();
        const string studentId = "20051234567890123";
        seat.Hold(studentId, TimeSpan.FromMinutes(2));

        // Act
        seat.Confirm(studentId);

        // Assert
        Assert.Equal(SeatStatus.Confirmed, seat.Status);
        Assert.Equal(studentId, seat.CurrentStudentId);
        Assert.Null(seat.ExpiresAtUtc); // Expiry cleared once confirmed
    }

    [Fact]
    public void Seat_Release_TransitionsStatusBackToAvailable()
    {
        // Arrange
        var schoolClass = Class.Create("Class 1", 5, 1000m);
        var seat = schoolClass.Seats.First();
        seat.Hold("20051234567890123", TimeSpan.FromMinutes(2));

        // Act
        seat.Release();

        // Assert
        Assert.Equal(SeatStatus.Available, seat.Status);
        Assert.Null(seat.CurrentStudentId);
        Assert.Null(seat.HeldAtUtc);
        Assert.Null(seat.ExpiresAtUtc);
    }
}
