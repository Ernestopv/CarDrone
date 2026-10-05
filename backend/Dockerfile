# Stage 1: build inside the official .NET SDK image. The four project files
# are copied before the source so the NuGet restore layer stays cached across
# source edits (specs/docker/backend-container.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/DroneControl.Api/DroneControl.Api.csproj", "src/DroneControl.Api/"]
COPY ["src/DroneControl.Application/DroneControl.Application.csproj", "src/DroneControl.Application/"]
COPY ["src/DroneControl.Domain/DroneControl.Domain.csproj", "src/DroneControl.Domain/"]
COPY ["src/DroneControl.Infrastructure/DroneControl.Infrastructure.csproj", "src/DroneControl.Infrastructure/"]
RUN dotnet restore "src/DroneControl.Api/DroneControl.Api.csproj"

COPY . .
RUN dotnet publish "src/DroneControl.Api/DroneControl.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 2: runtime — slim ASP.NET image only (never the SDK). Framework-
# dependent publish with no runtime identifier pinned, so the same Dockerfile
# builds for linux/amd64 and linux/arm64 in Task 15. No HEALTHCHECK: the
# aspnet image ships no probe tooling; probe orchestration against
# GET /api/health belongs to the Compose task (Task 14).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# libgpiod is the proven GPIO userspace library on the target — the same one
# the `gpioset` tool uses. The real-mode RaspberryGpioPlatform talks to it via
# P/Invoke (specs/hardware/gpio.md). Installed as root before dropping user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgpiod2 \
    && rm -rf /var/lib/apt/lists/*

# Non-root using the built-in app user of the .NET 8+ container images.
USER app

COPY --from=build /app/publish .

# The ASP.NET container images listen on ASPNETCORE_HTTP_PORTS=8080 by default.
EXPOSE 8080

ENTRYPOINT ["dotnet", "DroneControl.Api.dll"]
