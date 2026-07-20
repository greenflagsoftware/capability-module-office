import { useState, useRef, useCallback } from "react";

interface UploadDialogProps {
  onClose: () => void;
  onUploaded: (path: string) => void;
}

export default function UploadDialog({
  onClose,
  onUploaded,
}: UploadDialogProps) {
  const [file, setFile] = useState<File | null>(null);
  const [dragOver, setDragOver] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFile = useCallback((f: File) => {
    setFile(f);
    setError("");
  }, []);

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      setDragOver(false);
      const dropped = e.dataTransfer.files[0];
      if (dropped) handleFile(dropped);
    },
    [handleFile],
  );

  const handleUpload = useCallback(async () => {
    if (!file) return;

    setUploading(true);
    setError("");

    try {
      const { uploadFile } = await import("../api/client");
      const res = await uploadFile(file, file.name, "create");
      onUploaded(res.path);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed");
    } finally {
      setUploading(false);
    }
  }, [file, onUploaded, onClose]);

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Upload Document</h2>
          <button className="modal-close" onClick={onClose}>
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
              <path
                d="M4 4l8 8M12 4l-8 8"
                stroke="currentColor"
                strokeWidth="1.5"
                strokeLinecap="round"
              />
            </svg>
          </button>
        </div>
        <div className="modal-body">
          <input
            ref={inputRef}
            type="file"
            hidden
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) handleFile(f);
            }}
          />

          <div
            className={`file-drop-zone ${dragOver ? "drag-over" : ""} ${file ? "has-file" : ""}`}
            onClick={() => inputRef.current?.click()}
            onDragOver={(e) => {
              e.preventDefault();
              setDragOver(true);
            }}
            onDragLeave={() => setDragOver(false)}
            onDrop={handleDrop}
          >
            {file ? (
              <>
                <svg
                  width="32"
                  height="32"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.5"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                >
                  <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
                  <path d="M14 2v6h6" />
                  <path d="M9 15l3-3 3 3" />
                  <path d="M12 18v-6" />
                </svg>
                <p className="file-name">{file.name}</p>
                <p style={{ fontSize: "0.78rem", marginTop: 4 }}>
                  {(file.size / 1024).toFixed(1)} KB
                </p>
              </>
            ) : (
              <>
                <svg
                  width="32"
                  height="32"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.5"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                >
                  <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4" />
                  <path d="M17 8l-5-5-5 5" />
                  <path d="M12 3v12" />
                </svg>
                <p>Click or drag a file here to upload</p>
              </>
            )}
          </div>

          {error && (
            <p
              style={{
                color: "var(--danger)",
                fontSize: "0.82rem",
                marginTop: 8,
              }}
            >
              {error}
            </p>
          )}
        </div>
        <div className="modal-footer">
          <button className="btn" onClick={onClose} disabled={uploading}>
            Cancel
          </button>
          <button
            className="btn btn-primary"
            onClick={handleUpload}
            disabled={!file || uploading}
          >
            {uploading ? (
              <>
                <span className="spinner" />
                Uploading...
              </>
            ) : (
              "Upload"
            )}
          </button>
        </div>
      </div>
    </div>
  );
}