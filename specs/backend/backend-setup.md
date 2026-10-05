# Backend Initial Setup

## Purpose

Create the initial .NET backend foundation for the CarDrone project.

The backend will eventually expose drone control functionality to the React frontend and later communicate with the Raspberry Pi.

This task only creates the backend foundation.

---

## Technology

Use:

- .NET 10
- ASP.NET Core Web API
- C#

The backend must be able to run on:

- Windows
- Linux

Do not introduce operating-system-specific code.

---

## Location

Create the backend inside:

```text
CarDrone/backend/
```

Expected structure:

```text
backend/
├── DroneControl.sln
└── src/
    ├── DroneControl.Api/
    ├── DroneControl.Application/
    ├── DroneControl.Domain/
    └── DroneControl.Infrastructure/
```

---

## Project Responsibilities

### DroneControl.Api

Responsibilities:

- ASP.NET Core entry point
- HTTP endpoints
- dependency injection
- configuration
- middleware
- health endpoint
- OpenAPI
- CORS

The API project must not contain hardware-specific implementation.

---

### DroneControl.Application

Responsibilities:

- application services
- use cases
- orchestration
- service contracts

The Application layer may depend on Domain.

It must not directly access GPIO, Raspberry Pi, camera, or operating-system APIs.

---

### DroneControl.Domain

Responsibilities:

- core domain concepts
- domain rules
- enums
- value objects
- entities when required

The Domain project must not depend on:

- ASP.NET Core
- Infrastructure
- GPIO libraries
- Raspberry Pi libraries
- Docker

---

### DroneControl.Infrastructure

Responsibilities:

Future integrations such as:

- external services
- Raspberry Pi communication
- GPIO
- camera infrastructure

For this task, do not implement any hardware integrations.

---

## Project References

Use a dependency direction equivalent to:

```text
DroneControl.Api
      ↓
DroneControl.Application
      ↓
DroneControl.Domain
```

The API may also reference:

```text
DroneControl.Infrastructure
```

Infrastructure may reference:

```text
DroneControl.Application
DroneControl.Domain
```

Domain must remain independent.

---

## Health Endpoint

Add:

```http
GET /api/health
```

Expected successful response:

```json
{
  "status": "ok"
}
```

The endpoint should return HTTP 200.

---

## OpenAPI

Enable the built-in ASP.NET Core OpenAPI support in development.

Do not add unnecessary third-party packages if the selected .NET version already provides the required capability.

---

## CORS

Configure development CORS.

The React frontend origin must be configurable.

Do not hardcode production origins.

For local development, support a frontend such as:

```text
http://localhost:5173
```

Prefer configuration over constants where practical.

---

## Configuration

Use standard ASP.NET Core configuration.

Expected files include:

```text
appsettings.json
appsettings.Development.json
```

Do not add Raspberry Pi or GPIO configuration yet.

---

## Logging

Use standard ASP.NET Core logging.

Do not add Serilog, NLog, or another external logging framework during this task.

---

## Cross-Platform Requirements

The backend must build and run on:

```text
Windows
Linux
```

Do not use:

- Windows-specific APIs
- Linux-specific APIs
- shell-specific runtime logic
- Raspberry Pi-specific libraries
- GPIO dependencies

---

## Out of Scope

Do not implement:

- Docker
- Docker Compose
- Raspberry Pi communication
- GPIO
- PWM
- uStreamer
- real camera access
- real drone commands
- SignalR
- authentication
- database
- Entity Framework Core

---

## Validation

Run:

```bash
dotnet restore
dotnet build
```

If test projects exist:

```bash
dotnet test
```

Run the API and verify:

```text
GET /api/health
```

returns HTTP 200 and:

```json
{
  "status": "ok"
}
```

---

## Acceptance Criteria

- [x] `backend/DroneControl.sln` exists.
- [x] `DroneControl.Api` exists.
- [x] `DroneControl.Application` exists.
- [x] `DroneControl.Domain` exists.
- [x] `DroneControl.Infrastructure` exists.
- [x] Project references follow the specified dependency direction.
- [x] Domain does not depend on Infrastructure.
- [x] Domain does not depend on ASP.NET Core.
- [x] API starts successfully.
- [x] `/api/health` returns HTTP 200.
- [x] `/api/health` returns `{ "status": "ok" }`.
- [x] OpenAPI is available in development.
- [x] Development CORS is configured.
- [x] Standard configuration files exist.
- [x] Standard ASP.NET Core logging is used.
- [x] `dotnet restore` succeeds.
- [x] `dotnet build` succeeds.
- [x] No Docker configuration has been introduced.
- [x] No Raspberry Pi integration exists.
- [x] No GPIO or PWM integration exists.
- [x] Backend remains cross-platform for Windows and Linux.
