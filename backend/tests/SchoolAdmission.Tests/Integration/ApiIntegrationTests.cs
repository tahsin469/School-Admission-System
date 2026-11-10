using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SchoolAdmission.Application.Common;
using SchoolAdmission.Application.DTOs;
using SchoolAdmission.Domain.Entities;
using Xunit;

namespace SchoolAdmission.Tests.Integration;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
        }).CreateClient();
    }

    [Fact]
    public async Task GetClasses_ReturnsSuccessAndSeedData()
    {
        // Act
        var response = await _client.GetAsync("/api/classes");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<ClassDto>>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotEmpty(result.Data!);
    }

    [Fact]
    public async Task HoldSeat_WithInvalidStudentId_ReturnsBadRequestProblemDetails()
    {
        // Arrange
        var getClassesResponse = await _client.GetAsync("/api/classes");
        var result = await getClassesResponse.Content.ReadFromJsonAsync<ApiResponse<List<ClassDto>>>();
        var classId = result!.Data!.First().Id;

        var invalidRequest = new HoldSeatRequest("12345"); // Invalid birth registration length

        // Act
        var response = await _client.PostAsJsonAsync($"/api/classes/{classId}/hold", invalidRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task HoldAndConfirmSeat_FullLifecycle_Succeeds()
    {
        // Arrange
        var getClassesResponse = await _client.GetAsync("/api/classes");
        var result = await getClassesResponse.Content.ReadFromJsonAsync<ApiResponse<List<ClassDto>>>();
        var targetClass = result!.Data!.First(c => c.AvailableSeats > 0);
        var studentId = "20059876543210987"; // Valid 17 digits

        // 1. Hold Seat
        var holdResponse = await _client.PostAsJsonAsync(
            $"/api/classes/{targetClass.Id}/hold",
            new HoldSeatRequest(studentId));

        Assert.Equal(HttpStatusCode.OK, holdResponse.StatusCode);
        var holdResult = await holdResponse.Content.ReadFromJsonAsync<ApiResponse<SeatDto>>();
        Assert.NotNull(holdResult);
        Assert.True(holdResult.Success);
        Assert.Equal(SeatStatus.Held, holdResult.Data!.Status);

        // 2. Confirm Seat
        var confirmResponse = await _client.PostAsJsonAsync(
            $"/api/classes/{targetClass.Id}/confirm",
            new ConfirmSeatRequest(studentId));

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmResult = await confirmResponse.Content.ReadFromJsonAsync<ApiResponse<SeatDto>>();
        Assert.NotNull(confirmResult);
        Assert.True(confirmResult.Success);
        Assert.Equal(SeatStatus.Confirmed, confirmResult.Data!.Status);
    }
}
