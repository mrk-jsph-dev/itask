import { afterEach, describe, expect, it, vi } from 'vitest'

import { attachmentsApi, type Attachment } from '../api'
import { contentTypeOf, formatSize, typeBadge, uploadFile, uploadProblem } from '../upload'

const file = (name: string, type: string, size = 4) =>
  new File([new Uint8Array(size)], name, { type })

describe('upload', () => {
  afterEach(() => vi.restoreAllMocks())

  it('accepts allowed files and explains the rest', () => {
    expect(uploadProblem(file('a.pdf', 'application/pdf'))).toBeNull()
    expect(uploadProblem(file('notes.md', ''))).toBeNull()
    expect(uploadProblem(file('x.exe', 'application/x-msdownload'))).toMatch(/use images/)
    expect(uploadProblem(file('logo.svg', 'image/svg+xml'))).toMatch(/use images/)
    expect(uploadProblem(file('empty.pdf', 'application/pdf', 0))).toMatch(/empty/)
    expect(
      uploadProblem({ name: 'big.pdf', type: 'application/pdf', size: 10 * 1024 * 1024 + 1 }),
    ).toMatch(/10 MB/)
  })

  it('guesses missing types from the extension', () => {
    expect(contentTypeOf({ name: 'data.CSV', type: '' })).toBe('text/csv')
    expect(contentTypeOf({ name: 'blob', type: '' })).toBe('application/octet-stream')
  })

  it('requests a slot, PUTs the bytes without cookies, then confirms', async () => {
    const attachment = { id: 'a1', fileName: 'a.pdf' } as Attachment
    vi.spyOn(attachmentsApi, 'requestUpload').mockResolvedValue({
      blobName: 'boards/b/cards/c/x',
      uploadUrl: 'https://store.example/x?sig=1',
      expiresAt: '2026-11-18T10:10:00Z',
    })
    const confirm = vi.spyOn(attachmentsApi, 'confirm').mockResolvedValue(attachment)
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(uploadFile('b1', 'c1', file('a.pdf', 'application/pdf'))).resolves.toBe(attachment)

    const [url, init] = fetchMock.mock.calls[0]! as [string, RequestInit]
    expect(url).toBe('https://store.example/x?sig=1')
    expect(init).toMatchObject({
      method: 'PUT',
      credentials: 'omit',
      headers: { 'Content-Type': 'application/pdf', 'x-ms-blob-type': 'BlockBlob' },
    })
    expect(confirm).toHaveBeenCalledWith('b1', 'c1', 'boards/b/cards/c/x', 'a.pdf')
    vi.unstubAllGlobals()
  })

  it('does not confirm when the PUT fails', async () => {
    vi.spyOn(attachmentsApi, 'requestUpload').mockResolvedValue({
      blobName: 'n',
      uploadUrl: '/u',
      expiresAt: '',
    })
    const confirm = vi.spyOn(attachmentsApi, 'confirm')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })))

    await expect(uploadFile('b1', 'c1', file('a.pdf', 'application/pdf'))).rejects.toThrow(/403/)
    expect(confirm).not.toHaveBeenCalled()
    vi.unstubAllGlobals()
  })

  it('formats sizes and badges', () => {
    expect([formatSize(512), formatSize(240 * 1024), formatSize(3.5 * 1024 * 1024)]).toEqual([
      '512 B',
      '240 KB',
      '3.5 MB',
    ])
    expect(typeBadge({ fileName: 'drag-states.png', contentType: 'image/png' })).toBe('PNG')
    expect(typeBadge({ fileName: 'README', contentType: 'text/plain' })).toBe('PLAI')
  })
})
