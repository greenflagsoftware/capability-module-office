import type {
  SearchResponse,
  HybridSearchResponse,
  ViewResponse,
  UploadResponse,
  EditResponse,
  DeleteResponse,
  IndexResponse,
} from "../types";

const BASE_URL = "";

async function apiFetch<T>(url: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${url}`, {
    headers: { "Accept": "application/json" },
    ...options,
  });

  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    const message = body.detail || body.error || body.title || `HTTP ${res.status}`;
    throw new Error(message);
  }

  return res.json();
}

export async function searchFilename(
  q: string,
  path?: string,
): Promise<SearchResponse> {
  const params = new URLSearchParams({ q });
  if (path) params.set("path", path);
  params.set("mode", "filename");
  return apiFetch<SearchResponse>(`/search?${params}`);
}

export async function searchHybrid(
  q: string,
  path?: string,
): Promise<HybridSearchResponse> {
  const params = new URLSearchParams({ q });
  if (path) params.set("path", path);
  params.set("mode", "hybrid");
  return apiFetch<HybridSearchResponse>(`/search?${params}`);
}

export async function viewDocument(path: string): Promise<ViewResponse> {
  const params = new URLSearchParams({ path });
  return apiFetch<ViewResponse>(`/view?${params}`);
}

export async function uploadFile(
  file: File,
  destPath: string,
  mode: "create" | "overwrite" = "create",
): Promise<UploadResponse> {
  const formData = new FormData();
  formData.set("path", destPath);
  formData.set("mode", mode);
  formData.append("file", file);

  const res = await fetch(`${BASE_URL}/upload`, {
    method: "POST",
    body: formData,
  });

  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    const message = body.detail || body.error || body.title || `HTTP ${res.status}`;
    throw new Error(message);
  }

  return res.json();
}

export async function editDocument(
  path: string,
  find: string,
  replace: string,
): Promise<EditResponse> {
  return apiFetch<EditResponse>("/edit", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ path, find, replace }),
  });
}

export async function deleteDocument(path: string): Promise<DeleteResponse> {
  const params = new URLSearchParams({ path });
  return apiFetch<DeleteResponse>(`/delete?${params}`, {
    method: "DELETE",
  });
}

export async function indexBuild(path?: string): Promise<IndexResponse> {
  const params = new URLSearchParams();
  if (path) params.set("path", path);
  const qs = params.toString();
  return apiFetch<IndexResponse>(`/index${qs ? `?${qs}` : ""}`, {
    method: "POST",
  });
}
