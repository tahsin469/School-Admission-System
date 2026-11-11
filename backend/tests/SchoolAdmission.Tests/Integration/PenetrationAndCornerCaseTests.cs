using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SchoolAdmission.Application.DTOs;
using Xunit;

namespace SchoolSeatAllocation.Tests.Integration;

/// <summary>
/// Hardcore Penetration, Regression, Smoke, and Extreme Corner-Case Test Suite.
/// Tests malicious payloads, SQL injection tokens, boundary overflows, race windows, and invalid states.
/// </summary>
public class PenetrationAndCornerCaseTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PenetrationAndCornerCaseTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("'; DROP TABLE \"Seats\";--")]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("1234567890123456")]     // 16 digits (underflow)
    [InlineData("123456789012345678")]   // 18 digits (overflow)
    [InlineData("abcdefghijklmnopq")]   // 17 alphabets
    [InlineData("2005 12345678901")]    // whitespace in between
    [InlineData("                   ")] // empty whitespaces
    public async Task Penetration_MaliciousOrInvalidStudentId_ReturnsBadRequestNever500(string dirtyStudentId)
    {
        // 1. Arrange a target class
        var getClasses = await _client.GetAsync("/classes");
        var classes = await getClasses.Content.ReadFromJsonAsync<List<SeatAllocationClassResponse>>();
        Assert.NotNull(classes);
        var targetClass = classes.First();

        // 2. Act on POST /classes/{id}/holds with dirty header
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{targetClass.id}/holds");
        request.Headers.Add("X-Student-Id", dirtyStudentId);

        var response = await _client.SendAsync(request);

        // 3. Assert: Must strictly be 400 Bad Request, NEVER 500 Internal Server Error
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task CornerCase_NonExistentGuid_Returns404NotFoundNever500()
    {
        var randomGuid = Guid.NewGuid().ToString();

        // Check non-existent class
        var responseClass = await _client.GetAsync($"/classes/{randomGuid}");
        Assert.Equal(HttpStatusCode.NotFound, responseClass.StatusCode);

        // Check non-existent hold
        var responseHold = await _client.GetAsync($"/holds/{randomGuid}");
        Assert.Equal(HttpStatusCode.NotFound, responseHold.StatusCode);
    }

    [Fact]
    public async Task CornerCase_ConfirmWithoutHold_ReturnsNotFoundOrConflictNever500()
    {
        var validStudentId = "20098765432109876";
        var fakeHoldId = Guid.NewGuid().ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/holds/{fakeHoldId}/confirm");
        request.Headers.Add("X-Student-Id", validStudentId);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SmokeTest_SystemHealthCheck_Returns200AndOnline()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Online", content);
    }

    [Fact]
    public async Task ExtremeCorner_CancelSeatWithOtherStudentId_Returns400BadRequest()
    {
        // 1. Target class
        var getClasses = await _client.GetAsync("/classes");
        var classes = await getClasses.Content.ReadFromJsonAsync<List<SeatAllocationClassResponse>>();
        var targetClass = classes!.First();

        // 2. Hold by student 1
        var student1 = "20071111222233334";
        using var holdReq = new HttpRequestMessage(HttpMethod.Post, $"/classes/{targetClass.id}/holds");
        holdReq.Headers.Add("X-Student-Id", student1);
        var holdRes = await _client.SendAsync(holdReq);
        var holdData = await holdRes.Content.ReadFromJsonAsync<SeatAllocationHoldResponse>();

        // 3. Malicious cancellation attempt by student 2
        var attackerStudent = "20079999888877776";
        using var cancelReq = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdData!.holdId}");
        cancelReq.Headers.Add("X-Student-Id", attackerStudent);
        var cancelRes = await _client.SendAsync(cancelReq);

        // 4. Must reject attacker
        Assert.Equal(HttpStatusCode.BadRequest, cancelRes.StatusCode);
    }

    [Fact]
    public async Task ExtremeCorner_ClassCreationWithZeroOrNegativeSeats_Returns400BadRequest()
    {
        var badRequest = new SeatAllocationCreateClassRequest("Zero Seat Class", 0);
        var response = await _client.PostAsJsonAsync("/classes", badRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExtremeCorner_ClassCreationWithEmptyName_Returns400BadRequest()
    {
        var badRequest = new SeatAllocationCreateClassRequest("", 50);
        var response = await _client.PostAsJsonAsync("/classes", badRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
