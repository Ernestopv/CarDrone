using System.Net;
using System.Net.Http.Json;
using DroneControl.Application;
using DroneControl.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DroneControl.Api.Tests;

/// <summary>
/// Scriptable controller used to drive the Task 16 error mappings over real
/// HTTP: the simulator can never produce 503 or unexpected faults, so this
/// fake is the permanent replacement for the throwaway harness (spec:
/// specs/backend/error-handling.md + specs/backend/backend-tests.md).
/// </summary>
public sealed class ScriptedDroneController : IDroneController
{
    public Exception? ThrowOnSend { get; set; }

    public Task<DroneStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new DroneStatus
        {
            State = new DroneState { Connection = ConnectionStatus.Connected },
            RaspberryPi = ConnectionStatus.Connected,
        });

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendCommandAsync(DroneCommand command, CancellationToken cancellationToken = default)
        => ThrowOnSend is { } ex
            ? Task.FromException(ex)
            : Task.CompletedTask;

    public Task SetSpeedAsync(int speed, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class ErrorMappingFactory : WebApplicationFactory<Program>
{
    public ScriptedDroneController Controller { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Last registration wins, so the scripted controller replaces the
        // real singleton for this fixture's host only.
        builder.ConfigureServices(services =>
            services.AddSingleton<IDroneController>(Controller));
    }
}

public class ErrorMappingTests : IClassFixture<ErrorMappingFactory>
{
    private readonly ErrorMappingFactory _factory;

    public ErrorMappingTests(ErrorMappingFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpStatusCode StatusCode, string ContentType, string Body)> SendCommandAsync()
    {
        var response = await _factory.CreateClient().PostAsync(
            "/api/drone/command",
            new StringContent("""{"command":"forward"}""", System.Text.Encoding.UTF8, "application/json"));
        return (
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType ?? string.Empty,
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NotConnected_MapsTo409ProblemJson()
    {
        _factory.Controller.ThrowOnSend = new DroneNotConnectedException();

        var (status, contentType, body) = await SendCommandAsync();

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("application/problem+json", contentType);
        Assert.Contains("\"status\":409", body);
        Assert.Contains("not connected", body);
        Assert.DoesNotContain("stackTrace", body);
    }

    [Fact]
    public async Task Unavailable_MapsTo503ProblemJson()
    {
        _factory.Controller.ThrowOnSend = new DroneUnavailableException();

        var (status, contentType, body) = await SendCommandAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("application/problem+json", contentType);
        Assert.Contains("\"status\":503", body);
        Assert.Contains("unavailable", body);
    }

    [Fact]
    public async Task Unexpected_MapsToGeneric500_AndLeaksNothing()
    {
        _factory.Controller.ThrowOnSend =
            new InvalidOperationException("internal-detail-MARKER-4f2a should never reach clients");

        var (status, contentType, body) = await SendCommandAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal("application/problem+json", contentType);
        Assert.Contains("\"status\":500", body);
        Assert.DoesNotContain("internal-detail-MARKER-4f2a", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("stackTrace", body);
    }

    [Fact]
    public async Task Command_OnHealthyController_Returns200()
    {
        _factory.Controller.ThrowOnSend = null;

        var response = await _factory.CreateClient().PostAsync(
            "/api/drone/command",
            new StringContent("""{"command":"forward"}""", System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"connection\":\"connected\"", body);
    }
}
