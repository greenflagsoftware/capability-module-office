# ModuleName Development Plan

Status: living document. Update as phases complete or the plan changes — this is not a
one-time artifact.

## What this module does

One paragraph: the capability this module adds to a VTC team, and who/what within VTC
consumes it (which persona, which existing tool group it complements).

## Scope

- In scope for v0.1:
- Explicitly deferred:

## Architecture notes specific to this module

Most of the shape is already decided at the AgentDock level (see the architecture reference
below) — only note what's specific to *this* module here: external services it talks to,
credentials/config it needs, whether it's stateless or backs onto a stateful service, and any
higher-risk capabilities (shell exec, sending messages, financial actions) that need the
confirmation/scoping treatment called out in the AgentDock architecture decisions.

## Phase Plan

### Phase 0 — Prove the loop

- Deliverable: the MCP HTTP endpoint responds and VTC can connect to it and call the
  placeholder `echo` tool in [ExampleTool.cs](../src/AgentDock.ModuleName/Tools/ExampleTool.cs).
- Exit criteria: `docker compose up` starts the module, `/health` returns healthy, and a
  manual MCP client call to `echo` round-trips.

### Phase 1 — First real tool

- Deliverable: replace `ExampleTool` with the module's actual first capability.
- Update [module.manifest.json](../src/AgentDock.ModuleName/module.manifest.json) to describe
  the real tool set instead of the placeholder.
- Exit criteria: VTC can call the real tool end to end through a persona.

### Phase 2 — Harden

- Error handling for the failure modes specific to this module's external dependencies.
- Resource limits / timeouts appropriate to what the module does.
- Exit criteria: the module fails predictably (clear error surfaced to the calling persona)
  rather than hanging or crashing the sidecar on bad input.

## Reference

- AgentDock module architecture decisions (sidecar-per-module, MCP over HTTP, entitlement via
  running container, module contract/manifest shape): tracked separately at the VTC/AgentDock
  level, not duplicated here.
