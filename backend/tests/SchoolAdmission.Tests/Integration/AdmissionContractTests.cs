using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SchoolAdmission.Application.DTOs;
using Xunit;

namespace SchoolAdmission.Tests.Integration;

public class AdmissionContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public AdmissionContractTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
        }).CreateClient();
    }

    [Fact]
    public async Task Spec_FullLifecycle_MeetsAll8Endpoints()
    {
        var studentId = "20081234567890123";

        // 1. POST /classes
        var createClassResponse = await _client.PostAsJsonAsync("/classes", new AdmissionCreateClassRequest(
            "Class 6 - Morning Shift",
            120
        ));
        Assert.Equal(HttpStatusCode.Created, createClassResponse.StatusCode);
        var created = await createClassResponse.Content.ReadFromJsonAsync<AdmissionClassResponse>();
        Assert.NotNull(created);
        var classId = created.id;

        // 2. GET /classes
        var getClassesResponse = await _client.GetAsync("/classes");
        Assert.Equal(HttpStatusCode.OK, getClassesResponse.StatusCode);
        var classesList = await getClassesResponse.Content.ReadFromJsonAsync<List<AdmissionClassResponse>>();
        Assert.NotNull(classesList);
        Assert.Contains(classesList, c => c.id == classId);

        // 3. GET /classes/{classId}
        var getClassResponse = await _client.GetAsync($"/classes/{classId}");
        Assert.Equal(HttpStatusCode.OK, getClassResponse.StatusCode);

        // 4. POST /classes/{classId}/holds (with X-Student-Id header)
        using var holdReq = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        holdReq.Headers.Add("X-Student-Id", studentId);
        var holdResponse = await _client.SendAsync(holdReq);
        Assert.Equal(HttpStatusCode.Created, holdResponse.StatusCode);
        var holdData = await holdResponse.Content.ReadFromJsonAsync<AdmissionHoldResponse>();
        Assert.NotNull(holdData);
        var holdId = holdData.holdId;
        Assert.Equal("HELD", holdData.status);

        // 5. GET /holds/{holdId}
        var getHoldResponse = await _client.GetAsync($"/holds/{holdId}");
        Assert.Equal(HttpStatusCode.OK, getHoldResponse.StatusCode);
        var holdStatusData = await getHoldResponse.Content.ReadFromJsonAsync<AdmissionHoldStatusResponse>();
        Assert.NotNull(holdStatusData);
        Assert.Equal("HELD", holdStatusData.status);

        // 8. GET /me/holds (with X-Student-Id header)
        using var myHoldsReq = new HttpRequestMessage(HttpMethod.Get, "/me/holds");
        myHoldsReq.Headers.Add("X-Student-Id", studentId);
        var myHoldsResponse = await _client.SendAsync(myHoldsReq);
        Assert.Equal(HttpStatusCode.OK, myHoldsResponse.StatusCode);
        var myHolds = await myHoldsResponse.Content.ReadFromJsonAsync<List<AdmissionMyHoldItem>>();
        Assert.NotNull(myHolds);
        Assert.Contains(myHolds, h => h.holdId == holdId);

        // 6. POST /holds/{holdId}/confirm (with X-Student-Id header)
        using var confirmReq = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        confirmReq.Headers.Add("X-Student-Id", studentId);
        var confirmResponse = await _client.SendAsync(confirmReq);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmData = await confirmResponse.Content.ReadFromJsonAsync<AdmissionConfirmResponse>();
        Assert.NotNull(confirmData);
        Assert.Equal("CONFIRMED", confirmData.status);

        // 7. DELETE /holds/{holdId} (Test on new hold -> 204 NoContent)
        var studentId2 = "20089999999999999";
        using var holdReq2 = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        holdReq2.Headers.Add("X-Student-Id", studentId2);
        var holdResponse2 = await _client.SendAsync(holdReq2);
        var holdData2 = await holdResponse2.Content.ReadFromJsonAsync<AdmissionHoldResponse>();

        using var deleteReq = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdData2!.holdId}");
        deleteReq.Headers.Add("X-Student-Id", studentId2);
        var deleteResponse = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }
}
