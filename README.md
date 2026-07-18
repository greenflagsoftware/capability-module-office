# agentdock-module-template

A GitHub template repository for AgentDock capability modules — sidecar services that expose
tools to VTC (Virtual Team Chat) over MCP.

## What a module is

Each module is its own Docker container running an ASP.NET Core MCP server (HTTP transport,
via [`ModelContextProtocol.AspNetCore`](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)).
VTC connects to entitled modules' MCP endpoints and merges their tools into its agents' tool
list. A module is "licensed" simply by whether its container is running — there is no separate
entitlement check inside the module itself.

## Creating a new module from this template

1. `gh repo create your-org/agentdock-<module> --template your-org/agentdock-module-template`
2. Find-and-replace `ModuleName` → your module's PascalCase name (e.g. `Docs`) and
   `module-name` → its kebab-case id (e.g. `docs`) across the repo. That covers:
   - `AgentDock.ModuleName.slnx` → rename the file itself too
   - `src/AgentDock.ModuleName/` → rename the folder and `.csproj`
   - `tests/AgentDock.ModuleName.Tests/` → rename the folder and `.csproj`
   - `module.manifest.json` (`id`, `name` fields)
   - `Dockerfile`, `docker-compose.yml`, `README.md`, `docs/DEV_PLAN.md`
3. Delete [`ExampleTool.cs`](src/AgentDock.ModuleName/Tools/ExampleTool.cs) once you've added a
   real tool and confirmed the MCP endpoint round-trips end to end.
4. Fill in [`docs/DEV_PLAN.md`](docs/DEV_PLAN.md) with what this module actually does.
5. Update `module.manifest.json`'s `tools` array to match the real tool set as you build it —
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
src/AgentDock.ModuleName/       ASP.NET Core MCP server (the module itself)
  Tools/                        Tool implementations, one file per tool or tool group
  Program.cs                    MCP HTTP transport + /health + /manifest wiring
  module.manifest.json          Declared tool contract (id, name, version, tools)
tests/AgentDock.ModuleName.Tests/
docs/DEV_PLAN.md                Phase-by-phase plan for this module
Dockerfile                      Sidecar container build
docker-compose.yml              Standalone local run, for testing the module in isolation
```
