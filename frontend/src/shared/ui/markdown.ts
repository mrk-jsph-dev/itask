import DOMPurify from 'dompurify'
import { marked } from 'marked'

marked.use({ gfm: true, breaks: true })

// Links open in a new tab without access to this window.
DOMPurify.addHook('afterSanitizeAttributes', (node) => {
  if (node.tagName === 'A' && node.getAttribute('href')) {
    node.setAttribute('target', '_blank')
    node.setAttribute('rel', 'noopener noreferrer')
  }
})

/** Markdown to sanitised HTML, safe for `v-html`. Raw HTML in the source is stripped. */
export function renderMarkdown(source: string): string {
  const html = marked.parse(source, { async: false })
  return DOMPurify.sanitize(html, {
    FORBID_TAGS: ['style', 'img', 'iframe', 'form', 'input'],
    FORBID_ATTR: ['style'],
  })
}

/** Markdown as plain text, whitespace collapsed and cut to `max` characters with an ellipsis. */
export function markdownExcerpt(source: string, max = 160): string {
  const doc = new DOMParser().parseFromString(renderMarkdown(source), 'text/html')
  const text = (doc.body.textContent ?? '').replace(/\s+/g, ' ').trim()
  return text.length > max ? `${text.slice(0, max - 1).trimEnd()}…` : text
}
