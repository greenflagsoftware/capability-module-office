# Agent Memory

## Current Status

**All 4 phases complete — fully hardened end-to-end pipeline**

### Phase 0 — CLI scaffold (verified)
- CLI project builds standalone, `--help`/`--version` work, unknown commands error cleanly

### Phase 1 — Filesystem primitives (verified)
- `read`, `write` (create/overwrite/append + stdin piping), `list` with restricted-root sandboxing
- JSON stdout, errors on stderr, path traversal rejected

### Phase 2 — docx capability (verified)
- `docx create`, `docx read`, `docx info` via DocumentFormat.OpenXml v3.5.1

### Phase 3 — MCP adapter (verified)
- `CliRunner` shells out to CLI binary; `DocxTools` MCP tools call CLI
- Docker publishes both projects side-by-side; `/data` writable root
- All three tools round-trip end-to-end

### Phase 4 — Harden (verified)

- [x] Known failure modes documented and handled:
  - CLI binary not found → `FileNotFoundException` → wrapped as `InvalidOperationException`
  - Non-zero exit code → `CliToolException` with exit code, stderr, and command
  - Timeout → `CliTimeoutException` with process killed, command logged
  - Malformed JSON output → caught `JsonException`, wrapped with raw output preview
  - Empty output from CLI → `InvalidOperationException`
  - Null/empty/whitespace path → `ArgumentException`
  - Pre-cancelled token → `CliTimeoutException`
- [x] Non-zero CLI exit code surfaced as clear MCP tool-call failure (`isError: true`), not a crash
- [x] CLI subprocess timeout handled without hanging sidecar (process killed, exception thrown)
- [x] Malformed/unexpected CLI stdout handled without crashing MCP adapter (try/catch with wrapping)
- [x] Resource limits/timeouts applied at both CLI invocation (30s default) and MCP adapter layer
- [x] Bad input produces clear, predictable error surfaced to calling persona

### Test coverage (21 tests, all passing)

| Area | Tests |
|---|---|
| Path resolution | ResolveCliPath returns non-empty path |
| Default timeout | 30 seconds |
| Binary not found | Win32Exception |
| Help command | Succeeds and contains docx |
| Invalid command | CliToolException with non-zero exit |
| Nonexistent file read | CliToolException with exit code 1 |
| Path traversal | CliToolException with exit code 2 |
| Short timeout | CliTimeoutException |
| Cancellation | CliTimeoutException (pre-cancelled token) |
| Exception types | CliTool/CliTimeout/CliMalformedOutput property validation |
| Input validation | Null/empty/whitespace path — ArgumentException for all 3 tools |

### Project structure

```
src/
├── AgentDock.Office/                       MCP HTTP server
│   ├── Program.cs
│   ├── CliRunner.cs                        Hardened subprocess runner (timeout, cancellation, typed exceptions)
│   ├── CliToolException.cs                 Typed exceptions: CliToolException, CliTimeoutException, CliMalformedOutputException
│   ├── module.manifest.json
│   └── Tools/
│       └── DocxTools.cs                    Hardened MCP tools (input validation, malformed JSON, failure wrapping)
└── AgentDock.Office.Cli/                   CLI project (unchanged from Phase 2)
    ├── Program.cs, PathSecurity.cs, DocxEngine.cs
    └── Commands/
        ├── DocxCommand.cs, ListCommand.cs, ReadCommand.cs, WriteCommand.cs, SharedOptions.cs
```