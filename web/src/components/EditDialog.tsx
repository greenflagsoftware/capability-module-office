import { useState } from "react";

interface EditDialogProps {
  path: string;
  onClose: () => void;
  onEdited: (path: string) => void;
}

export default function EditDialog({
  path,
  onClose,
  onEdited,
}: EditDialogProps) {
  const [find, setFind] = useState("");
  const [replace, setReplace] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  const handleSave = async () => {
    if (!find.trim()) return;

    setSaving(true);
    setError("");

    try {
      const { editDocument } = await import("../api/client");
      const res = await editDocument(path, find, replace);
      onEdited(res.path);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Edit failed");
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Edit Document</h2>
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
          <p
            style={{
              fontSize: "0.82rem",
              color: "var(--text-secondary)",
              marginBottom: 16,
            }}
          >
            Performing a find-and-replace in{" "}
            <strong style={{ color: "var(--text-primary)" }}>
              {path.split("/").pop() || path.split("\\").pop() || path}
            </strong>
          </p>

          <div className="form-group">
            <label htmlFor="edit-find">Find</label>
            <input
              id="edit-find"
              type="text"
              placeholder="Text to find..."
              value={find}
              onChange={(e) => setFind(e.target.value)}
              autoFocus
            />
          </div>

          <div className="form-group">
            <label htmlFor="edit-replace">Replace with</label>
            <input
              id="edit-replace"
              type="text"
              placeholder="Replacement text (optional)"
              value={replace}
              onChange={(e) => setReplace(e.target.value)}
            />
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
          <button className="btn" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button
            className="btn btn-primary"
            onClick={handleSave}
            disabled={!find.trim() || saving}
          >
            {saving ? (
              <>
                <span className="spinner" />
                Saving...
              </>
            ) : (
              "Replace"
            )}
          </button>
        </div>
      </div>
    </div>
  );
}