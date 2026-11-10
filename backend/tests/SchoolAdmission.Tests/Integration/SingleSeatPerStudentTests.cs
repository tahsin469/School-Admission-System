using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Application.DTOs;
using SchoolAdmission.Application.Exceptions;
using SchoolAdmission.Infrastructure.Persistence;
using SchoolAdmission.Infrastructure.Services;
using Xunit;

namespace SchoolAdmission.Tests.Integration;

public class SingleSeatPerStudentTests
{
    private static SchoolDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<SchoolDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new SchoolDbContext(options);
    }

    [Fact]
    public async Task HoldSeat_WhenStudentAlreadyHoldsASeat_ThrowsSeatConflictException()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var seatService = new SeatService(context);

        var createdClass = await seatService.CreateClassAsync(new CreateClassRequest("Class 1", 10, 1500m));
        const string studentId = "20051234567890123";

        // Act 1: First hold succeeds
        var firstSeat = await seatService.HoldSeatAsync(createdClass.Id, new HoldSeatRequest(studentId));
        Assert.NotNull(firstSeat);

        // Act 2 & Assert: Second hold with same studentId MUST throw SeatConflictException
        await Assert.ThrowsAsync<SeatConflictException>(async () =>
        {
            await seatService.HoldSeatAsync(createdClass.Id, new HoldSeatRequest(studentId));
        });
    }

    [Fact]
    public async Task HoldSeat_WhenStudentCancelsHold_CanHoldAnotherSeat()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var seatService = new SeatService(context);

        var createdClass = await seatService.CreateClassAsync(new CreateClassRequest("Class 2", 10, 1500m));
        const string studentId = "20051234567890123";

        // Hold seat
        await seatService.HoldSeatAsync(createdClass.Id, new HoldSeatRequest(studentId));

        // Act: Cancel hold
        await seatService.CancelHoldAsync(createdClass.Id, studentId);

        // Act: Hold again -> Should succeed without conflict
        var newSeat = await seatService.HoldSeatAsync(createdClass.Id, new HoldSeatRequest(studentId));

        // Assert
        Assert.NotNull(newSeat);
    }
}
