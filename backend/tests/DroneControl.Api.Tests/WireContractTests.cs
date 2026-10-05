using System.Net;
using DroneControl.Domain;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DroneControl.Api.Tests;

/// <summary>
/// In-process host with the real DI graph (singleton MockDroneController).
/// Tests call <see cref="Reset"/> first so the shared simulator state can
/// never leak assumptions across test order.
/// </summary>
public sealed class DroneApiFactory : WebApplicationFactory<Program>
{
    public async Task ResetAsync()
    {
        var response = await CreateClient().PostAsync("/api/drone/disconnect", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public class WireContractTests : IClassFixture<DroneApiFactory>
{
    private readonly DroneApiFactory _factory;

    public WireContractTests(DroneApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Status_IsCamelCaseWithLowercaseEnums_AndApiConnected()
    {
        await _factory.ResetAsync();

        var response = await _factory.CreateClient().GetAsync("/api/drone/status");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"state\":", body);
        Assert.Contains("\"connection\":\"offline\"", body);
        Assert.Contains("\"camera\":\"offline\"", body);
        Assert.Contains("\"requestedCommand\":\"stop\"", body);
        Assert.Contains("\"confirmedCommand\":\"stop\"", body);
        Assert.Contains("\"raspberryPi\":\"offline\"", body);
        Assert.Contains("\"api\":\"connected\"", body);
        Assert.DoesNotContain("\"Offline\"", body);
    }

    [Fact]
    public async Task ConnectThenCommandThenDisconnect_FlowSucceeds()
    {
        await _factory.ResetAsync();
        var client = _factory.CreateClient();

        var connect = await client.PostAsync("/api/drone/connect", null);
        var connectBody = await connect.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, connect.StatusCode);
        Assert.Contains("\"connection\":\"connected\"", connectBody);

        var command = await client.PostAsync(
            "/api/drone/command",
            new StringContent("""{"command":"forward"}""", System.Text.Encoding.UTF8, "application/json"));
        var commandBody = await command.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, command.StatusCode);
        Assert.Contains("\"confirmedCommand\":\"forward\"", commandBody);

        var disconnect = await client.PostAsync("/api/drone/disconnect", null);
        Assert.Equal(HttpStatusCode.OK, disconnect.StatusCode);
    }

    [Fact]
    public async Task Command_MissingField_Returns400WithErrors()
    {
        await _factory.ResetAsync();

        var response = await _factory.CreateClient().PostAsync(
            "/api/drone/command",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", body);
    }

    [Fact]
    public async Task Command_UnknownValue_Returns400WithErrors()
    {
        await _factory.ResetAsync();

        var response = await _factory.CreateClient().PostAsync(
            "/api/drone/command",
            new StringContent("""{"command":"spin"}""", System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", body);
    }

    [Fact]
    public async Task Command_MalformedJson_Returns400()
    {
        await _factory.ResetAsync();

        var response = await _factory.CreateClient().PostAsync(
            "/api/drone/command",
            new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"speed":120}""")]
    [InlineData("""{"speed":-5}""")]
    public async Task Speed_InvalidBodies_Return400WithErrors(string json)
    {
        await _factory.ResetAsync();

        var response = await _factory.CreateClient().PutAsync(
            "/api/drone/speed",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", body);
    }

    [Fact]
    public async Task Speed_WhileDisconnected_Returns200WithSpeedUnchanged()
    {
        await _factory.ResetAsync();

        var response = await _factory.CreateClient().PutAsync(
            "/api/drone/speed",
            new StringContent("""{"speed":50}""", System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"speed\":0", body);
    }

    [Fact]
    public async Task UnknownRoute_Returns404_Not500()
    {
        var response = await _factory.CreateClient().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
