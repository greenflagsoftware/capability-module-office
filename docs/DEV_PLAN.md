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

Most of the shape is already decided at the capability-module level (see the architecture
reference below) — only note what's specific to *this* module here: external services it talks
to, credentials/config it needs, whether it's stateless or backs onto a stateful service, and any
higher-risk capabilities (shell exec, sending messages, financial actions) that need the
confirmation/scoping treatment called out in the capability-module architecture decisions.

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
  - [x] CLI builds standalone with `dotnet build`
  - [x] `dotnet run --project <cli-project> -- --help` prints help text and exits 0
  - [x] `dotnet run --project <cli-project> -- --version` prints a version and exits 0
  - [x] at least one placeholder command routes correctly and produces output (now superseded
    by the real `read`/`write`/`list`/`docx` commands below)
  - [x] an unrecognized command returns a non-zero exit code with a clear error (not a crash)

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
  - [x] `read` returns the contents of an existing text file within the restricted root
  - [x] `write` creates a new text file within the restricted root
  - [x] `write` overwrites an existing text file within the restricted root
  - [x] `write` appends to an existing text file within the restricted root
  - [x] `list` returns directory entries (including paths) for a directory within the
    restricted root
  - [x] all three commands emit machine-readable (JSON) output on stdout
  - [x] `read`, `write`, and `list` each reject a path that traverses outside the restricted
    root, with a non-zero exit code — covered by unit tests in
    `tests/CapabilityModule.Office.Cli.Tests/PathSecurityTests.cs`, including a regression test for
    the leading-slash bug fixed in a later commit
  - [x] each command surfaces errors via exit code/stderr, not folded into the JSON payload

### Phase 2 — First real Office capability

- Deliverable: one real, end-to-end Office capability implemented as a CLI command, built on
  top of the Phase 1 filesystem primitives (file type/capability TBD — not yet decided). Still
  no MCP involvement.
- Decided: .docx (Word), via `DocumentFormat.OpenXml`. `docx read` extracts plain text,
  `docx create` builds a new document with a title heading and body paragraphs, `docx info`
  returns paragraph/word/character counts.
- Exit criteria:
  - [x] the file type/capability for this phase has been decided — .docx
  - [x] the command runs standalone against a real input file of that type
  - [x] the command produces correct output for that real input — covered by
    `tests/CapabilityModule.Office.Cli.Tests/DocxEngineTests.cs`
  - [x] errors surface via exit code/stderr, consistent with the Phase 0/1 CLI contract
    (`docx info` on a document with no body throws rather than folding an `"error"` key into
    the JSON payload — fixed after an initial version violated this)

### Phase 3 — MCP adapter

- Deliverable: wire the MCP server (`src/CapabilityModule.Office`) to shell out to the CLI for the
  capability built in Phase 2, replacing the placeholder `echo` tool in
  [ExampleTool.cs](../src/CapabilityModule.Office/Tools/ExampleTool.cs).
- Update [module.manifest.json](../src/CapabilityModule.Office/module.manifest.json) to describe the
  real tool set instead of the placeholder.
- Exit criteria:
  - [x] `docker compose up --build` starts the module successfully
  - [x] `/health` returns healthy
  - [x] `module.manifest.json` describes the real tool set, not the `echo` placeholder
  - [x] `ExampleTool.cs`/`echo` has been removed
  - [x] VTC (or a manual MCP client) can call the real tool and get correct output end to end
    through the CLI subprocess — verified against the built container via `mcp-test.mjs` and
    raw MCP requests (`docx_create` / `docx_read` / `docx_info` round-trip correctly, including
    content containing backslashes, which an earlier argument-escaping bug had corrupted)

**Note on transport mode:** an intermediate commit switched `Program.cs` to
`options.Stateless = false` to make manual testing via MCP Inspector easier. That contradicted
the stateless architecture decision recorded in `CLAUDE.md` (VTC connects fresh per call; no
server-side session state) and was reverted back to `Stateless = true`. Stateless mode works
fine for `tools/list`/`tools/call` without any session handshake — verified directly against
the running container.

### Phase 4 — Harden

- Error handling for the failure modes specific to this module's external dependencies,
  including CLI subprocess failures (non-zero exit, timeout, malformed output).
- Resource limits / timeouts appropriate to what the module does, applied at both the CLI
  invocation and the MCP adapter layer.
- Exit criteria:
  - [x] known failure modes for this module's external dependencies are documented and handled
    (CLI binary missing, non-zero exit, timeout, malformed/empty stdout — see `CliRunner.cs`
    and `CliToolException.cs`)
  - [x] a non-zero CLI exit code is surfaced as a clear MCP tool-call failure, not a crash
  - [x] a CLI subprocess timeout is handled without hanging the sidecar (process killed,
    `CliTimeoutException` thrown)
  - [x] malformed/unexpected CLI stdout is handled without crashing the MCP adapter
  - [x] resource limits/timeouts are applied at both the CLI invocation (30s default) and the
    MCP adapter layer
  - [x] bad input produces a clear, predictable error surfaced to the calling persona
    (`ArgumentException` for null/empty/whitespace paths)

**Note on argument passing:** the original Phase 3/4 implementation built CLI arguments as a
single manually-escaped string (`DocxTools.EscapeArg`), which unconditionally doubled every
backslash rather than only ones preceding an embedded quote. This corrupted any document
content/title/path containing a literal backslash (Windows paths, UNC shares, regex, etc.) —
confirmed by round-tripping such content through the live container before the fix. Fixed by
switching `CliRunner`/`DocxTools` to build a `ProcessStartInfo.ArgumentList` instead of a hand-
escaped string, which sidesteps manual escaping entirely.

### Phase 5 — Document & filename search

- Deliverable: a `search` CLI command that finds files by name/path pattern within the
  restricted root, recursively (unlike `list`, which only enumerates one directory's immediate
  entries). Built on .NET's own file-system APIs (`Directory.EnumerateFiles`/glob matching) —
  no shelling out to external search binaries (`find`/`grep`). That keeps the CLI self-contained
  and portable per the CLI-first architecture notes above, and avoids a second layer of
  subprocess/escaping risk on top of the MCP→CLI subprocess boundary that Phase 4 just hardened.
  Output follows the same reference-composability contract as `list`/`read` (matched entries
  include `path` so they can be piped into `read`/`docx read`/etc.).
- No index/lookup structure in this phase — filename search over the restricted root's
  directory tree is fast enough for an on-demand tool call at this scale. Revisit indexing only
  if/when content search (below) is added and a directory walk proves too slow in practice.
- Explicitly deferred: content-based search (matching *inside* file contents, e.g. .docx body
  text) — a likely future enhancement, not built now. When it lands, that's the natural trigger
  to reconsider an index — a full-text index amortizes across many searches; a directory walk
  doesn't need one for filenames alone.
- Exit criteria:
  - [x] `search` command added to the CLI, scoped to the restricted root (same `PathSecurity`
    sandboxing as `read`/`write`/`list`)
  - [x] matches by filename/path pattern (substring or glob), recursively under the given
    directory
  - [x] JSON output on stdout; each match includes `name`/`path` (and `type`), consistent with
    `list`'s entry shape
  - [x] rejects a path/root that traverses outside the restricted root, with a non-zero exit
    code (covered by tests alongside existing `PathSecurityTests.cs`)
  - [x] errors surface via exit code/stderr, not folded into the JSON payload
  - [x] MCP tool wired per Phase 3's pattern (new tool class alongside `DocxTools.cs`),
    `module.manifest.json` updated
  - [x] unit tests under `tests/CapabilityModule.Office.Cli.Tests/` for the CLI command; MCP-adapter
    tests under `tests/CapabilityModule.Office.Tests/`

### Phase 6 — Document editing

- Decided: first edit primitive is find-and-replace, via a new `docx replace` command —
  targeted text substitution within an existing document's body, not a full caller-supplied
  rewrite (that stays a deferred "full-content replace" option, not built now — see prior
  draft of this phase for the rejected/deferred alternatives).
- Mechanically, `docx replace` rebuilds the whole document in memory (OpenXml doesn't offer a
  clean partial in-place text patch), so the on-disk operation is a full-file overwrite even
  though the caller-facing contract is a targeted substitution — this is an implementation
  detail, not a change to the primitive's semantics or risk profile.
- **Versioning:** before the new content is written, the CLI snapshots the current file to a
  version store, then overwrites the original in place. This gives an undo path without needing
  a separate "undo" command to exist yet — the previous version is just a file sitting in the
  restricted root.
  - Proposed convention (flag if you want something else before this is built): a `_versions/`
    subdirectory mirroring the document's relative path, with the pre-edit copy saved as
    `_versions/<relative-path>/<original-filename>.v{N}.docx` (`N` incrementing per prior
    version count for that file). Kept inside the restricted root so existing `PathSecurity`
    sandboxing covers it with no new trust boundary.
  - Retention is unbounded for this phase — no pruning/expiry of old versions. Worth flagging
    as a follow-up hardening item (akin to Phase 4's resource limits) once real usage shows
    whether unbounded version growth is actually a problem, rather than guessing now.
  - `docx replace`'s JSON output includes the version number and version path it wrote, so the
    version is both a human-readable ordinal and referenceable by other commands
    (`docx read <version-path>` to inspect a prior version) per the reference-composability
    contract.
  - JSON output also includes the last-write/modified timestamp (UTC, ISO 8601) of the file
    after the overwrite — read from the filesystem (`FileInfo.LastWriteTimeUtc`) rather than
    tracked separately, so it can't drift from what's actually on disk.
- Follows the same CLI-first pattern as Phase 2: new `DocxEngine` method(s), new `docx replace`
  subcommand, then an MCP tool once the CLI proves out standalone.
- Consult [agentic_guidance.xml](agentic_guidance.xml) for method-safety/reversibility gating —
  write-capable, mutating commands warrant more caution than the read-only
  `docx read`/`info`/`search`; the versioning mechanism above is this module's answer to that
  for `docx replace` specifically.
- Exit criteria:
  - [ ] `docx replace <path>` command added, taking a single find/replace text pair (not a
    batch — one substitution per call, matching current scope)
  - [ ] before overwriting, the pre-edit content is snapshotted to the version store described
    above, with an incrementing version number
  - [ ] the target file is overwritten with the substitution applied; non-matching content is
    verifiably unchanged (this is the check that distinguishes edit correctness from `create`'s)
  - [ ] JSON output includes the edited file's path/resolved location, the version number and
    version path written, and the file's last-write timestamp (UTC, ISO 8601) after the
    overwrite, consistent with other commands' reference-composability contract
  - [ ] rejects a path that traverses outside the restricted root, and a find/replace on a
    nonexistent file, each with a non-zero exit code
  - [ ] errors surface via exit code/stderr, not folded into the JSON payload
  - [ ] MCP tool wired per Phase 3's pattern (alongside `DocxTools.cs`), `module.manifest.json`
    updated
  - [ ] unit tests under `tests/CapabilityModule.Office.Cli.Tests/` covering both the substitution and
    the versioning behavior; MCP-adapter tests under `tests/CapabilityModule.Office.Tests/`

## Reference

- Capability-module architecture decisions (sidecar-per-module, MCP over HTTP, entitlement via
  running container, module contract/manifest shape): tracked separately at the VTC level, not
  duplicated here.
- [agentic_guidance.xml](agentic_guidance.xml) — Atom feed export of the *Agentic Thinking*
  blog (agenticthinking.ai), covering agent personas, MCP tool design, tool composability, and
  the standards that make agent tools/agents usable. Consult it when designing this module's
  actual tools (Phase 2 onward) and the MCP adapter (Phase 3) for applicable patterns —
  particularly method-safety/reversibility gating for any write-capable command, and
  composability conventions consistent with this plan's own piping principles above. It's a
  reference corpus, not something to read end to end each session — grep/search it for the
  topic at hand.
