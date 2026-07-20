# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`capability-module-office` is the Office capability module — a sidecar service that exposes
Office-document tools to VTC (Virtual Team Chat) over MCP. It was scaffolded from
[`capability-module-template`](https://github.com/greenflagsoftware/capability-module-template).
The template's placeholder `echo` tool (`ExampleTool.cs`) has been replaced: Phases 0–13 are
done (CLI scaffold, filesystem primitives, .docx read/create/info/edit, MCP adapter, hardening,
filename/content search, indexing, embeddings, file upload/delete, and the web API backend — see
[docs/DEV_PLAN.md](docs/DEV_PLAN.md)). `docs/DEV_PLAN.md` is a living document — keep its phase
checklists current as work lands, don't let it drift from what's actually merged.

## Module architecture (how this fits into VTC)

Each capability module is its own Docker container running an ASP.NET Core MCP server (HTTP
transport, via `ModelContextProtocol.AspNetCore`). VTC connects to entitled modules' MCP
endpoints and merges their tools into its agents' tool list.

- **Entitlement = running container.** A module is "licensed" simply by whether its container
  is running — there is no separate entitlement/license check inside the module itself. Do not
  add one.
- **Stateless HTTP transport.** `Program.cs` sets `options.Stateless = true` deliberately: VTC
  connects fresh per tool call rather than holding a session open, so the sidecar can scale or
  restart independently. Don't introduce server-side session state without revisiting this.
- **The MCP module has three endpoints, all wired in `Program.cs`:**
  - MCP endpoint (`app.MapMcp()`) — tools are discovered automatically via
    `WithToolsFromAssembly()`, so any `[McpServerToolType]` class in the assembly is picked up
    with no manual registration.
  - `/health` — used by both the Docker `HEALTHCHECK` and VTC as the entitlement signal.
  - `/manifest` — serves `module.manifest.json` verbatim as the declared tool contract.
- **`module.manifest.json` is a manually-maintained duplicate of the tool contract.** It must
  be kept in sync by hand with the actual `[McpServerTool]` methods until a shared contract
  package exists — update its `tools` array whenever tools are added, renamed, or removed.

## Adding a real tool

Per the CLI-first architecture, a new capability is built in the CLI first, then wired
into the MCP server and/or the WebApi:

1. Add the capability as a CLI command under `src/CapabilityModule.Office.Cli/Commands/` (see
   `DocxCommand.cs` for the pattern), exercised standalone before any MCP/WebApi involvement.
2. Add a class under `src/CapabilityModule.Office/Tools/` (one file per tool or tool group),
   `[McpServerToolType]` on the class, `[McpServerTool]` + `[Description(...)]` on each method
   (see `DocxTools.cs` for the pattern) — each method shells out to the CLI via `CliRunner` and
   adapts the JSON result. No manual registration needed — assembly scanning picks it up.
3. If the capability should be available over the REST API, add an endpoint in
   `src/CapabilityModule.Office.WebApi/Program.cs`.
4. Update `tools` in [module.manifest.json](src/CapabilityModule.Office/module.manifest.json) to match.
5. Add CLI-layer tests under `tests/CapabilityModule.Office.Cli.Tests/` (unit tests against the CLI's
   internals — see `PathSecurityTests.cs`/`DocxEngineTests.cs`), MCP-adapter tests under
   `tests/CapabilityModule.Office.Tests/` (see `CliRunnerTests.cs`), and WebApi-adapter tests under
   `tests/CapabilityModule.Office.WebApi.Tests/`.

## Commands

```
dotnet build
dotnet test
dotnet test tests/CapabilityModule.Office.WebApi.Tests
docker compose up --build
curl http://localhost:8082/health
```

Run a single test:
```
dotnet test --filter "FullyQualifiedName~DocxEngineTests.Create_ThenReadText_RoundTripsTitleAndContent"
```

Local docker-compose maps the container's internal port 8080 to host port 8082, and loads
`.env` if present (not committed).

## Layout

```
src/CapabilityModule.Office/             ASP.NET Core MCP server (thin adapter over the CLI)
  Tools/                                 MCP tool implementations, one file per tool group
  CliRunner.cs                           Shells out to the CLI subprocess, handles timeouts/errors
  CliToolException.cs                    Typed exceptions for CLI failure modes
  Program.cs                             MCP HTTP transport + /health + /manifest wiring
  module.manifest.json                   Declared tool contract (id, name, version, tools)
src/CapabilityModule.Office.Cli/         Standalone CLI — every capability's actual implementation
  Commands/                              One file per command (read, write, list, docx, search, index, upload, delete)
  PathSecurity.cs                        Restricted-root sandboxing shared by all commands
  DocxEngine.cs                          OpenXml operations for .docx
  VersionStore.cs                        Version store for pre-mutation snapshots
src/CapabilityModule.Office.WebApi/      REST API for the web UI (thin adapter over the CLI)
  Cli/                                   WebApi-specific CliRunner and exception types
  Program.cs                             Minimal API endpoints (/health, /search, /view, /upload, /edit, /delete)
tests/CapabilityModule.Office.Tests/     Tests for the MCP adapter layer (CliRunner, DocxTools)
tests/CapabilityModule.Office.Cli.Tests/ Tests for the CLI layer (PathSecurity, DocxEngine, VersionStore, commands)
tests/CapabilityModule.Office.WebApi.Tests/ Tests for the WebApi layer (CliRunner)
docs/DEV_PLAN.md                         Phase-by-phase plan for this module (living doc)
docs/TODO.md                             Scratch/ephemeral notes — not phase-tracking, prune freely
docs/agentic_guidance.xml                Reference corpus (blog feed) on agent/MCP tool design patterns
Dockerfile                               Sidecar + WebApi container build (multi-stage, publishes module and webapi targets)
docker-compose.yml                       Standalone local run, for testing the module and web UI in isolation
```

Higher-level capability-module/VTC architecture decisions (sidecar-per-module, MCP-over-HTTP,
entitlement-via-running-container, module contract/manifest shape) are tracked at the VTC level,
not in this repo — don't try to re-derive or duplicate them here.
