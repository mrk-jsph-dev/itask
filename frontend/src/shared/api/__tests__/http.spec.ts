import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiError, http } from '../http'

describe('http', () => {
  afterEach(() => vi.restoreAllMocks())

  it('sends the auth cookie and parses JSON', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response(JSON.stringify({ status: 'ok' }), { status: 200 }))

    const result = await http<{ status: string }>('/health')

    expect(result.status).toBe('ok')
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/health',
      expect.objectContaining({ credentials: 'include' }),
    )
  })

  it('turns ProblemDetails into ApiError', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ title: 'Validation failed', errors: { Name: ['Required'] } }), {
        status: 400,
      }),
    )

    const error = await http('/boards').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).errors?.Name).toEqual(['Required'])
  })
})
