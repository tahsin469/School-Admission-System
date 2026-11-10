using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchoolAdmission.Domain.Entities;
using SchoolAdmission.Infrastructure.Persistence;

namespace SchoolAdmission.Infrastructure.BackgroundServices;

public class SeatExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SeatExpiryWorker> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    public SeatExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<SeatExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Seat Expiry Background Worker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepExpiredSeatsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while sweeping expired seat holds.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("Seat Expiry Background Worker is stopping.");
    }

    private async Task SweepExpiredSeatsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

        var now = DateTime.UtcNow;

        var expiredSeats = await context.Seats
            .Where(s => s.Status == SeatStatus.Held && s.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);

        if (expiredSeats.Count > 0)
        {
            foreach (var seat in expiredSeats)
            {
                _logger.LogInformation(
                    "Releasing expired hold on Seat {SeatNumber} (ID: {SeatId}) held by student {StudentId}.",
                    seat.SeatNumber, seat.Id, seat.CurrentStudentId);

                seat.Release();
            }

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
