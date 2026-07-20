export interface SearchEntry {
  name: string;
  path: string;
  type: string;
}

export interface HybridSearchEntry {
  path: string;
  chunkText: string;
  headingPath: string | null;
  score: number;
  vectorScore: number | null;
  keywordScore: number | null;
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
}

export interface ApiError {
  error?: string;
  detail?: string;
  title?: string;
  status?: number;
}
