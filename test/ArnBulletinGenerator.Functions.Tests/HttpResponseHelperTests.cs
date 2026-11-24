using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Functions.Contracts;
using Shouldly;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class HttpResponseHelperTests
{
    [Fact]
    public async Task OkJsonAsync_Returns200WithJson()
    {
        var req = MockHttpRequestData.CreateGet();
        var response = await HttpResponseHelper.OkJsonAsync(req, new PayloadDto("x", 1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        var dto = JsonSerializer.Deserialize<PayloadDto>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        dto!.Name.ShouldBe("x");
        dto.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AcceptedJsonAsync_Returns202WithJson()
    {
        var req = MockHttpRequestData.CreateGet();
        var response = await HttpResponseHelper.AcceptedJsonAsync(req, new PayloadDto("y", 2));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        body.ShouldContain("\"Name\":\"y\"");
    }

    [Fact]
    public async Task BadRequestAsync_Returns400WithError()
    {
        var req = MockHttpRequestData.CreateGet();
        var response = await HttpResponseHelper.BadRequestAsync(req, "bad input", "detail here");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        var err = JsonSerializer.Deserialize<ErrorResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        err!.Error.ShouldBe("bad input");
        err.Detail.ShouldBe("detail here");
        err.Status.ShouldBe(400);
    }

    [Fact]
    public async Task NotFoundAsync_Returns404WithError()
    {
        var req = MockHttpRequestData.CreateGet();
        var response = await HttpResponseHelper.NotFoundAsync(req, "not found");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        body.ShouldContain("not found");
    }

    [Fact]
    public async Task ConflictAsync_Returns409WithError()
    {
        var req = MockHttpRequestData.CreateGet();
        var response = await HttpResponseHelper.ConflictAsync(req, "conflict");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        body.ShouldContain("conflict");
    }

    [Fact]
    public async Task InternalErrorAsync_Returns500WithError()
    {
        var req = MockHttpRequestData.CreateGet();
        var response = await HttpResponseHelper.InternalErrorAsync(req, "server error", "stack trace");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        var err = JsonSerializer.Deserialize<ErrorResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        err!.Error.ShouldBe("server error");
        err.Detail.ShouldBe("stack trace");
        err.Status.ShouldBe(500);
    }

    [Fact]
    public void NoContent_Returns204()
    {
        var req = MockHttpRequestData.CreateGet();
        var response = HttpResponseHelper.NoContent(req);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private sealed record PayloadDto(string Name, int Count);
}
