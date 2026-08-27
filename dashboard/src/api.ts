import type {
  Definition,
  Detail,
  ExperimentListItem,
  Report,
} from "./contracts";
export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public correlationId?: string,
  ) {
    super(message);
  }
}
export class Api {
  constructor(
    private key: string,
    private base = import.meta.env.VITE_API_BASE_URL || "http://127.0.0.1:5080",
  ) {}
  async request<T>(
    path: string,
    body?: unknown,
    signal?: AbortSignal,
  ): Promise<T> {
    const response = await fetch(this.base.replace(/\/$/, "") + path, {
      method: body === undefined ? "GET" : "POST",
      signal,
      headers: {
        "X-SimOps-Admin-Key": this.key,
        ...(body === undefined ? {} : { "Content-Type": "application/json" }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: "omit",
      cache: "no-store",
      redirect: "error",
    });
    if (!response.ok) {
      const error = await response.json().catch(() => ({}));
      throw new ApiError(
        response.status,
        error.code || "REQUEST_FAILED",
        error.message || `HTTP ${response.status}`,
        error.correlationId,
      );
    }
    return response.json() as Promise<T>;
  }
  list(signal?: AbortSignal) {
    return this.request<ExperimentListItem[]>(
      "/api/v1/experiments",
      undefined,
      signal,
    );
  }
  detail(id: string, signal?: AbortSignal) {
    return this.request<Detail>(
      `/api/v1/experiments/${encodeURIComponent(id)}`,
      undefined,
      signal,
    );
  }
  results(id: string, full = false, signal?: AbortSignal) {
    return this.request<Report>(
      `/api/v1/experiments/${encodeURIComponent(id)}/results?full=${full}`,
      undefined,
      signal,
    );
  }
  template() {
    return this.request<Definition>("/api/v1/catalog/experiment-template");
  }
}
export const percent = (value: number | null | undefined) =>
  value == null ? "관측 없음" : `${(value * 100).toFixed(1)}%`;
export const points = (value: number) => `${(value * 100).toFixed(3)}%p`;
