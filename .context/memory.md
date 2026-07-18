# Agent Memory

## Current Status

**Phase 0 — CLI scaffold — COMPLETE**
**Phase 1 — Filesystem primitives — COMPLETE**
**Phase 2 — First real Office capability (docx) — COMPLETE**
**Phase 3 — MCP adapter — COMPLETE**

### Phase 0 — CLI scaffold (verified)

- [x] CLI project (`src/AgentDock.Office.Cli/`) builds standalone with `dotnet build`
- [x] `--help` prints help text with available commands and exits 0
- [x] `--version` prints version and exits 0
- [x] Three commands route correctly: `read`, `write`, `list`
- [x] Unrecognized command returns non-zero exit code (1) with clear error
- [x] Default handler shows "Use --help" message with exit code 1

### Phase 1 — Filesystem primitives (verified)

- [x] `read` returns contents of an existing text file within restricted root (JSON stdout)
- [x] `write` creates a new text file within restricted root (--mode create)
- [x] `write` overwrites an existing text file within restricted root (--mode overwrite)
- [x] `write` appends to an existing text file within restricted root (--mode append)
- [x] `write` accepts piped stdin input (content piping)
- [x] `list` returns directory entries with paths, type, and size for files
- [x] All three commands emit machine-readable JSON on stdout
- [x] Path traversal (e.g. `../etc/passwd`) rejected with non-zero exit code (2)
- [x] Errors surfaced via stderr, not folded into JSON payload
- [x] `OFFICE_CLI_ROOT` env var and `--root` option for scoping

### Phase 2 — docx capability (verified)

- [x] `docx create`, `read`, `info` subcommands using DocumentFormat.OpenXml v3.5.1
- [x] stdin content piping supported
- [x] Path traversal rejected; non-existent file / create-existing errors

### Phase 3 — MCP adapter (verified)

- [x] `CliRunner.cs` — shells out to CLI binary as subprocess, captures JSON stdout
- [x] `DocxTools.cs` — MCP tools (`docx_read`, `docx_create`, `docx_info`) that call CLI
- [x] `ExampleTool.cs` (placeholder echo) deleted
- [x] `module.manifest.json` updated with real tool set, removed echo
- [x] `Dockerfile` builds and publishes both projects; CLI binary sits alongside MCP server
- [x] `/data` directory created for writable restricted root; `OFFICE_CLI_ROOT=/data` set in container
- [x] `docker compose up --build` starts the module successfully
- [x] `/health` returns `{"status":"healthy"}`
- [x] MCP tool calls round-trip end to end through CLI subprocess inside Docker:
  - `docx_create` → `Created .docx at /data/...`
  - `docx_read` → full text content with paragraph breaks
  - `docx_info` → paragraph, word, character counts
- [x] Solution builds: 0 warnings, 0 errors
- [x] Tests: 2/2 passed

### Project structure

```
src/
├── AgentDock.Office/                       MCP HTTP server
│   ├── Program.cs                           Entry point, MCP/health/manifest
│   ├── CliRunner.cs                         Shells out to CLI binary as subprocess
│   ├── module.manifest.json                 Tool contract (docx_read/create/info)
│   └── Tools/
│       └── DocxTools.cs                     MCP tool methods calling CLI
└── AgentDock.Office.Cli/                   CLI project
    ├── Program.cs                           Entry point, command routing
    ├── PathSecurity.cs                      Restricted-root sandboxing
    ├── DocxEngine.cs                        OpenXml operations
    └── Commands/
        ├── DocxCommand.cs                   docx {read|create|info}
        ├── ListCommand.cs, ReadCommand.cs, WriteCommand.cs, SharedOptions.cs
```

## Up Next

**Phase 4 — Harden**: Error handling for CLI subprocess failures (non-zero exit, timeout, malformed output), resource limits, and timeouts at both the CLI and MCP adapter layers.