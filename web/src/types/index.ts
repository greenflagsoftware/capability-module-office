export interface SearchEntry {
  name: string;
  path: string;
  type: string;
}

// Normalized shape used throughout the UI — see RawHybridSearchEntry below
// for what the /search?mode=hybrid endpoint actually returns on the wire.
export interface HybridSearchEntry {
  path: string;
  chunkIndex: number;
  chunkText: string;
  headingPath?: string[];
  score: number;
  vectorScore: number;
  keywordScore: number;
}

// The actual JSON shape returned by GET /search?mode=hybrid (mirrors the
// CLI's `index search` output field names, which differ from the UI's
// normalized HybridSearchEntry above — documentPath/text vs. path/chunkText).
// Mapped to HybridSearchEntry in api/client.ts before reaching components.
export interface RawHybridSearchEntry {
  documentPath: string;
  chunkIndex: number;
  text: string;
  headingPath?: string[];
  score: number;
  vectorScore: number;
  keywordScore: number;
}

export interface SearchResponse {
  query: string;
  mode: "filename" | "hybrid";
  path: string;
  totalResults: number;
  entries: SearchEntry[];
}

export interface HybridSearchResponse {
  query: string;
  mode: "hybrid";
  path: string;
  totalResults: number;
  entries: HybridSearchEntry[];
}

export interface RawHybridSearchResponse {
  query: string;
  mode: "hybrid";
  path: string;
  totalResults: number;
  entries: RawHybridSearchEntry[];
}

export interface ViewResponse {
  path: string;
  resolved: string;
  content: string;
  format: "docx" | "text";
}

export interface UploadResponse {
  path: string;
  resolved: string;
  bytesWritten: number;
  version: number | null;
  versionPath: string | null;
}

export interface EditResponse {
  path: string;
  resolved: string;
  version: number;
  versionPath: string;
  lastModifiedUtc: string;
}

export interface DeleteResponse {
  path: string;
  resolved: string;
  version: number;
  versionPath: string;
  indexRemoved: boolean | null;
}

export interface IndexResponse {
  path: string;
  resolved: string;
  filesProcessed: number;
  filesIndexed: number;
  filesUnchanged: number;
  filesSkipped: number;
  filesWithErrors: number;
  totalChunksWritten: number;
  totalChunksEmbedded: number;
  existingChunksEmbedded: number;
}

export interface ApiError {
  error?: string;
  detail?: string;
  title?: string;
  status?: number;
}
