-- 001_create_documents_and_chunks.sql
-- Phase 7: Indexing infrastructure (Postgres + pgvector)
--
-- Creates the core indexing tables for content search. The documents table
-- tracks indexed files by their restricted-root-relative path; the chunks
-- table holds the split text content with structural metadata, a generated
-- tsvector for keyword search, and a nullable pgvector column (populated
-- in Phase 10).
--
-- Tenant-agnostic (no tenant_id column) — isolation is at the
-- container/database level per the architecture decision recorded in the
-- development plan. Adding tenant support later is an additive column, not
-- a redesign.

-- Enable pgvector extension (idempotent)
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS documents (
    id              BIGSERIAL PRIMARY KEY,
    -- Path relative to the restricted root, e.g. "reports/q4-summary.docx"
    relative_path   TEXT NOT NULL,
    -- Hash of the file content at last index time; used to detect re-index
    -- necessity without re-reading and re-chunking the whole file.
    content_hash    TEXT NOT NULL,
    -- Source format identifier, e.g. "docx", "txt", "pdf"
    source_format   TEXT NOT NULL,
    -- Open-ended metadata JSONB — indexed document types aren't limited to
    -- one domain so no rigid typed columns.
    metadata        JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_documents_relative_path ON documents (relative_path);

CREATE TABLE IF NOT EXISTS chunks (
    id              BIGSERIAL PRIMARY KEY,
    document_id     BIGINT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    -- Ordinal position of this chunk within the document (0-based)
    chunk_index     INT NOT NULL,
    -- The extracted text of this chunk
    chunk_text      TEXT NOT NULL,
    -- Structural context: e.g. the heading path for a section-based chunk,
    -- page range for a PDF, or NULL for plain-text fallback chunks.
    -- Stored as a JSON array of strings: ["Section 1", "Subsection A"]
    heading_path    JSONB,
    page_range      INT8RANGE,
    -- Generated tsvector for keyword search (kept in sync via trigger)
    search_vector   TSVECTOR,
    -- Vector embedding — nullable until Phase 10 populates it.
    -- 1536 dimensions matches OpenAI text-embedding-3-small.
    vector          vector(1536),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON chunks (document_id);
CREATE INDEX IF NOT EXISTS idx_chunks_chunk_index ON chunks (document_id, chunk_index);
CREATE INDEX IF NOT EXISTS idx_chunks_search_vector ON chunks USING GIN (search_vector);

-- Trigger function to keep search_vector in sync with chunk_text
CREATE OR REPLACE FUNCTION chunks_search_vector_update() RETURNS trigger AS $$
BEGIN
    NEW.search_vector := to_tsvector('english', COALESCE(NEW.chunk_text, ''));
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_chunks_search_vector ON chunks;
CREATE TRIGGER trg_chunks_search_vector
    BEFORE INSERT OR UPDATE OF chunk_text ON chunks
    FOR EACH ROW
    EXECUTE FUNCTION chunks_search_vector_update();
