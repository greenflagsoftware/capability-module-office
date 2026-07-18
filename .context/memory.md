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
- `DocxEngine.GetInfo` throws `InvalidDataException` on a document with no body — it used to
  fold `["error"] = "..."` into the JSON payload on a 200-equivalent exit code, which violated
  the CLI's own error contract (errors surface via exit code/stderr, never in the JSON payload).
  Fixed; covered by `DocxEngineTests.GetInfo_DocumentWithNoBody_ThrowsInsteadOfReturningErrorInPayload`.

### Phase 3 — MCP adapter (verified)
- `CliRunner` shells out to CLI binary; `DocxTools` MCP tools call CLI
- Docker publishes both projects side-by-side; `/data` writable root
- All three tools round-trip end-to-end
- MCP transport is **stateless** (`Program.cs` — `options.Stateless = true`), per CLAUDE.md's
  architecture decision (VTC connects fresh per call; sidecar can scale/restart independently).
  A later commit briefly flipped this to stateful to ease MCP Inspector testing — that
  contradicted CLAUDE.md and was reverted. Stateless mode works fine for `tools/list`/
  `tools/call` with no session handshake required — verified against the built container.
  Don't flip this again without revisiting CLAUDE.md.
- `DocxTools`/`CliRunner` pass arguments via `ProcessStartInfo.ArgumentList`, not a manually
  escaped command-line string. An earlier version built one big string with hand-rolled
  backslash/quote escaping (`EscapeArg`) that unconditionally doubled every backslash — this
  corrupted any content/title/path containing a literal backslash (Windows paths, UNC shares,
  regex, etc.). Confirmed by round-tripping such content through the live container before the
  fix. `ArgumentList` avoids manual escaping entirely; don't reintroduce a hand-built arguments
  string here.

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

### Test coverage (37 tests, all passing, across two projects)

`tests/AgentDock.Office.Tests/` (21 tests) — MCP adapter layer, subprocess integration:

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

`tests/AgentDock.Office.Cli.Tests/` (16 tests, added after the Phase 4 gap review) — direct
unit tests against `PathSecurity` and `DocxEngine` internals (via `InternalsVisibleTo`):

| Area | Tests |
|---|---|
| Path resolution | Relative, nested, `.` (root itself) |
| Path traversal | `../`, deep `../../`, absolute path outside root — all throw |
| Leading-slash regression | `/foo` and `///foo` treated as relative, not a root override (the bug fixed in `cdfe4ad`) |
| Prefix-confusion guard | Sibling dir sharing root's string prefix (`root-evil`) is still rejected |
| Root resolution | `EffectiveRoot` override vs. env-var/cwd fallback |
| Docx round-trip | Create → ReadText preserves title/content, including literal backslashes |
| Docx metadata | GetInfo paragraph/word counts match real content |
| Docx error contract | GetInfo on a body-less document throws `InvalidDataException`, doesn't fold an error into JSON |

### Project structure

```
src/
├── AgentDock.Office/                       MCP HTTP server (stateless transport)
│   ├── Program.cs
│   ├── CliRunner.cs                        Hardened subprocess runner (timeout, cancellation,
│   │                                        typed exceptions, ArgumentList-based invocation)
│   ├── CliToolException.cs                 Typed exceptions: CliToolException, CliTimeoutException, CliMalformedOutputException
│   ├── module.manifest.json
│   └── Tools/
│       └── DocxTools.cs                    Hardened MCP tools (input validation, malformed JSON,
│                                            failure wrapping, arg-list building — no manual escaping)
└── AgentDock.Office.Cli/                   CLI project (DocxEngine.GetInfo now throws on
    │                                        no-body docs instead of embedding an error in JSON)
    ├── Program.cs, PathSecurity.cs, DocxEngine.cs
    └── Commands/
        ├── DocxCommand.cs, ListCommand.cs, ReadCommand.cs, WriteCommand.cs, SharedOptions.cs

tests/
├── AgentDock.Office.Tests/                 MCP adapter layer (21 tests)
└── AgentDock.Office.Cli.Tests/             CLI layer — PathSecurity, DocxEngine (16 tests)
```

### Gap review (post-Phase-4)

A follow-up review against `DEV_PLAN.md`'s exit criteria (done by rebuilding the container and
exercising every tool live, not just reading code) found and fixed: the stateful-transport
regression, the argument-escaping content-corruption bug, and the GetInfo error-contract
violation, all documented above. It also updated `DEV_PLAN.md`'s exit-criteria checkboxes
(previously all unchecked despite the work being done), `README.md`, and `CLAUDE.md`, which had
drifted stale (dangling references to the deleted `ExampleTool.cs`/`ExampleToolTests`).