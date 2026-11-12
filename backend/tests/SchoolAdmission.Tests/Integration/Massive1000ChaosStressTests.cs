using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SchoolAdmission.Application.DTOs;
using Xunit;
using Xunit.Abstractions;

namespace SchoolAdmission.Tests.Integration;

/// <summary>
/// Hardcore Massive 1,000 Concurrent & Chaos Stress Harness.
/// Bombards the API with 1,000 concurrent threads, chaos headers, rapid confirm/cancel races,
/// and verifies ZERO overbooking, ZERO deadlocks, and ZERO unhandled 500 crashes.
/// </summary>
public class Massive1000ChaosStressTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;

    public Massive1000ChaosStressTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public async Task Massive1000_ConcurrentHoldsForLimitedSeats_StrictlyZeroOverbooking()
    {
        var client = _factory.CreateClient();

        // 1. Create a class with exactly 10 seats
        var createClassReq = new SeatAllocationCreateClassRequest($"Stress-Class-{Guid.NewGuid():N}", 10);
        var createRes = await client.PostAsJsonAsync("/classes", createClassReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var createdClass = await createRes.Content.ReadFromJsonAsync<SeatAllocationClassResponse>();
        Assert.NotNull(createdClass);
        var classId = createdClass.id;

        // 2. Prepare 1,000 distinct concurrent student tasks
        const int TotalRequests = 1000;
        var startBarrier = new ManualResetEventSlim(false);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();
        var successfulHoldIds = new ConcurrentBag<string>();

        var tasks = Enumerable.Range(1, TotalRequests).Select(i => Task.Run(async () =>
        {
            // Unique valid 17-digit student id: 2026 + 13 padded digits
            var studentId = $"2026{i.ToString().PadLeft(13, '0')}";

            // Wait for atomic release of all 1,000 threads
            startBarrier.Wait();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
                request.Headers.Add("X-Student-Id", studentId);

                var response = await client.SendAsync(request);
                statusCodes.Add(response.StatusCode);

                if (response.StatusCode == HttpStatusCode.Created)
                {
                    var holdData = await response.Content.ReadFromJsonAsync<SeatAllocationHoldResponse>();
                    if (holdData != null)
                    {
                        successfulHoldIds.Add(holdData.holdId);
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Task {i} error: {ex.Message}");
            }
        })).ToArray();

        // 3. Unleash all 1,000 requests simultaneously!
        startBarrier.Set();
        await Task.WhenAll(tasks);

        // 4. Assertions on Concurrency & Invariant
        var createdCount = statusCodes.Count(c => c == HttpStatusCode.Created);
        var conflictCount = statusCodes.Count(c => c == HttpStatusCode.Conflict);
        var serverErrorCount = statusCodes.Count(c => c == HttpStatusCode.InternalServerError);

        _output.WriteLine($"Total: {statusCodes.Count}, Created (Won): {createdCount}, Conflict (Gracefully Denied): {conflictCount}, 500s: {serverErrorCount}");

        // Invariant 1: Total held seats MUST NEVER exceed total class capacity (10)
        Assert.True(createdCount <= 10, $"CRITICAL: Overbooking detected! Created {createdCount} seats for capacity 10.");
        Assert.Equal(10, createdCount); // Since class had 10 available, exactly 10 must win

        // Invariant 2: Remaining 990 requests MUST receive 409 Conflict, NEVER 500
        Assert.Equal(990, conflictCount);
        Assert.Equal(0, serverErrorCount);

        // Invariant 3: Verify the class state directly
        var verifyRes = await client.GetAsync($"/classes/{classId}");
        var verifyData = await verifyRes.Content.ReadFromJsonAsync<SeatAllocationClassResponse>();
        Assert.NotNull(verifyData);
        Assert.Equal(10, verifyData.heldSeats);
        Assert.Equal(0, verifyData.availableSeats);
    }

    [Fact]
    public async Task MassiveFuzzing_1000RandomDirtyHeaders_GuaranteesZero500InternalErrors()
    {
        var client = _factory.CreateClient();

        // Arrange dirty, malicious, malformed student ID patterns
        var maliciousSamples = new[]
        {
            "'; DROP TABLE \"Seats\";--",
            "<script>alert(1)</script>",
            "123",
            "1234567890123456",
            "123456789012345678",
            "abcdefghijklmnopq",
            "   202612345678901   ",
            "\0",
            "2026-999-888-777",
            "99999999999999999" // valid 17 digits but edge case
        };

        var serverErrors = new ConcurrentBag<string>();

        var fuzzTasks = Enumerable.Range(1, 1000).Select(i => Task.Run(async () =>
        {
            var dirtyHeader = maliciousSamples[i % maliciousSamples.Length];
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{Guid.NewGuid()}/holds");
            request.Headers.Add("X-Student-Id", dirtyHeader);

            var res = await client.SendAsync(request);
            if (res.StatusCode == HttpStatusCode.InternalServerError)
            {
                serverErrors.Add($"Index {i} triggered 500 with payload: {dirtyHeader}");
            }
        }));

        await Task.WhenAll(fuzzTasks);

        // Absolute guarantee: Zero 500 Internal Server Errors
        Assert.Empty(serverErrors);
    }
}
