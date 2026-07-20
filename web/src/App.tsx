import { useState, useCallback } from "react";
import SearchPanel from "./components/SearchPanel";
import PreviewPane from "./components/PreviewPane";
import UploadDialog from "./components/UploadDialog";
import EditDialog from "./components/EditDialog";
import ConfirmDialog from "./components/ConfirmDialog";
import { ToastProvider, useToast } from "./components/Toast";
import { viewDocument, deleteDocument, indexBuild } from "./api/client";
import type { ViewResponse } from "./types";

function AppInner() {
  const { showToast } = useToast();
  const [selectedPath, setSelectedPath] = useState<string | null>(null);
  const [document, setDocument] = useState<ViewResponse | null>(null);
  const [loadingPreview, setLoadingPreview] = useState(false);

  const [showUpload, setShowUpload] = useState(false);
  const [showEdit, setShowEdit] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [reindexing, setReindexing] = useState(false);

  const loadDocument = useCallback(async (path: string) => {
    setLoadingPreview(true);
    try {
      const doc = await viewDocument(path);
      setDocument(doc);
    } catch (err) {
      setDocument(null);
      showToast(
        err instanceof Error ? err.message : "Failed to load document",
        "error",
      );
    } finally {
      setLoadingPreview(false);
    }
  }, [showToast]);

  const handleSelectDocument = useCallback(
    (path: string) => {
      setSelectedPath(path);
      loadDocument(path);
    },
    [loadDocument],
  );

  const handleUploaded = useCallback(
    (path: string) => {
      showToast(`Uploaded "${path}"`, "success");
      setSelectedPath(path);
      loadDocument(path);

      // Reindex so the new content is searchable via hybrid search. Runs in
      // the background — the upload itself already succeeded, so a slow or
      // failed index build shouldn't block the UI, just get surfaced.
      indexBuild()
        .then((summary) => {
          if (summary.filesWithErrors > 0) {
            showToast(
              `Indexed with ${summary.filesWithErrors} error(s) — see server logs`,
              "error",
            );
          }
        })
        .catch((err) => {
          showToast(
            err instanceof Error
              ? `Search indexing failed: ${err.message}`
              : "Search indexing failed",
            "error",
          );
        });
    },
    [loadDocument, showToast],
  );

  const handleEdited = useCallback(
    (path: string) => {
      showToast("Document updated", "success");
      loadDocument(path);
    },
    [loadDocument, showToast],
  );

  const handleReindex = useCallback(async () => {
    setReindexing(true);
    try {
      const summary = await indexBuild();
      showToast(
        summary.filesWithErrors > 0
          ? `Reindexed with ${summary.filesWithErrors} error(s) — ${summary.filesIndexed} file(s) updated`
          : `Reindexed: ${summary.filesIndexed} updated, ${summary.filesUnchanged} unchanged`,
        summary.filesWithErrors > 0 ? "error" : "success",
      );
    } catch (err) {
      showToast(
        err instanceof Error ? `Reindex failed: ${err.message}` : "Reindex failed",
        "error",
      );
    } finally {
      setReindexing(false);
    }
  }, [showToast]);

  const handleDelete = useCallback(async () => {
    if (!selectedPath) return;
    try {
      const res = await deleteDocument(selectedPath);
      showToast(
        res.indexRemoved
          ? `Deleted "${selectedPath}" and removed it from the search index`
          : `Deleted "${selectedPath}"`,
        "success",
      );
      setSelectedPath(null);
      setDocument(null);
      setShowDeleteConfirm(false);
    } catch (err) {
      showToast(
        err instanceof Error ? err.message : "Failed to delete document",
        "error",
      );
      setShowDeleteConfirm(false);
    }
  }, [selectedPath, showToast]);

  return (
    <>
      <header className="app-header">
        <h1>Office Documents</h1>
        <div className="header-actions">
          <button
            className="btn"
            onClick={handleReindex}
            disabled={reindexing}
            title="Rebuild the search index for the whole document root"
          >
            {reindexing ? (
              <>
                <span className="spinner" />
                Reindexing...
              </>
            ) : (
              <>
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
                  <path d="M23 4v6h-6" />
                  <path d="M1 20v-6h6" />
                  <path d="M3.51 9a9 9 0 0114.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0020.49 15" />
                </svg>
                Reindex
              </>
            )}
          </button>
          <button className="btn btn-primary" onClick={() => setShowUpload(true)}>
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
              <path d="M17 8l-5-5-5 5" />
              <path d="M12 3v12" />
            </svg>
            Upload
          </button>
        </div>
      </header>

      <div className="app-layout">
        <div className="sidebar">
          <SearchPanel
            onSelectDocument={handleSelectDocument}
            selectedPath={selectedPath}
          />
        </div>

        <div className="sidebar-resizer" />

        <div className="main-content">
          <PreviewPane
            document={document}
            loading={loadingPreview}
            onEdit={() => setShowEdit(true)}
            onDelete={() => setShowDeleteConfirm(true)}
          />
        </div>
      </div>

      {showUpload && (
        <UploadDialog
          onClose={() => setShowUpload(false)}
          onUploaded={handleUploaded}
        />
      )}

      {showEdit && selectedPath && (
        <EditDialog
          path={selectedPath}
          onClose={() => setShowEdit(false)}
          onEdited={handleEdited}
        />
      )}

      {showDeleteConfirm && selectedPath && (
        <ConfirmDialog
          title="Delete Document"
          message={`Are you sure you want to delete "${selectedPath}"? A copy will be saved to the version store so it can be recovered later.`}
          confirmLabel="Delete"
          confirmDanger
          onConfirm={handleDelete}
          onCancel={() => setShowDeleteConfirm(false)}
        />
      )}
    </>
  );
}

export default function App() {
  return (
    <ToastProvider>
      <AppInner />
    </ToastProvider>
  );
}