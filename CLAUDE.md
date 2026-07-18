# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`agentdock-office` is the Office capability module for AgentDock — a sidecar service that
exposes Office-document tools to VTC (Virtual Team Chat) over MCP. It was scaffolded from
[`agentdock-module-template`](https://github.com/greenflagsoftware/agentdock-module-template)
and is still at the template stage: `ExampleTool.cs` (an `echo` tool) is a placeholder proving
the MCP endpoint is wired up, not a real capability yet. See [docs/DEV_PLAN.md](docs/DEV_PLAN.md)
for the phase plan — it is a living document, not a fixed spec, and is still mostly unfilled.

## Module architecture (how this fits into AgentDock/VTC)

Each AgentDock module is its own Docker container running an ASP.NET Core MCP server (HTTP
transport, via `ModelContextProtocol.AspNetCore`). VTC connects to entitled modules' MCP
endpoints and merges their tools into its agents' tool list.

- **Entitlement = running container.** A module is "licensed" simply by whether its container
  is running — there is no separate entitlement/license check inside the module itself. Do not
  add one.
- **Stateless HTTP transport.** `Program.cs` sets `options.Stateless = true` deliberately: VTC
  connects fresh per tool call rather than holding a session open, so the sidecar can scale or
  restart independently. Don't introduce server-side session state without revisiting this.
- **Three endpoints, all wired in `Program.cs`:**
  - MCP endpoint (`app.MapMcp()`) — tools are discovered automatically via
    `WithToolsFromAssembly()`, so any `[McpServerToolType]` class in the assembly is picked up
    with no manual registration.
  - `/health` — used by both the Docker `HEALTHCHECK` and VTC as the entitlement signal.
  - `/manifest` — serves `module.manifest.json` verbatim as the declared tool contract.
- **`module.manifest.json` is a manually-maintained duplicate of the tool contract.** It must
  be kept in sync by hand with the actual `[McpServerTool]` methods until a shared contract
  package exists — update its `tools` array whenever tools are added, renamed, or removed.

## Adding a real tool

1. Add a class under `src/AgentDock.Office/Tools/` (one file per tool or tool group),
   `[McpServerToolType]` on the class, `[McpServerTool]` + `[Description(...)]` on each method
   (see `ExampleTool.cs` for the pattern). No manual registration needed — assembly scanning
   picks it up.
2. Update `tools` in [module.manifest.json](src/AgentDock.Office/module.manifest.json) to match.
3. Delete `ExampleTool.cs` once a real tool exists and the MCP endpoint has been confirmed to
   round-trip end to end (per the README's stated next step — don't delete it prematurely if
   nothing has replaced it yet).
4. Add corresponding tests under `tests/AgentDock.Office.Tests/`.

## Commands

```
dotnet build
dotnet test
docker compose up --build
curl http://localhost:8081/health
```

Run a single test:
```
dotnet test --filter "FullyQualifiedName~ExampleToolTests.Echo_ReturnsMessagePrefixedWithOffice"
```

Local docker-compose maps the container's internal port 8080 to host port 8081, and loads
`.env` if present (not committed).

## Layout

```
src/AgentDock.Office/       ASP.NET Core MCP server (the module itself)
  Tools/                        Tool implementations, one file per tool or tool group
  Program.cs                    MCP HTTP transport + /health + /manifest wiring
  module.manifest.json          Declared tool contract (id, name, version, tools)
tests/AgentDock.Office.Tests/
docs/DEV_PLAN.md                Phase-by-phase plan for this module (living doc)
Dockerfile                      Sidecar container build (non-root, multi-stage)
docker-compose.yml              Standalone local run, for testing the module in isolation
```

Higher-level AgentDock/VTC architecture decisions (sidecar-per-module, MCP-over-HTTP,
entitlement-via-running-container, module contract/manifest shape) are tracked at the
VTC/AgentDock level, not in this repo — don't try to re-derive or duplicate them here.
