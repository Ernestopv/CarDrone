# Drone Simulator

## Purpose

Provide a frontend-only simulation layer that allows the application to behave as if a drone were connected.

This simulator is temporary and will later be replaced by the real .NET backend.

---

## Scope

Implement:

- simulated drone connection;
- simulated Raspberry Pi status;
- simulated API status;
- simulated camera status;
- movement commands;
- speed control;
- requested command;
- confirmed command;
- connect and disconnect behavior.

---

## Out of Scope

Do not implement:

- .NET;
- ASP.NET Core;
- HTTP APIs;
- SignalR;
- WebSockets;
- Raspberry Pi communication;
- GPIO;
- PWM;
- uStreamer;
- real camera streaming;
- physical motor control.

---

## Architecture

The frontend must communicate through a service abstraction.

```text id="y0rxhg"
React UI
   ↓
DroneService
   ↓
MockDroneService
```

Presentation components should not contain hardware simulation logic.

---

## Connection States

Supported states:

```text id="rvqqne"
offline
connecting
connected
error
```

Normal connection flow:

```text id="l4em3p"
offline
↓
connecting
↓
connected
```

Disconnect flow:

```text id="wyo0i8"
connected
↓
offline
```

---

## Camera States

Supported states:

```text id="q74fob"
offline
connecting
streaming
error
```

When the drone is disconnected:

```text id="wgzlny"
camera = offline
```

When the simulated connection succeeds:

```text id="5iazf4"
camera = streaming
```

---

## Commands

Supported commands:

```text id="fneqeg"
forward
backward
left
right
stop
```

Commands must be processed through `DroneService`.

The UI must not directly set the confirmed command.

---

## Command Lifecycle

The application must distinguish between:

```text id="k6nt2h"
requestedCommand
confirmedCommand
```

Example:

```text id="bdtt5f"
User presses FORWARD
↓
requestedCommand = forward
↓
MockDroneService processes command
↓
confirmedCommand = forward
```

A short simulated acknowledgement delay may be used.

---

## Speed

Supported range:

```text id="5tcx4f"
0 - 100
```

Speed changes must go through the service abstraction.

Invalid values outside this range must be rejected or safely clamped according to the existing project conventions.

---

## Service Contract

Create or adapt an interface equivalent to:

```ts id="vcn0g2"
export interface DroneService {
  getState(): Promise<DroneState>;
  connect(): Promise<void>;
  disconnect(): Promise<void>;
  sendCommand(command: DroneCommand): Promise<void>;
  setSpeed(speed: number): Promise<void>;
}
```

The implementation may adapt this contract if a cleaner existing architecture already exists.

---

## State Model

The simulator state should contain at least:

```ts id="h2mi99"
export interface DroneState {
  connection: ConnectionStatus;
  raspberryPi: ConnectionStatus;
  api: ConnectionStatus;
  camera: CameraStatus;
  requestedCommand: DroneCommand;
  confirmedCommand: DroneCommand;
  speed: number;
}
```

Reuse existing types where possible.

Do not duplicate domain models unnecessarily.

The runtime state must always conform to this model. If an incomplete or older-shaped state is encountered (for example after a development hot-reload), normalize it before use; missing commands must default to `stop`.

---

## UI Integration

The dashboard must provide simulated controls for:

```text id="cw2akn"
Connect
Disconnect
```

When disconnected:

- movement controls must not report successful execution;
- movement controls may be disabled;
- camera must be offline.

When connected:

- movement controls become available;
- speed can be updated;
- camera displays simulated streaming.

---

## Telemetry

Display at least:

```text id="t95111"
Requested Command
Confirmed Command
Speed
Drone Connection
Camera
```

Example (shown after `DroneService` processes the request):

```text id="41annr"
Requested: FORWARD
Confirmed: FORWARD
```

`Confirmed` is displayed only from the service state; the UI never sets it
directly.
