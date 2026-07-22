import { downloadUrl } from "../api/client";
import type { ViewResponse } from "../types";

interface PreviewPaneProps {
  document: ViewResponse | null;
  loading: boolean;
  onEdit: () => void;
  onDelete: () => void;
}

export default function PreviewPane({
  document,
  loading,
  onEdit,
  onDelete,
}: PreviewPaneProps) {
  if (loading) {
    return (
      <div className="preview-loading">
        <span className="spinner" />
        Loading document...
      </div>
    );
  }

  if (!document) {
    return (
      <div className="preview-empty">
        <div className="empty-state">
          <svg
            width="48"
            height="48"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
            <path d="M14 2v6h6" />
            <path d="M16 13H8" />
            <path d="M16 17H8" />
            <path d="M10 9H8" />
          </svg>
          <p>Select a document to preview</p>
        </div>
      </div>
    );
  }

  const name =
    document.path.split("/").pop() || document.path.split("\\").pop() || document.path;

  return (
    <>
      <div className="preview-header">
        <div className="preview-header-left">
          <h2>{name}</h2>
          <span className="preview-format-badge">{document.format}</span>
        </div>
        <div className="preview-actions">
          <a
            className="btn btn-sm"
            href={downloadUrl(document.path)}
            download={name}
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4" />
              <path d="M7 10l5 5 5-5" />
              <path d="M12 15V3" />
            </svg>
            Download
          </a>
          {document.format === "docx" && (
            <button className="btn btn-sm" onClick={onEdit}>
              <svg
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7" />
                <path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z" />
              </svg>
              Edit
            </button>
          )}
          <button className="btn btn-sm btn-danger" onClick={onDelete}>
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M3 6h18" />
              <path d="M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2" />
            </svg>
            Delete
          </button>
        </div>
      </div>
      <div className="preview-body">{document.content || "(empty document)"}</div>
    </>
  );
}
