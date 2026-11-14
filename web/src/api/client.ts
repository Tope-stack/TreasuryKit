import createClient, { type Middleware } from 'openapi-fetch';
import type { paths } from './schema';
import { loadSession } from '../auth/session';

/** RFC 7807 problem details returned by the API for expected failures. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
  ) {
    super(message);
  }
}

const auth: Middleware = {
  onRequest({ request }) {
    const session = loadSession();
    if (session) {
      request.headers.set('Authorization', `Bearer ${session.token}`);
    }
    return request;
  },
};

// Absolute base URL and late-bound fetch: works in the browser and lets tests stub globalThis.fetch.
export const api = createClient<paths>({
  baseUrl: typeof window === 'undefined' ? '' : window.location.origin,
  fetch: (request) => globalThis.fetch(request),
});
api.use(auth);

type ProblemBody = { title?: string; detail?: string; errors?: Record<string, string[]> };

/** Turns an openapi-fetch result into data or a thrown {@link ApiError}. */
export async function unwrap<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await call;
  if (response.ok) {
    return data as T;
  }
  throw toApiError(response.status, error);
}

export function toApiError(status: number, body: unknown): ApiError {
  const problem = (body ?? {}) as ProblemBody;
  const validation = problem.errors ? Object.values(problem.errors).flat().join(' ') : undefined;
  const message =
    problem.detail ?? validation ?? (status === 403 ? 'You do not have permission to do this.' : `Request failed (${status}).`);
  return new ApiError(status, problem.title ?? `HTTP ${status}`, message);
}

/** Multipart upload (openapi-fetch's typing of IFormFile endpoints is awkward; fetch is clearer here). */
export async function upload<T>(url: string, file: File): Promise<T> {
  const form = new FormData();
  form.append('file', file);
  const session = loadSession();
  const response = await fetch(absolute(url), {
    method: 'POST',
    body: form,
    headers: session ? { Authorization: `Bearer ${session.token}` } : {},
  });
  const body = await response.json().catch(() => undefined);
  if (!response.ok) {
    throw toApiError(response.status, body);
  }
  return body as T;
}

/** Downloads an authenticated file (an <a href> would not send the bearer token). */
export async function download(url: string): Promise<void> {
  const session = loadSession();
  const response = await fetch(absolute(url), { headers: session ? { Authorization: `Bearer ${session.token}` } : {} });
  if (!response.ok) {
    throw toApiError(response.status, await response.json().catch(() => undefined));
  }
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(response.headers.get('content-disposition') ?? '')?.[1] ?? 'export';
  const link = document.createElement('a');
  link.href = URL.createObjectURL(await response.blob());
  link.download = decodeURIComponent(name);
  link.click();
  URL.revokeObjectURL(link.href);
}

/** Absolute URLs: a relative fetch fails on pages opened with credentials in the URL (https://user:pass@host). */
function absolute(url: string): URL {
  return new URL(url, window.location.origin);
}
