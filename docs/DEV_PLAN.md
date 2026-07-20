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

**Content indexing backs onto a stateful Postgres+pgvector service (Phase 7+).** Every phase
through Phase 6 operates only on the restricted-root filesystem — no external service, no state
beyond files on disk. Phase 7 introduces this module's first stateful external dependency: a
Postgres database (with the `pgvector` extension) for semantic/hybrid search over indexed
document content. Per the R&D tenancy decision recorded in Phase 7, this is one Postgres+pgvector
instance colocated with the module's own container (not shared across tenants) — consistent with
the existing entitlement-via-running-container model, and deliberately not designed around a
tenant count that isn't known yet. The schema itself stays tenant-agnostic (no `tenant_id`
column, no assumptions baked in that only hold for exactly one tenant per database) so a future
shared-instance model would be an additive change, not a redesign. This does not change the
module's own transport statelessness (`options.Stateless = true` in `Program.cs`, per VTC
connecting fresh per call) — that's about the MCP session, not the backing store; the CLI/MCP
adapter remains a thin front end, now over two things it shells/queries out to (the CLI binary,
and Postgres) instead of one.

**The web UI (Phase 14+) is this module's first non-VTC consumption surface.** Every phase
through Phase 13 is consumed exactly one way: VTC calls MCP tools, which shell out to the CLI.
Phase 14 adds a browser-facing REST API, and Phase 15 a React/TS single-page app on top of it —
a second, independent way to reach the same CLI capabilities. This does not change "no upward
references" (the web UI still knows nothing about VTC/personas) but it does mean the CLI's
capabilities now have two front ends (MCP adapter, web API) shelling out to the same binary. Per
the R&D-stage decision recorded in Phase 14, the web UI ships with **no authentication** —
consistent with this module's existing entitlement-via-running-container trust model (the
network/container boundary is the security boundary today, same as MCP access). Revisit if this
UI is ever exposed beyond a trusted network.

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
  - [x] `docx replace <path>` command added, taking a single find/replace text pair (not a
    batch — one substitution per call, matching current scope)
  - [x] before overwriting, the pre-edit content is snapshotted to the version store described
    above, with an incrementing version number
  - [x] the target file is overwritten with the substitution applied; non-matching content is
    verifiably unchanged (this is the check that distinguishes edit correctness from `create`'s)
  - [x] JSON output includes the edited file's path/resolved location, the version number and
    version path written, and the file's last-write timestamp (UTC, ISO 8601) after the
    overwrite, consistent with other commands' reference-composability contract
  - [x] rejects a path that traverses outside the restricted root, and a find/replace on a
    nonexistent file, each with a non-zero exit code
  - [x] errors surface via exit code/stderr, not folded into the JSON payload
  - [x] MCP tool wired per Phase 3's pattern (alongside `DocxTools.cs`), `module.manifest.json`
    updated
  - [x] unit tests under `tests/CapabilityModule.Office.Cli.Tests/` covering both the substitution and
    the versioning behavior; MCP-adapter tests under `tests/CapabilityModule.Office.Tests/`

### Phase 7 — Indexing infrastructure (Postgres + pgvector)

- Deliverable: stand up the storage backend for content search — a Postgres database with the
  `pgvector` extension, added as a new service in `docker-compose.yml`. This is this module's
  first stateful external dependency (see the architecture note above); everything through
  Phase 6 touches only the restricted-root filesystem.
- Decided (R&D-stage, revisit if the tenancy model changes): one Postgres+pgvector instance
  colocated with this module's own container, not shared across tenants — matches the existing
  entitlement-via-running-container model and avoids designing a multi-tenant schema before
  there's a second tenant to design for.
- Schema (initial pass — expect to iterate once real content/queries exist):
  - `documents`: id, restricted-root-relative path, content hash (detects when re-indexing is
    needed), source format, `metadata` JSONB (deliberately open-ended — indexed document types
    aren't fixed to one domain, so no rigid typed columns), created/updated timestamps.
  - `chunks`: id, `document_id` (FK), chunk index, chunk text, structural metadata (heading
    path/section, page or paragraph range where available), a generated `tsvector` column for
    keyword search, and a `vector` column (`pgvector`) — nullable until Phase 10 populates it, so
    chunking (Phase 9) can be validated independently of embedding cost/latency.
  - No `tenant_id` column — isolation is at the container/database level per the decision above.
    Naming and constraints stay tenant-agnostic so a future shared-instance model is an additive
    column, not a redesign.
- Open decision: migration tooling (plain versioned SQL scripts vs. a .NET migration framework).
  **Decided: plain SQL scripts** under `db/migrations/` with a `schema_migrations` tracking table,
  applied by `DbInitializer` on startup — consistent with the CLI-first approach and portable
  with no framework dependency.
- Exit criteria:
  - [x] Postgres + `pgvector` added as a service in `docker-compose.yml`, image version pinned
    (`pgvector/pgvector:pg17`)
  - [x] `documents` and `chunks` tables created via a migration, applied to the local compose
    environment
  - [x] connection config (host/port/credentials) sourced from `.env`, consistent with existing
    docker-compose `.env` loading — no hardcoded credentials
  - [x] a basic round-trip (insert a `documents` row, insert a `chunks` row referencing it, read
    both back) verified against the running container
  - [x] `/health` (or a new readiness check) reflects Postgres connectivity, not just the MCP
    server process being up

### Phase 8 — Format ingestion adapters (.docx, plain text, PDF)

- Deliverable: a small extraction-adapter abstraction in the CLI (e.g. `IContentExtractor` with
  an `extract(path) -> NormalizedDocument` shape carrying plain text plus whatever structure is
  recoverable — headings, paragraphs, page numbers) so chunking (Phase 9) operates against one
  normalized representation regardless of source format, rather than branching per file type
  inline. This is the ingestion-normalization layer flagged in this plan's design discussion —
  built as its own phase, and its own extension point, rather than folded silently into chunking.
- Decided: three adapters land in this phase, selected by file extension/content-type.
  - `.docx` adapter — conforms the existing `DocxEngine` (Phase 2) to the new interface; no new
    extraction logic, just wraps what's already built.
  - Plain-text adapter — conforms the existing `read` primitive (Phase 1) to the interface;
    paragraphs are the only recoverable structure (no headings), which is why chunking's fallback
    mode (Phase 9) is paragraph-based.
  - PDF adapter — new capability, not built in any earlier phase. Extracts text (and page
    numbers, where recoverable) via a PDF text-extraction library. Library choice TBD — flag a
    preference before this is built; default to a pure-.NET library (e.g. `PdfPig`) over shelling
    out to an external PDF binary, consistent with the reasoning Phase 5 used to keep `search`
    self-contained rather than wrapping `find`/`grep`. Scanned/image-only PDFs (no embedded text
    layer) are explicitly out of scope here — OCR is a separate, larger capability, not assumed
    in this phase.
- An unsupported file extension/format produces a clear "no adapter for this format" error from
  `ContentExtractorFactory` rather than being indexed as empty. In the single-lookup case (the
  factory called directly) that error propagates as a hard failure. In `index build`'s bulk
  directory walk (Phase 9) it's caught per file and recorded as a skip (counted in
  `filesSkipped`, visible in the JSON summary) rather than aborting the whole walk — a directory
  legitimately contains files with no adapter (`.gitkeep`, version-store backups, etc.), and
  failing the entire run on the first one would be worse than reporting it and continuing.
- Exit criteria:
  - [x] `IContentExtractor` (or equivalent) abstraction defined, returning normalized text plus
    whatever structure is recoverable for that format
  - [x] `.docx` adapter implemented, producing output equivalent to the existing `DocxEngine`
    extraction
  - [x] plain-text adapter implemented
  - [x] PDF adapter implemented, extracting text and page-level structure from PDFs that have an
    embedded text layer
  - [x] an unsupported file extension/format produces a clear error from
    `ContentExtractorFactory` (hard failure when looked up directly; a recorded per-file skip,
    not a silent no-op or empty-content result, when encountered during `index build`'s
    directory walk)
  - [x] a PDF with zero extractable text (scanned/image-only) is flagged distinctly rather than
    silently treated as a successful empty-content extraction
  - [x] unit tests under `tests/CapabilityModule.Office.Cli.Tests/` covering each adapter, including a
    multi-page/multi-heading PDF fixture

### Phase 9 — Content chunking

- Deliverable: an `index build` CLI command that walks a directory under the restricted root
  (recursively, like `search`), runs each file through the Phase 8 extraction adapters, splits
  the normalized text into chunks, and writes `documents`/`chunks` rows (text and structural
  metadata only — no vector yet, per Phase 7's schema).
- Decided: chunk boundaries follow document structure (headings/paragraphs) with a generic
  paragraph-based fallback when structure isn't available; target size ~200–500 tokens per
  chunk with ~10–20% overlap between adjacent chunks; table content is kept intact rather than
  flattened into surrounding prose. Exact token target/overlap are tunable — treat these as a
  starting point to validate against real content, not fixed permanently here.
- `index build` is idempotent per file via the `documents.content_hash` column — re-running
  against unchanged files is a no-op; a changed file replaces its prior chunks.
- Explicitly deferred: embeddings — this phase produces indexable text only, not vectors. Adding
  a new format is no longer deferred to this phase — that's Phase 8's extension point now.
- Exit criteria:
  - [x] `index build <path>` command added to the CLI, scoped to the restricted root (same
    `PathSecurity` sandboxing as `read`/`write`/`list`/`search`)
  - [x] chunking consumes the Phase 8 `IContentExtractor` output rather than calling any
    format-specific engine directly
  - [x] `.docx` and PDF files are chunked using recovered document structure (headings/pages);
    plain text is chunked via the generic paragraph-based fallback
  - [x] each chunk is written to the `chunks` table with document reference, chunk index, text,
    and structural metadata (heading path where available)
  - [x] re-running `index build` against unchanged files does not duplicate chunks (content-hash
    check); a changed file's prior chunks are replaced
  - [x] JSON output on stdout summarizing what was indexed (documents processed, chunks written),
    consistent with the CLI's machine-readable output contract
  - [x] rejects a path that traverses outside the restricted root, with a non-zero exit code
  - [x] errors surface via exit code/stderr, not folded into the JSON payload
  - [x] unit tests under `tests/CapabilityModule.Office.Cli.Tests/` covering chunk boundary behavior
    (structure-aware and fallback); the idempotent re-index case is covered by
    `IndexEngineTests.cs` against a real Postgres+pgvector container (Testcontainers — see the
    note after Phase 10's exit criteria)

### Phase 10 — Embeddings

- Deliverable: generate a vector embedding for each chunk lacking one and populate the `vector`
  column added in Phase 7.
- Decided: embedding generation goes through a provider-agnostic interface in the CLI (e.g. an
  `IEmbeddingProvider` with a single `embed(texts) -> vectors` method), not a hardcoded SDK call
  to one vendor. The concrete provider is a config value (`.env`/config, not a compile-time
  choice), specifically so cost/vendor can change without touching call sites.
- **Decided default provider: OpenAI `text-embedding-3-small`.** Chosen for affordability
  (~$0.02 per million tokens, among the cheapest hosted options), no infrastructure to run, and
  it's the de facto default across the RAG ecosystem, so tooling/assumptions elsewhere tend to
  line up with it. This is the concrete implementation built in this phase; other providers
  (Voyage AI, self-hosted open-source models via Ollama, etc.) remain swappable later via the
  `IEmbeddingProvider` config value without a redesign.
- **Important constraint the abstraction does *not* remove:** embedding vector spaces aren't
  compatible across providers or models (different dimensionality, different training). Switching
  providers later always means re-embedding every existing chunk from scratch, however clean the
  interface is — the abstraction saves a code rewrite, not a re-index.
- Requires a new credential (OpenAI API key), sourced from `.env`/config, never logged.
- Embedding generation is a batched step over chunks where `vector IS NULL` (or whose parent
  document's content hash changed since the last embed pass) — decide whether this runs as part
  of `index build` itself or as a separate `index embed` step before building; default to folding
  it into `index build` for a single command unless a reason to separate them emerges (e.g.
  wanting to re-chunk without re-spending on embeddings).
- Exit criteria:
  - [x] `IEmbeddingProvider` (or equivalent) interface defined in the CLI, with the concrete
    implementation selected via config rather than hardcoded
  - [x] OpenAI `text-embedding-3-small` implemented end to end as the default provider
  - [x] API key sourced from config/`.env`, never logged
  - [x] chunks lacking a vector are embedded and the `vector` column populated
  - [x] re-running against already-embedded, unchanged chunks does not re-embed (cost control)
  - [x] embedding failures (provider error, rate limit, timeout) surface via exit code/stderr,
    consistent with the CLI's error contract, without corrupting partially-written index state
  - [x] JSON output includes counts of chunks embedded vs. skipped
  - [x] the skip-already-embedded behavior is covered by `IndexEngineTests.cs` against a real
    Postgres+pgvector container, with a fake `IEmbeddingProvider` (schema-valid 1536-dim vectors,
    no real API calls — see the note below)

**Note on database integration testing.** `IndexEngineTests.cs` and `IndexSearchEngineDbTests.cs`
(`tests/CapabilityModule.Office.Cli.Tests/`) run against a real `pgvector/pgvector:pg17` container
spun up per test run via Testcontainers (`PostgresFixture.cs`), with the actual migration scripts
from `db/migrations/` applied — not a hand-maintained schema copy. This was added after the
Phase 7-11 architecture review found that no automated test had ever exercised the DB-backed code
path (`IndexEngine`, `IndexSearchEngine`'s SQL) — everything before this was unit-level only,
exercised manually via `docker compose up`. Standing the real thing up immediately caught a bug
that manual testing had missed: the subdirectory-filter parameter in `IndexSearchEngine.SearchAsync`
had no explicit `NpgsqlDbType`, so Postgres couldn't infer its type when the value was `null` — the
common case of an unscoped search — and every such query threw `42P08: could not determine data
type of parameter`. Fixed by setting `NpgsqlDbType.Text` explicitly. **Requires Docker running
locally** (same requirement as `docker compose up` for this module); there is no CI pipeline yet
to gate on this.

### Phase 11 — Semantic + hybrid search

- Deliverable: `index search` CLI command that queries the index — embeds the query text via the
  Phase 10 provider, runs a hybrid query combining vector similarity (`pgvector` cosine distance)
  and keyword relevance (`tsvector`/`ts_rank`), and returns ranked chunk results. Added as a
  subcommand of the existing `index` command (alongside `index build`), keeping the CLI surface
  clean and consistent with the `docx` subcommand pattern.
- Decided: hybrid (vector + keyword), not pure-semantic — precise-term content (defined terms,
  identifiers, exact phrasing, which matters especially for legal-style documents) depends on
  exact matches that embeddings alone can blur.
- Combination strategy: weighted sum with equal weights (0.5 each), NULL-safe so chunks
  contributing only one signal still score via the other. Weighting is tunable constant in
  `IndexSearchEngine` — refine once real query traffic exists.
- Each result includes the source document's restricted-root path (per the reference-
  composability contract established in Phase 1/5) so a hit can be piped into `docx read`/`read`
  for full context, plus the chunk's structural metadata (heading path) so results are
  self-describing without a round-trip.
- Explicitly deferred: automatically keeping the index in sync when `docx create`/`docx replace`
  (Phase 2/Phase 6) modify a file — for now, re-indexing is a manual `index build` re-run.
  Automatic reindex-on-write is a natural follow-up once the manual path is proven in practice.
- Exit criteria:
  - [x] `index search` subcommand added, taking free-text query input (plus optional path scope
    and limit)
  - [x] query text is embedded via the Phase 10 `IEmbeddingProvider` and compared against
    `chunks.vector` using cosine distance (`<=>`)
  - [x] keyword relevance (`tsvector`/`ts_rank` via `plainto_tsquery`) is combined with vector
    similarity into a single ranked result set (weighted sum, equal weights, NULL-safe)
  - [x] each result includes the source document path, chunk text, structural metadata, and
    individual vector/keyword/combined relevance scores
  - [x] rejects a path/root that traverses outside the restricted root, with a non-zero exit code
    (inherited from PathSecurity sandbox via the same pattern as every other command)
  - [x] errors (embedding failure, DB unavailable) surface via exit code/stderr, not folded into
    the JSON payload
  - [x] MCP tool wired per Phase 3's pattern (`IndexTools.cs` alongside `DocxTools.cs`),
    `module.manifest.json` updated (added `index_build` and `index_search`)
  - [x] unit tests under `tests/CapabilityModule.Office.Cli.Tests/` (record shape, input validation,
    constants); MCP-adapter tests under `tests/CapabilityModule.Office.Tests/` (input validation)

### Phase 12 — File management primitives (upload, delete)

- Deliverable: round out the CLI's CRUD surface with the two operations it's missing —
  ingesting an existing file's raw bytes (`upload`) and removing a stored file (`delete`).
  Everything through Phase 11 can create documents from scratch (`docx create`) or edit them
  in place (`write`, `docx replace`), but there is no way to bring an *existing* file's actual
  bytes (a `.docx`/`.pdf`/etc. the caller already has) into the restricted root, and no way to
  remove one. Both are needed before the web UI (Phase 14/15) can offer a real upload/delete
  experience.
- **`upload <path>`:** binary-safe write. `write` (Phase 1) uses `File.WriteAllText` and is
  scoped to text content; `upload` instead accepts base64-encoded content (via `--content-base64`
  or base64 text piped over stdin) and writes the decoded bytes with `File.WriteAllBytes`. Base64
  is the transport specifically because both of this command's callers — an MCP tool argument
  (JSON string) and, later, the web UI backend (Phase 14, re-encoding a multipart file upload) —
  can only pass text, not raw bytes, through their respective call boundaries.
  - Same `--mode create|overwrite` semantics as `write`. An `overwrite` of an existing file
    snapshots the pre-upload content to the Phase 6 version store first, using the same
    `_versions/` convention — mutating existing stored content gets versioned regardless of
    which command did the mutating, not just `docx replace`.
  - No file-type restriction — `upload` doesn't parse or validate the bytes as any particular
    format (that's the extraction adapters' job, Phase 8, if/when the file is indexed). An
    unreadable/corrupt upload of a claimed type is only caught later, if something tries to
    extract from it.
  - A configurable max upload size is enforced (reject oversized uploads with a clear error
    rather than an unbounded in-memory base64 decode) — default TBD, follow Phase 4's precedent
    of a sensible default that's easy to override via config, not a hardcoded constant.
  - Does not auto-trigger `index build` — consistent with Phase 11's existing deferred
    reindex-on-write behavior for `docx create`/`docx replace`.
- **`delete <path>`:** removes a file from the restricted root. Per the method-safety/
  reversibility gating in `agentic_guidance.xml` (already applied to `docx replace` in Phase 6),
  this is **not** a hard delete — it snapshots the file to the Phase 6 version store (as its
  final version) and then removes it from its original location, so the content is recoverable
  via the version store rather than being unrecoverably gone. A true hard-delete/purge of version
  history is explicitly out of scope here (no command for it yet).
  - Deleting a path that doesn't exist, or one outside the restricted root, is a non-zero-exit
    error, not a silent no-op.
- Exit criteria:
  - [ ] `upload <path>` command added, accepting base64 content via `--content-base64` or stdin
  - [ ] `upload` decodes and writes bytes correctly for a real binary fixture (round-tripped
    against a `.docx` and a `.pdf`, not just plain text)
  - [ ] `upload --mode overwrite` on an existing file snapshots the pre-upload content to the
    version store before overwriting
  - [ ] oversized upload content is rejected with a clear non-zero-exit error rather than
    silently truncated or OOM-ing the process
  - [ ] `delete <path>` command added; deleting snapshots to the version store, then removes the
    file from its original location
  - [ ] `delete` on a nonexistent path, or a path outside the restricted root, exits non-zero
    with a clear error
  - [ ] both commands emit machine-readable (JSON) output on stdout, consistent with the CLI's
    existing contract (path/resolved location; `upload` includes bytes written and, when
    versioned, the version number/path; `delete` includes the version path the content was
    snapshotted to)
  - [ ] MCP tools wired per Phase 3's pattern (new tool class, e.g. `FileTools.cs`),
    `module.manifest.json` updated with `upload_file` / `delete_file`
  - [ ] unit tests under `tests/CapabilityModule.Office.Cli.Tests/` (binary round-trip, oversized
    rejection, versioned-overwrite, versioned-delete, path-traversal rejection); MCP-adapter
    tests under `tests/CapabilityModule.Office.Tests/`

### Phase 13 — Web UI backend API

- Deliverable: a new ASP.NET Core project, **`src/CapabilityModule.Office.WebApi`** (proposed
  name — flag if you want something else before this is built), exposing a REST API that a
  browser SPA can call directly (no MCP protocol involved). Same CLI-first pattern as the MCP
  adapter: each endpoint shells out to the CLI via a `CliRunner`-equivalent rather than
  reimplementing any logic, so the web UI and MCP stay two front ends over one CLI, not two
  divergent implementations.
- Decided (per the architecture note above): standalone project/container, not new endpoints
  bolted onto the existing MCP sidecar — keeps the MCP host's container/health/scaling profile
  unchanged, at the cost of a second service to build/deploy/health-check. No authentication in
  this phase (see architecture note above).
- Endpoint surface (each wraps an existing or Phase-12 CLI command — no new CLI capability
  introduced by this phase):
  - Search — wraps `search` (Phase 5, filename) and `index search` (Phase 11, semantic/hybrid).
  - View — wraps `read`/`docx read` (Phase 1/2) to return extracted text for preview. Full-
    fidelity in-browser rendering (preserving original Word layout/formatting) is explicitly
    deferred — this phase returns extracted text, not a rendered document.
  - Upload — wraps `upload` (Phase 12), accepting a multipart/form-data file from the browser
    and re-encoding it to base64 before invoking the CLI.
  - Edit — wraps `docx replace` (Phase 6). **This endpoint has a hard dependency on Phase 6
    shipping first** — as of this writing Phase 6's exit criteria are unchecked, so this
    endpoint cannot be built until that command exists. Scoped to the same find/replace
    primitive Phase 6 offers, not free-form rich-text editing.
  - Delete — wraps `delete` (Phase 12).
- Exit criteria:
  - [ ] `src/CapabilityModule.Office.WebApi` builds standalone with `dotnet build`
  - [ ] `/health` endpoint, consistent with the existing module's health-check convention
  - [ ] search endpoint returns results from both filename search and semantic/hybrid search
  - [ ] view endpoint returns extracted text for a stored document
  - [ ] upload endpoint accepts a multipart file and stores it via the Phase 12 `upload` command
  - [ ] edit endpoint performs a find/replace via `docx replace` (blocked until Phase 6 lands)
  - [ ] delete endpoint removes a stored file via the Phase 12 `delete` command
  - [ ] all endpoints reject paths outside the restricted root with a clear error status, not a
    500 or a silent no-op
  - [ ] added as a new service in `docker-compose.yml`
  - [ ] integration/adapter tests under a new `tests/CapabilityModule.Office.WebApi.Tests/`
    project, following the existing MCP-adapter test pattern

### Phase 14 — Web UI frontend

- Deliverable: a React/TypeScript single-page app (proposed location: `web/`, sibling to `src/`
  — flag if you want it elsewhere) consuming the Phase 13 REST API. Views: a document
  list/search page, a read-only preview pane (extracted text, per Phase 13's scoping), an upload
  dialog, an edit (find/replace) dialog, and delete with a confirmation step (delete is
  recoverable via the version store per Phase 12, but the UI still confirms before calling it —
  it's still a destructive-feeling action from the user's perspective).
- Decided: production build serves the compiled static SPA assets from the Phase 13 `WebApi`
  project (one container for the whole web UI, separate from the MCP sidecar container per the
  architecture note above) rather than running a separate frontend server/container. Local dev
  can still use the SPA's own dev server against the API for hot reload.
- No authentication (per the architecture note above) — this is a UI-only phase, no new access
  control introduced here.
- Exit criteria:
  - [ ] SPA scaffolded under `web/`, builds standalone
  - [ ] document list/search page, calling Phase 13's search endpoint (both filename and
    semantic/hybrid search)
  - [ ] preview pane showing extracted text for a selected document
  - [ ] upload dialog, round-tripped against a real file end to end through Phase 13's endpoint
  - [ ] edit (find/replace) dialog, round-tripped end to end (blocked until Phase 13's edit
    endpoint is unblocked by Phase 6)
  - [ ] delete flow with a confirmation step before calling the delete endpoint
  - [ ] production build's static assets are served from the `WebApi` project; verified via
    `docker compose up --build`

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
