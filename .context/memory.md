# Agent Memory

## Current Status

**Phase 0 — CLI scaffold — COMPLETE**
**Phase 1 — Filesystem primitives — COMPLETE**

### Phase 0 exit criteria verified

- [x] CLI project (`src/AgentDock.Office.Cli/`) builds standalone with `dotnet build`
- [x] `--help` prints help text with available commands and exits 0
- [x] `--version` prints version and exits 0
- [x] Three commands route correctly: `read`, `write`, `list`
- [x] Unrecognized command returns non-zero exit code (1) with clear error
- [x] Default handler shows "Use --help" message with exit code 1

### Phase 1 exit criteria verified

- [x] `read` returns contents of an existing text file within restricted root (JSON stdout)
- [x] `write` creates a new text file within restricted root (--mode create)
- [x] `write` overwrites an existing text file within restricted root (--mode overwrite)
- [x] `write` appends to an existing text file within restricted root (--mode append)
- [x] `write` accepts piped stdin input (content piping)
- [x] `list` returns directory entries with paths, type, and size for files
- [x] All three commands emit machine-readable JSON on stdout
- [x] Path traversal (e.g. `../etc/passwd`) rejected with non-zero exit code (2)
- [x] Read of non-existent file returns non-zero exit code (1)
- [x] Write create on existing file returns non-zero exit code (5)
- [x] Errors surfaced via stderr, not folded into JSON payload
- [x] Unauthorized/security errors use exit code 2
- [x] `--root` option overrides the restricted root directory
- [x] `OFFICE_CLI_ROOT` env var used as default restricted root

### Project structure

```
src/
├── AgentDock.Office/           MCP HTTP server (existing, unchanged)
└── AgentDock.Office.Cli/       CLI project (filesystem primitives)
    ├── Program.cs               Entry point, command routing
    ├── PathSecurity.cs          Restricted-root sandboxing
    └── Commands/
        ├── ReadCommand.cs       read <path>
        ├── WriteCommand.cs      write <path> [--mode] [--content] (stdin pipe)
        └── ListCommand.cs       list [<path>]
```

### Key architectural decisions (from DEV_PLAN.md)

- **CLI-first**: All capability lives in the CLI first; MCP adapter (Phase 3) shells out to the CLI binary.
- **Restricted root**: All file operations resolve against a configurable base directory (`OFFICE_CLI_ROOT` env var or `--root`). Paths traversing outside are rejected with `UnauthorizedAccessException`.
- **JSON output**: All commands emit machine-readable JSON on stdout; human-readable errors go to stderr.
- **Unix composability**: Commands that take text content support stdin piping.

## Up Next

**Phase 2 — First real Office capability**: One real Office-document command (docx/xlsx/etc.) built on the Phase 1 primitives. File type TBD.