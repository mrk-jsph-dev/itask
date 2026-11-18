/**
 * fetch wrapper for the API: sends credentials (HttpOnly auth cookie) and maps
 * ASP.NET Core ProblemDetails responses to ApiError.
 */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly errors?: Record<string, string[]>,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

const baseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

export async function http<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`${baseUrl}/api${path}`, {
    ...init,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json', ...init.headers },
  })

  if (!response.ok) {
    let title = response.statusText
    let errors: Record<string, string[]> | undefined
    try {
      const problem = (await response.json()) as {
        title?: string
        errors?: Record<string, string[]>
      }
      title = problem.title ?? title
      errors = problem.errors
    } catch {
      // Body was not JSON; keep the status text.
    }
    throw new ApiError(response.status, title, errors)
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
  put: <T>(path: string, body?: unknown) => http<T>(path, { method: 'PUT', body: json(body) }),
}
