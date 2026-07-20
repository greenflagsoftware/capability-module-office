import { useState, useCallback, useRef, useEffect } from "react";
import {
  searchFilename,
  searchHybrid,
} from "../api/client";
import type {
  SearchEntry,
  HybridSearchEntry,
} from "../types";

interface SearchPanelProps {
  onSelectDocument: (path: string) => void;
  selectedPath: string | null;
}

type SearchMode = "filename" | "hybrid";

export default function SearchPanel({
  onSelectDocument,
  selectedPath,
}: SearchPanelProps) {
  const [query, setQuery] = useState("");
  const [mode, setMode] = useState<SearchMode>("hybrid");
  const [results, setResults] = useState<
    (SearchEntry | HybridSearchEntry)[]
  >([]);
  const [totalResults, setTotalResults] = useState(0);
  const [loading, setLoading] = useState(false);
  const [hasSearched, setHasSearched] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>();

  const doSearch = useCallback(
    async (q: string) => {
      const trimmed = q.trim();
      if (!trimmed) {
        setResults([]);
        setTotalResults(0);
        setHasSearched(false);
        return;
      }

      setLoading(true);
      try {
        if (mode === "filename") {
          const res = await searchFilename(trimmed);
          setResults(res.entries);
          setTotalResults(res.totalResults);
        } else {
          const res = await searchHybrid(trimmed);
          setResults(res.entries);
          setTotalResults(res.totalResults);
        }
        setHasSearched(true);
      } catch {
        setResults([]);
        setTotalResults(0);
        setHasSearched(true);
      } finally {
        setLoading(false);
      }
    },
    [mode],
  );

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => doSearch(query), 250);
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, [query, doSearch]);

  const toggleMode = useCallback(() => {
    setMode((m) => (m === "filename" ? "hybrid" : "filename"));
  }, []);

  const handleSelect = useCallback(
    (entry: SearchEntry | HybridSearchEntry) => {
      onSelectDocument(entry.path);
    },
    [onSelectDocument],
  );

  const getIcon = (entry: SearchEntry | HybridSearchEntry) => {
    const path = entry.path.toLowerCase();
    if (path.endsWith(".docx")) return "docx";
    if (path.endsWith(".pdf")) return "pdf";
    if (path.endsWith(".txt") || path.endsWith(".md")) return "text";
    if (
      path.endsWith(".png") ||
      path.endsWith(".jpg") ||
      path.endsWith(".jpeg") ||
      path.endsWith(".gif") ||
      path.endsWith(".svg")
    )
      return "image";
    return "other";
  };

  const getName = (entry: SearchEntry | HybridSearchEntry) => {
    const parts = entry.path.replace(/\\/g, "/").split("/");
    return parts[parts.length - 1] || entry.path;
  };

  const getDir = (entry: SearchEntry | HybridSearchEntry) => {
    const parts = entry.path.replace(/\\/g, "/").split("/");
    return parts.length > 1 ? parts.slice(0, -1).join("/") : "";
  };

  const isSelected = (entry: SearchEntry | HybridSearchEntry) =>
    entry.path === selectedPath;

  return (
    <>
      <div className="search-section">
        <div className="search-input-wrapper">
          <button
            className="search-mode-toggle"
            onClick={toggleMode}
            title={
              mode === "filename"
                ? "Switch to hybrid search"
                : "Switch to filename search"
            }
          >
            <svg
              width="12"
              height="12"
              viewBox="0 0 16 16"
              fill="none"
            >
              <path
                d="M8 3v10M3 8h10"
                stroke="currentColor"
                strokeWidth="1.5"
                strokeLinecap="round"
              />
            </svg>
            {mode === "filename" ? "Filename" : "Hybrid"}
          </button>
          <input
            ref={inputRef}
            type="text"
            placeholder={
              mode === "filename"
                ? "Search files by name..."
                : "Search document content..."
            }
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
          {query && (
            <button
              className="search-clear-btn"
              onClick={() => setQuery("")}
            >
              <svg width="14" height="14" viewBox="0 0 16 16" fill="none">
                <path
                  d="M4 4l8 8M12 4l-8 8"
                  stroke="currentColor"
                  strokeWidth="1.5"
                  strokeLinecap="round"
                />
              </svg>
            </button>
          )}
        </div>
      </div>

      <div className="search-results">
        {loading && (
          <div
            style={{
              display: "flex",
              justifyContent: "center",
              padding: 24,
            }}
          >
            <span className="spinner" />
          </div>
        )}

        {!loading && hasSearched && results.length === 0 && (
          <div className="empty-state">
            <svg
              width="40"
              height="40"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <circle cx="11" cy="11" r="8" />
              <path d="M21 21l-4.35-4.35" />
            </svg>
            <p>No documents found</p>
          </div>
        )}

        {!loading && !hasSearched && (
          <div className="empty-state">
            <svg
              width="40"
              height="40"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
              <path d="M14 2v6h6" />
              <path d="M12 18v-6" />
              <path d="M9 15l3-3 3 3" />
            </svg>
            <p>Search for documents to get started</p>
          </div>
        )}

        {!loading && hasSearched && results.length > 0 && (
          <>
            <div className="search-meta">
              {totalResults} result{totalResults !== 1 ? "s" : ""}
            </div>
            {results.map((entry, idx) => (
              <button
                key={`${entry.path}-${idx}`}
                className={`result-item ${isSelected(entry) ? "selected" : ""}`}
                onClick={() => handleSelect(entry)}
              >
                <div className={`result-icon ${getIcon(entry)}`}>
                  {getIcon(entry) === "docx" && "W"}
                  {getIcon(entry) === "pdf" && "P"}
                  {getIcon(entry) === "text" && "T"}
                  {getIcon(entry) === "image" && "I"}
                  {getIcon(entry) === "other" && "F"}
                </div>
                <div className="result-info">
                  <div className="result-name">{getName(entry)}</div>
                  <div className="result-path">{getDir(entry) || "/"}</div>
                  {"chunkText" in entry && entry.chunkText && (
                    <div className="hybrid-result-text">
                      {entry.chunkText}
                    </div>
                  )}
                  {"chunkText" in entry && (
                    <div className="hybrid-scores">
                      {"score" in entry && entry.score != null && (
                        <span className="score-badge">
                          {(entry as HybridSearchEntry).score.toFixed(2)}
                        </span>
                      )}
                    </div>
                  )}
                </div>
              </button>
            ))}
          </>
        )}
      </div>
    </>
  );
}
