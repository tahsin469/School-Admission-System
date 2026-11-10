using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Application.DTOs;
using SchoolAdmission.Application.Exceptions;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Infrastructure.Persistence;
using SchoolAdmission.Infrastructure.Services;
using Xunit;

namespace SchoolAdmission.Tests.Integration;

public class SeatConcurrencyStressTests
{
    [Fact]
    public async Task TwentyConcurrentThreads_AttemptingToHoldThreeSeats_ExactlyThreeSucceedAndSeventeenFailWithoutOverbooking()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<SchoolDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        // Initialize class with exactly 3 seats
        using (var setupContext = new SchoolDbContext(options))
        {
            var setupService = new SeatService(setupContext);
            await setupService.CreateClassAsync(new CreateClassRequest("Concurrency Class", 3, 2000m));
        }

        Guid classId;
        using (var readContext = new SchoolDbContext(options))
        {
            var created = await readContext.Classes.FirstAsync();
            classId = created.Id;
        }

        const int totalConcurrentRequests = 20;
        var successList = new ConcurrentBag<SeatDto>();
        var failureList = new ConcurrentBag<string>();

        // Act: 20 simultaneous threads firing at the exact same millisecond
        var tasks = Enumerable.Range(1, totalConcurrentRequests).Select(async i =>
        {
            // Each thread gets its own DbContext instance (mimicking real HTTP request scope)
            using var requestContext = new SchoolDbContext(options);
            var service = new SeatService(requestContext);
            var studentId = $"200500000000000{i:D2}"; // 17 digits unique ID

            try
            {
                var seat = await service.HoldSeatAsync(classId, new HoldSeatRequest(studentId));
                successList.Add(seat);
            }
            catch (SeatConflictException ex)
            {
                failureList.Add(ex.Message);
            }
        });

        await Task.WhenAll(tasks);

        // Assert: Exactly 3 seats held, exactly 17 failed
        Assert.Equal(3, successList.Count);
        Assert.Equal(17, failureList.Count);

        // Verify database state: Exactly 3 seats are held, 0 available, and 0 overbooked
        using (var verifyContext = new SchoolDbContext(options))
        {
            var finalClass = await verifyContext.Classes
                .Include(c => c.Seats)
                .FirstAsync(c => c.Id == classId);

            var heldSeats = finalClass.Seats.Count(s => s.Status == SeatStatus.Held);
            var availableSeats = finalClass.Seats.Count(s => s.Status == SeatStatus.Available);

            Assert.Equal(3, heldSeats);
            Assert.Equal(0, availableSeats);
            Assert.Equal(3, finalClass.Seats.Count); // Total seats remain unchanged
        }
    }
}
