# Agent Memory

## Current Status

**Phase 0 — CLI scaffold — COMPLETE**
**Phase 1 — Filesystem primitives — COMPLETE**
**Phase 2 — First real Office capability (docx) — COMPLETE**

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
- [x] Read of non-existent file returns non-zero exit code (1)
- [x] Write create on existing file returns non-zero exit code (5)
- [x] Errors surfaced via stderr, not folded into JSON payload
- [x] Unauthorized/security errors use exit code 2
- [x] `--root` option overrides the restricted root directory
- [x] `OFFICE_CLI_ROOT` env var used as default restricted root

### Phase 2 — docx capability (verified)

- [x] `docx create <path>` creates a .docx document with title (Heading1) and body paragraphs
- [x] `docx create` accepts content via `--content` flag and stdin piping
- [x] `docx read <path>` extracts plain text from a .docx, preserving paragraph breaks
- [x] `docx info <path>` returns paragraph count, word count, and character count
- [x] Path traversal protection applies to all docx subcommands (exit code 2)
- [x] Non-existent file returns exit code 1
- [x] Existing file on create returns exit code 5
- [x] Build: 0 warnings, 0 errors
- [x] Tests: 1/1 passed
- [x] Full solution builds and tests pass

### Project structure

```
src/
├── AgentDock.Office/                       MCP HTTP server (unchanged from scaffold)
└── AgentDock.Office.Cli/                   CLI project
    ├── Program.cs                           Entry point, command routing
    ├── PathSecurity.cs                      Restricted-root sandboxing
    ├── DocxEngine.cs                        OpenXml operations (read text, create docx, get info)
    └── Commands/
        ├── SharedOptions.cs                 Shared option definitions (--root)
        ├── ReadCommand.cs                   read <path>
        ├── WriteCommand.cs                  write <path> [--mode] [--content] (stdin pipe)
        ├── ListCommand.cs                   list [<path>]
        └── DocxCommand.cs                   docx {read|create|info} <path>
```

### Key architectural decisions (from DEV_PLAN.md)

- **CLI-first**: All capability lives in the CLI first; MCP adapter (Phase 3) shells out to the CLI binary.
- **Restricted root**: All file operations resolve against a configurable base directory (`OFFICE_CLI_ROOT` env var or `--root`). Paths traversing outside are rejected.
- **JSON output**: All commands emit machine-readable JSON on stdout; errors go to stderr.
- **Unix composability**: Commands that take text content support stdin piping.
- **docx via OpenXml**: Uses `DocumentFormat.OpenXml` v3.5.1 for .docx read, create, and metadata.

## Up Next

**Phase 3 — MCP adapter**: Wire the MCP server (`src/AgentDock.Office`) to shell out to the CLI binary, replacing the `ExampleTool.cs` placeholder `echo` tool with real docx tools. Update `module.manifest.json` accordingly.