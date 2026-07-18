# agentdock-office

The Office capability module for AgentDock — a sidecar service that exposes Office-document
tools to VTC (Virtual Team Chat) over MCP. See [`docs/DEV_PLAN.md`](docs/DEV_PLAN.md) for
scope and phase plan (not yet filled in beyond the template).

Scaffolded from [`agentdock-module-template`](https://github.com/greenflagsoftware/agentdock-module-template).

## What a module is

Each module is its own Docker container running an ASP.NET Core MCP server (HTTP transport,
via [`ModelContextProtocol.AspNetCore`](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)).
VTC connects to entitled modules' MCP endpoints and merges their tools into its agents' tool
list. A module is "licensed" simply by whether its container is running — there is no separate
entitlement check inside the module itself.

## Next steps

- Fill in [`docs/DEV_PLAN.md`](docs/DEV_PLAN.md) with what this module actually does.
- Delete [`ExampleTool.cs`](src/AgentDock.Office/Tools/ExampleTool.cs) once a real tool exists
  and the MCP endpoint has been confirmed to round-trip end to end.
- Update `module.manifest.json`'s `tools` array to match the real tool set as you build it —
  this is a temporary duplication of the tool contract until a shared contract package exists.

## Local development

```
dotnet build
dotnet test
docker compose up --build
curl http://localhost:8081/health
```

## Layout

```
src/AgentDock.Office/       ASP.NET Core MCP server (the module itself)
  Tools/                        Tool implementations, one file per tool or tool group
  Program.cs                    MCP HTTP transport + /health + /manifest wiring
  module.manifest.json          Declared tool contract (id, name, version, tools)
tests/AgentDock.Office.Tests/
docs/DEV_PLAN.md                Phase-by-phase plan for this module
Dockerfile                      Sidecar container build
docker-compose.yml              Standalone local run, for testing the module in isolation
```
