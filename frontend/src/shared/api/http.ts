/**
 * Thin fetch wrapper. Always sends the HttpOnly auth cookie, never stores tokens
 * in localStorage, and turns ASP.NET Core ProblemDetails into ApiError.
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

  if (response.status === 204 || response.headers.get('content-length') === '0') {
    return undefined as T
  }
  return (await response.json()) as T
}

export const api = {
  get: <T>(path: string) => http<T>(path),
  post: <T>(path: string, body?: unknown) =>
    http<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) }),
}
