import { useState, useCallback, useRef, useEffect, useMemo } from "react";
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
  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(new Set());
  const inputRef = useRef<HTMLInputElement>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>();

  const doSearch = useCallback(
    async (q: string) => {
      const trimmed = q.trim();
      setExpandedGroups(new Set());
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

  // Hybrid search returns one row per matching chunk, so a single document
  // with many relevant chunks (e.g. "bread" hitting a 14-chunk contract)
  // otherwise floods the list with rows that all point at the same file.
  // Group by path, preserving the backend's score-descending order via the
  // first-seen index of each document's best-ranked chunk.
  const groupedResults = useMemo(() => {
    const order: string[] = [];
    const byPath = new Map<string, (SearchEntry | HybridSearchEntry)[]>();
    for (const entry of results) {
      if (!byPath.has(entry.path)) {
        byPath.set(entry.path, []);
        order.push(entry.path);
      }
      byPath.get(entry.path)!.push(entry);
    }
    return order.map((path) => ({ path, entries: byPath.get(path)! }));
  }, [results]);

  const toggleGroupExpanded = useCallback((path: string) => {
    setExpandedGroups((prev) => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }, []);

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
              {mode === "hybrid" && groupedResults.length !== totalResults
                ? `${totalResults} match${totalResults !== 1 ? "es" : ""} in ${groupedResults.length} document${groupedResults.length !== 1 ? "s" : ""}`
                : `${totalResults} result${totalResults !== 1 ? "s" : ""}`}
            </div>
            {groupedResults.map((group) => {
              const [primary, ...extraChunks] = group.entries;
              const isExpanded = expandedGroups.has(group.path);
              return (
                <div key={group.path} className="result-group">
                  <button
                    className={`result-item ${isSelected(primary) ? "selected" : ""}`}
                    onClick={() => handleSelect(primary)}
                  >
                    <div className={`result-icon ${getIcon(primary)}`}>
                      {getIcon(primary) === "docx" && "W"}
                      {getIcon(primary) === "pdf" && "P"}
                      {getIcon(primary) === "text" && "T"}
                      {getIcon(primary) === "image" && "I"}
                      {getIcon(primary) === "other" && "F"}
                    </div>
                    <div className="result-info">
                      <div className="result-name">{getName(primary)}</div>
                      <div className="result-path">{getDir(primary) || "/"}</div>
                      {"chunkText" in primary && primary.chunkText && (
                        <div className="hybrid-result-text">
                          {primary.chunkText}
                        </div>
                      )}
                      {"chunkText" in primary && (
                        <div className="hybrid-scores">
                          {group.entries.length > 1 ? (
                            <span className="score-badge">
                              {group.entries.length} matches
                            </span>
                          ) : (
                            "score" in primary &&
                            primary.score != null && (
                              <span className="score-badge">
                                {(primary as HybridSearchEntry).score.toFixed(2)}
                              </span>
                            )
                          )}
                        </div>
                      )}
                    </div>
                  </button>

                  {extraChunks.length > 0 && (
                    <>
                      <button
                        className="result-chunk-toggle"
                        onClick={() => toggleGroupExpanded(group.path)}
                      >
                        {isExpanded
                          ? "Hide additional matches"
                          : `Show ${extraChunks.length} more match${extraChunks.length !== 1 ? "es" : ""} in this document`}
                      </button>
                      {isExpanded && (
                        <div className="result-chunk-list">
                          {extraChunks.map((chunk, idx) => (
                            <button
                              key={`${group.path}-chunk-${idx}`}
                              className={`result-chunk ${isSelected(chunk) ? "selected" : ""}`}
                              onClick={() => handleSelect(chunk)}
                            >
                              {"chunkText" in chunk && chunk.chunkText && (
                                <span className="result-chunk-text">
                                  {chunk.chunkText}
                                </span>
                              )}
                              {"score" in chunk && chunk.score != null && (
                                <span className="score-badge">
                                  {(chunk as HybridSearchEntry).score.toFixed(2)}
                                </span>
                              )}
                            </button>
                          ))}
                        </div>
                      )}
                    </>
                  )}
                </div>
              );
            })}
          </>
        )}
      </div>
    </>
  );
}
