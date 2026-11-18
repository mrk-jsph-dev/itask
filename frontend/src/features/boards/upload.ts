import { attachmentsApi, type Attachment } from './api'

/** Largest file the API accepts (10 MB). */
export const maxUploadBytes = 10 * 1024 * 1024

const allowedTypes = new Set([
  'application/pdf',
  'text/plain',
  'text/csv',
  'text/markdown',
  'application/zip',
  'application/x-zip-compressed',
  'application/msword',
  'application/vnd.ms-excel',
  'application/vnd.ms-powerpoint',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/vnd.openxmlformats-officedocument.presentationml.presentation',
  'image/png',
  'image/jpeg',
  'image/gif',
  'image/webp',
  'image/avif',
  'image/bmp',
])

/** Types browsers often leave empty, guessed from the extension. */
const byExtension: Record<string, string> = {
  md: 'text/markdown',
  csv: 'text/csv',
  txt: 'text/plain',
  zip: 'application/zip',
}

/** The type to upload `file` as. */
export function contentTypeOf(file: Pick<File, 'name' | 'type'>): string {
  if (file.type) return file.type
  const ext = file.name.split('.').pop()?.toLowerCase() ?? ''
  return byExtension[ext] ?? 'application/octet-stream'
}

/** Why `file` can't be attached, or null when it can. */
export function uploadProblem(file: Pick<File, 'name' | 'type' | 'size'>): string | null {
  if (!allowedTypes.has(contentTypeOf(file)))
    return `${file.name}: use images, PDF, text, Office documents or zip.`
  if (file.size === 0) return `${file.name} is empty.`
  if (file.size > maxUploadBytes) return `${file.name} is larger than 10 MB.`
  return null
}

/**
 * Uploads straight to storage: ask the API for a short-lived URL, PUT the bytes there (no
 * cookies), then confirm so the API records the file.
 */
export async function uploadFile(boardId: string, cardId: string, file: File): Promise<Attachment> {
  const contentType = contentTypeOf(file)
  const slot = await attachmentsApi.requestUpload(boardId, cardId, {
    fileName: file.name,
    contentType,
    sizeBytes: file.size,
  })
  const put = await fetch(slot.uploadUrl, {
    method: 'PUT',
    body: file,
    credentials: 'omit',
    headers: { 'Content-Type': contentType, 'x-ms-blob-type': 'BlockBlob' },
  })
  if (!put.ok) throw new Error(`Upload failed (${put.status}).`)
  return attachmentsApi.confirm(boardId, cardId, slot.blobName, file.name)
}

/** "240 KB" style sizes. */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

/** Short badge for a file type: PNG, PDF, DOCX… */
export function typeBadge(attachment: Pick<Attachment, 'fileName' | 'contentType'>): string {
  const ext = attachment.fileName.includes('.') ? attachment.fileName.split('.').pop()! : ''
  return (ext || attachment.contentType.split('/').pop() || 'file').slice(0, 4).toUpperCase()
}

/** Badge colours per frame C5: images blue, PDFs red, everything else grey. */
export function badgeClass(attachment: Pick<Attachment, 'contentType'>): string {
  if (attachment.contentType.startsWith('image/')) return 'bg-[#EFF4FF] text-[#1D4ED8]'
  if (attachment.contentType === 'application/pdf') return 'bg-[#FEF2F2] text-[#B91C1C]'
  return 'bg-hover text-muted'
}
