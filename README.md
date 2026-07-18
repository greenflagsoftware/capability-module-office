# agentdock-office

The Office capability module for AgentDock — a sidecar service that exposes Office-document
tools to VTC (Virtual Team Chat) over MCP. Every capability is implemented first as a
standalone CLI (`AgentDock.Office.Cli`); the MCP server is a thin adapter that shells out to it.
See [`docs/DEV_PLAN.md`](docs/DEV_PLAN.md) for scope and phase plan.

Scaffolded from [`agentdock-module-template`](https://github.com/greenflagsoftware/agentdock-module-template).

## What a module is

Each module is its own Docker container running an ASP.NET Core MCP server (HTTP transport,
via [`ModelContextProtocol.AspNetCore`](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)).
VTC connects to entitled modules' MCP endpoints and merges their tools into its agents' tool
list. A module is "licensed" simply by whether its container is running — there is no separate
entitlement check inside the module itself.

## Current capability

- **docx** — read plain text from a `.docx`, create a new `.docx` from text content, and get
  metadata (paragraph/word/character counts). Exposed over MCP as `docx_read`, `docx_create`,
  `docx_info`.
- Filesystem primitives (`read`/`write`/`list`) are available in the CLI but not yet exposed
  over MCP — see [`docs/DEV_PLAN.md`](docs/DEV_PLAN.md).

`module.manifest.json`'s `tools` array is a manually-maintained duplicate of the tool contract
until a shared contract package exists — update it by hand whenever tools are added, renamed,
or removed.

## Local development

```
dotnet build
dotnet test
docker compose up --build
curl http://localhost:8081/health
```

## Layout

```
src/AgentDock.Office/            ASP.NET Core MCP server (thin adapter over the CLI)
  Tools/                             MCP tool implementations, one file per tool group
  CliRunner.cs                       Shells out to the CLI subprocess, handles timeouts/errors
  Program.cs                         MCP HTTP transport + /health + /manifest wiring
  module.manifest.json               Declared tool contract (id, name, version, tools)
src/AgentDock.Office.Cli/        Standalone CLI — where the actual capability lives
  Commands/                          One file per command (read, write, list, docx)
  PathSecurity.cs                    Restricted-root sandboxing shared by all commands
  DocxEngine.cs                      OpenXml operations for .docx
tests/AgentDock.Office.Tests/     Tests for the MCP adapter layer
tests/AgentDock.Office.Cli.Tests/ Tests for the CLI layer (PathSecurity, DocxEngine)
docs/DEV_PLAN.md                 Phase-by-phase plan for this module
docs/TODO.md                     Scratch/ephemeral notes
Dockerfile                       Sidecar container build (publishes both projects side by side)
docker-compose.yml               Standalone local run, for testing the module in isolation
```
