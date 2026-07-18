# Office Development Plan

Status: living document. Update as phases complete or the plan changes — this is not a
one-time artifact. Ephemeral notes (blockers, bugs noticed in passing, mid-phase reminders)
belong in [TODO.md](TODO.md), not here — this file tracks phases and architecture decisions,
not scratch state.

## What this module does

This module is a black-box document/filesystem capability exposed to VTC over MCP, backed by a
standalone CLI (see architecture notes below). It has no knowledge of which VTC persona or team
calls it — any mapping from a persona to this module's tools is a downward reference made by
VTC into this module, never the reverse. Starting from filesystem primitives (`read`, `write`,
`list` for plain text files, scoped to a restricted root), it will grow to cover real
Office-document capabilities (docx, xlsx, etc.) in later phases, with MCP wiring added only
after the CLI proves itself standalone.

## Scope

- In scope for v0.1:
  - CLI scaffold (Phase 0): command routing, `--help`/`--version`.
  - Filesystem primitives (Phase 1): `read`, `write` (create/overwrite/append), and `list`
    commands for plain text files, scoped to a restricted root, with composable/pipeable
    (reference and content) output.
  - Everything exercised standalone from the command line — no MCP wiring in v0.1.
- Explicitly deferred:
  - Office-document-specific capabilities (docx/xlsx/etc.) — Phase 2.
  - MCP adapter / VTC integration — Phase 3.
  - Hardening (error handling, resource limits/timeouts for the CLI subprocess) — Phase 4.
  - Any persona-specific behavior or assumptions about who is calling this module — it's
    intentionally a black box (see "What this module does" above).

## Architecture notes specific to this module

Most of the shape is already decided at the AgentDock level (see the architecture reference
below) — only note what's specific to *this* module here: external services it talks to,
credentials/config it needs, whether it's stateless or backs onto a stateful service, and any
higher-risk capabilities (shell exec, sending messages, financial actions) that need the
confirmation/scoping treatment called out in the AgentDock architecture decisions.

**CLI-first architecture.** Unlike the template's default (MCP tool methods holding the logic
directly), this module's actual capability lives in a standalone .NET C# CLI. The CLI is the
"operating system" for this module — every Office capability is implemented as a CLI command
first, with no MCP involved. The MCP server is a thin front end: each `[McpServerTool]` method
shells out to the CLI binary as a subprocess (arguments in, stdout/exit code out) and adapts the
result into an MCP tool response. Practical implications:

- The CLI must be scriptable and usable entirely on its own, outside of VTC/MCP — no hidden
  dependency on being invoked by the MCP host.
- CLI output that the MCP layer needs to parse back into structured tool results should be
  stable and machine-readable (e.g. JSON on stdout), with human-facing text kept separate from
  the parseable payload.
- Exit codes and stderr are the CLI's error-reporting contract; the MCP adapter maps them to
  tool-call failures rather than inventing a second error scheme.
- This reverses the phase order implied by the template (`docs/DEV_PLAN.md`'s old Phase 0/1
  assumed MCP came first) — see Phase Plan below. MCP wiring is deferred until the CLI proves
  itself standalone.

**Filesystem access is scoped to a restricted root.** The CLI's file commands (read/write/list)
do not accept arbitrary absolute paths — they resolve against a configured base directory, and
any path that would traverse outside it is rejected. This caps the blast radius once an agent
(via MCP) is the thing calling these commands; the sandboxing lives in the CLI itself rather
than being left to the MCP adapter to enforce.

**Composable/pipeable by design (Unix philosophy).** Commands are designed so one command's
output can feed another command's input, à la classic Unix/Bell Labs tool composition. This has
two distinct levels, and a given command may support one or both — it's a per-command design
decision made when that command is built, not a fixed rule by file format:

- *Reference composability (universal):* every command's output includes stable, parseable
  identifiers — file paths, names, locations — that can be captured and passed as an *argument*
  to another command. `list` outputs paths; those paths are valid input to `read`, `write`, or
  any future Office-document command. This applies to every command regardless of file type,
  since it only requires a reference to a file, never its actual bytes.
- *Content piping:* raw content flows directly through stdin/stdout between commands — e.g.
  `read foo.txt | write bar.txt`, or a document pipeline like
  `generate | spellcheck | write` where a document's textual content is generated, corrected,
  and only then committed to disk. This is not limited to the plain-text file primitives — it
  applies anywhere a command's input/output is naturally stream-shaped content (most often
  text, including text extracted from or destined for an Office document). The limiting case is
  a command whose input/output isn't stream-shaped at all — e.g. an operation needing random
  access to an entire file's structure at once — which composes via reference passing instead
  of content piping. That's evaluated per command, not assumed in advance for a whole file type.

Practical implication: every command's machine-readable output (see CLI-first architecture
above) should include the file path/identifier it produced or operated on, so reference
composability always works — and commands whose input/output is naturally a text stream
(generation, spell-checking, transformation stages, etc.) should also support stdin/stdout
content piping rather than requiring a file round-trip at every stage.

**No upward references.** This module has no knowledge of VTC personas, teams, or who is
calling it — it exposes its CLI/MCP capabilities as a black box. Any mapping from a persona to
this module's tools is decided and owned by VTC, referencing downward into this module; this
module must never reference back up into VTC or persona-specific concepts.

## Phase Plan

### Phase 0 — CLI scaffold

- Deliverable: a bare .NET C# CLI project (separate from the MCP host project) with argument
  parsing, command routing, and `--help`/`--version` output. No real Office capability yet —
  this phase only proves the "operating system" skeleton: a command comes in, gets routed, and
  produces output.
- Exit criteria:
  - [ ] CLI builds standalone with `dotnet build`
  - [ ] `dotnet run --project <cli-project> -- --help` prints help text and exits 0
  - [ ] `dotnet run --project <cli-project> -- --version` prints a version and exits 0
  - [ ] at least one placeholder command routes correctly and produces output
  - [ ] an unrecognized command returns a non-zero exit code with a clear error (not a crash)

### Phase 1 — Filesystem primitives

- Deliverable: `read`, `write`, and `list` commands operating on the filesystem, scoped to a
  restricted root (see architecture notes above — no arbitrary absolute paths, no traversal
  outside the configured base directory). Output is machine-readable (JSON on stdout) per the
  CLI's contract with the future MCP adapter. Still no MCP involvement; exercised directly from
  the command line.
- `write` initially only targets plain text files — no binary formats, no Office formats
  (docx/xlsx/etc.) yet. Within that scope it covers create, overwrite, and append (not just
  create-new); `read` and `list` are not format-restricted the same way (`read` reads back what
  `write` produced; `list` just reports directory entries).
- Not yet Office-document-aware — these are generic file primitives (bytes/text in, bytes/text
  out, directory entries listed), not docx/xlsx parsing. That comes in the next phase.
- Exit criteria:
  - [ ] `read` returns the contents of an existing text file within the restricted root
  - [ ] `write` creates a new text file within the restricted root
  - [ ] `write` overwrites an existing text file within the restricted root
  - [ ] `write` appends to an existing text file within the restricted root
  - [ ] `list` returns directory entries (including paths) for a directory within the
    restricted root
  - [ ] all three commands emit machine-readable (JSON) output on stdout
  - [ ] `read`, `write`, and `list` each reject a path that traverses outside the restricted
    root, with a non-zero exit code
  - [ ] each command surfaces errors via exit code/stderr, not folded into the JSON payload

### Phase 2 — First real Office capability

- Deliverable: one real, end-to-end Office capability implemented as a CLI command, built on
  top of the Phase 1 filesystem primitives (file type/capability TBD — not yet decided). Still
  no MCP involvement.
- Exit criteria:
  - [ ] the file type/capability for this phase has been decided (currently TBD)
  - [ ] the command runs standalone against a real input file of that type
  - [ ] the command produces correct output for that real input
  - [ ] errors surface via exit code/stderr, consistent with the Phase 0/1 CLI contract

### Phase 3 — MCP adapter

- Deliverable: wire the MCP server (`src/AgentDock.Office`) to shell out to the CLI for the
  capability built in Phase 2, replacing the placeholder `echo` tool in
  [ExampleTool.cs](../src/AgentDock.Office/Tools/ExampleTool.cs).
- Update [module.manifest.json](../src/AgentDock.Office/module.manifest.json) to describe the
  real tool set instead of the placeholder.
- Exit criteria:
  - [ ] `docker compose up --build` starts the module successfully
  - [ ] `/health` returns healthy
  - [ ] `module.manifest.json` describes the real tool set, not the `echo` placeholder
  - [ ] `ExampleTool.cs`/`echo` has been removed
  - [ ] VTC (or a manual MCP client) can call the real tool and get correct output end to end
    through the CLI subprocess

### Phase 4 — Harden

- Error handling for the failure modes specific to this module's external dependencies,
  including CLI subprocess failures (non-zero exit, timeout, malformed output).
- Resource limits / timeouts appropriate to what the module does, applied at both the CLI
  invocation and the MCP adapter layer.
- Exit criteria:
  - [ ] known failure modes for this module's external dependencies are documented and handled
  - [ ] a non-zero CLI exit code is surfaced as a clear MCP tool-call failure, not a crash
  - [ ] a CLI subprocess timeout is handled without hanging the sidecar
  - [ ] malformed/unexpected CLI stdout is handled without crashing the MCP adapter
  - [ ] resource limits/timeouts are applied at both the CLI invocation and the MCP adapter
    layer
  - [ ] bad input produces a clear, predictable error surfaced to the calling persona

## Reference

- AgentDock module architecture decisions (sidecar-per-module, MCP over HTTP, entitlement via
  running container, module contract/manifest shape): tracked separately at the VTC/AgentDock
  level, not duplicated here.
