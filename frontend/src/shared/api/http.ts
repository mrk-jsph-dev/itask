import { noteResponse, OFFLINE_HINT, online } from '@/shared/pwa/network'

/**
 * fetch wrapper for the API: sends credentials (HttpOnly auth cookie) and maps
 * ASP.NET Core ProblemDetails responses to ApiError.
 */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly errors?: Record<string, string[]>,
    public readonly detail?: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

export const baseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

/** The live hub connection of this tab; the API skips echoing this tab's own changes to it. */
let liveConnection: string | null = null

export function setLiveConnection(id: string | null) {
  liveConnection = id
}

export async function http<T>(path: string, init: RequestInit = {}): Promise<T> {
  // Offline the app is read-only: writes fail at once instead of hanging on the network.
  if (!online.value && (init.method ?? 'GET') !== 'GET') throw new ApiError(0, OFFLINE_HINT)

  const response = await fetch(`${baseUrl}/api${path}`, {
    ...init,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
      ...(liveConnection ? { 'X-Live-Connection': liveConnection } : {}),
      ...init.headers,
    },
  })
  noteResponse(response)

  if (!response.ok) {
    let title = response.statusText
    let errors: Record<string, string[]> | undefined
    let detail: string | undefined
    try {
      const problem = (await response.json()) as {
        title?: string
        detail?: string
        errors?: Record<string, string[]>
      }
      title = problem.title ?? title
      errors = problem.errors
      detail = problem.detail
    } catch {
      // Body was not JSON; keep the status text.
    }
    throw new ApiError(response.status, title, errors, detail)
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

const json = (body: unknown) => (body === undefined ? undefined : JSON.stringify(body))

export const api = {
  get: <T>(path: string) => http<T>(path),
  post: <T>(path: string, body?: unknown, headers?: Record<string, string>) =>
    http<T>(path, { method: 'POST', body: json(body), headers }),
  put: <T>(path: string, body?: unknown, headers?: Record<string, string>) =>
    http<T>(path, { method: 'PUT', body: json(body), headers }),
  patch: <T>(path: string, body?: unknown) => http<T>(path, { method: 'PATCH', body: json(body) }),
  del: <T = void>(path: string) => http<T>(path, { method: 'DELETE' }),
}
