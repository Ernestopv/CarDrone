# Keyboard Controls

## Purpose

Allow the user to control the simulated drone using the keyboard while preserving the existing visual controls and simulator architecture.

Keyboard input must behave as another input method for the existing drone command flow.

---

## Dependencies

This specification depends on:

```text
specs/frontend/drone-simulator.md
specs/frontend/drone-controls.md
```

If `drone-controls.md` does not exist yet, reuse the existing control behavior implemented in the frontend.

Keyboard controls must use the existing `DroneService` flow.

Do not create a second command system.

---

## Scope

Support these keyboard mappings:

```text
W           → forward
S           → backward
A           → left
D           → right
ArrowUp     → forward
ArrowDown   → backward
ArrowLeft   → left
ArrowRight  → right
Space       → stop
```

Arrow keys are aliases for the same four movement commands. They do not
introduce new commands, new state, or a second command path.

Keyboard input must update the same state and telemetry as the visual controls.

---

## Out of Scope

Do not implement:

- gamepad support;
- joystick support;
- mobile touch gestures;
- configurable key bindings;
- real backend commands;
- GPIO;
- PWM;
- Raspberry Pi communication;
- uStreamer;
- global operating-system hotkeys.

---

## Architecture

Expected command flow:

```text
Keyboard
   ↓
Keyboard control handler
   ↓
DroneService
   ↓
MockDroneService
   ↓
Drone state
   ↓
UI / Telemetry
```

Keyboard handlers must not directly modify confirmed drone state.

---

## Supported Keys

### Forward

```text
W
ArrowUp
```

Command:

```text
forward
```

### Backward

```text
S
ArrowDown
```

Command:

```text
backward
```

### Left

```text
A
ArrowLeft
```

Command:

```text
left
```

### Right

```text
D
ArrowRight
```

Command:

```text
right
```

### Stop

```text
Space
```

Command:

```text
stop
```

Letter keys should work regardless of uppercase/lowercase state.
Arrow keys are matched by their standard `ArrowUp` / `ArrowDown` /
`ArrowLeft` / `ArrowRight` key values.

---

## Connection Rules

When the drone is disconnected:

- movement keyboard commands must not be treated as successful;
- behavior must remain consistent with visual controls;
- `Space` may still invoke the local STOP behavior if supported by the existing simulator.

Do not bypass existing connection validation.

---

## Input Safety

Keyboard controls must not trigger when the user is typing inside editable elements.

Ignore keyboard drone commands when focus is inside:

```text
input
textarea
select
contenteditable
```

This prevents accidental drone commands while interacting with forms.

Arrow keys must keep their native behaviour inside these elements — for
example the speed slider and any `select` menu must still respond to
arrow keys normally.

---

## Repeated Key Events

Browser keyboard events may repeat while a key is held down.

Avoid sending unnecessary repeated commands if the same command is already active.

Do not create a complex command queue.

A simple guard against redundant repeated commands is sufficient.

---

## Space and Arrow Key Scrolling

When using:

```text
Space
```

to stop the drone, prevent unwanted page scrolling only when the keyboard control is actually being handled.

The same rule applies to arrow keys: they scroll the page by default, so
prevent their default behaviour only when they are actually being handled
as movement commands.

Do not globally disable normal browser behavior unnecessarily.

---

## Visual Synchronization

Keyboard commands must use the same command system as the visual buttons.

Example:

```text
User presses W
↓
requestedCommand = forward
↓
confirmedCommand = forward
↓
Telemetry shows FORWARD
```

Visual controls may optionally reflect the active command if the existing component supports that behavior.

Do not duplicate command state solely for keyboard UI feedback.

---

## Hook / Integration

Prefer a small reusable integration such as:

```text
useDroneKeyboardControls
```

Suggested location:

```text
src/features/drone-control/useDroneKeyboardControls.ts
```

or another location consistent with the existing project structure.

The hook should:

- register keyboard listeners;
- clean them up correctly;
- translate keys into semantic commands;
- call the existing command handler/service.

Do not put large keyboard event logic directly inside `App.tsx`.

---

## Lifecycle

Keyboard listeners must be removed when the relevant React component unmounts.

Avoid registering duplicate listeners during re-renders.

---

## Accessibility

Visual drone controls must remain available.

Keyboard controls are an additional input method, not a replacement.

If appropriate, show a small keyboard hint such as:

```text
W / A / S / D or Arrow keys — Move
Space — Stop
```

Keep it visually secondary.

---

## Cross-Platform Requirements

Keyboard controls must work in modern browsers on:

```text
Windows
Linux
```

Do not depend on operating-system-specific APIs.

Use standard browser keyboard events.

---

## Error Handling

If a command is rejected by the simulator, the keyboard layer must not fake a successful state.

Reuse existing error/command handling.

Do not swallow meaningful errors silently.

---

## Testing Scenarios

### Scenario 1 — Forward

Given the simulated drone is connected.

Press:

```text
W
```

Expected:

```text
requestedCommand = forward
confirmedCommand = forward
```

---

### Scenario 2 — Left

Press:

```text
A
```

Expected:

```text
requestedCommand = left
confirmedCommand = left
```

---

### Scenario 3 — Stop

Press:

```text
Space
```

Expected:

```text
requestedCommand = stop
confirmedCommand = stop
```

---

### Scenario 4 — Disconnected

Given the drone is disconnected.

Press:

```text
W
```

Expected:

Movement must not be reported as successfully executed.

---

### Scenario 5 — Form Input

Place focus inside an input field.

Press:

```text
W
```

Expected:

The character is handled by the input.

No drone movement command is sent.

---

### Scenario 6 — Key Hold

Hold:

```text
W
```

Expected:

The application does not generate unnecessary duplicate command operations.

---

### Scenario 7 — Arrow Keys

Given the simulated drone is connected.

Press:

```text
ArrowUp
```

Expected:

```text
requestedCommand = forward
confirmedCommand = forward
```

Press:

```text
ArrowRight
```

Expected:

```text
requestedCommand = right
confirmedCommand = right
```

---

### Scenario 8 — Arrow Keys Inside a Form Control

Place focus inside the speed slider (an `input` element).

Press:

```text
ArrowUp
```

Expected:

The slider handles the arrow key.

No drone movement command is sent.

---

## Acceptance Criteria

- [x] `W` triggers `forward`.
- [x] `S` triggers `backward`.
- [x] `A` triggers `left`.
- [x] `D` triggers `right`.
- [x] `Space` triggers `stop`.
- [x] `ArrowUp` triggers `forward`.
- [x] `ArrowDown` triggers `backward`.
- [x] `ArrowLeft` triggers `left`.
- [x] `ArrowRight` triggers `right`.
- [x] Uppercase/lowercase behavior is handled correctly.
- [x] Keyboard commands use the existing `DroneService`.
- [x] Keyboard input does not directly modify confirmed state.
- [x] Visual and keyboard controls share the same command flow.
- [x] Movement commands respect disconnected state.
- [x] Keyboard shortcuts are ignored inside editable elements.
- [x] Arrow keys keep their native behaviour inside editable elements.
- [x] Space does not cause unwanted scrolling when used as STOP.
- [x] Arrow keys do not cause unwanted scrolling when used as movement controls.
- [x] Keyboard listeners are cleaned up correctly.
- [x] Duplicate listeners are not introduced.
- [x] Unnecessary repeated commands are avoided.
- [x] Visual controls remain usable.
- [x] Implementation works in browsers on Windows and Linux.
- [x] Build passes.
- [x] Lint passes.
- [x] No backend or hardware integration is introduced.