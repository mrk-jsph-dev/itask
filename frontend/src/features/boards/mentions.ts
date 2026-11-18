/** Someone who can be mentioned with `@DisplayName`. */
export interface Mentionable {
  userId: string
  displayName: string
}

/** The `@query` being typed just before the caret, or null when there isn't one. */
export function activeMention(
  text: string,
  caret: number,
): { start: number; query: string } | null {
  const before = text.slice(0, caret)
  const at = before.lastIndexOf('@')
  if (at < 0) return null
  if (at > 0 && /[\p{L}\p{N}]/u.test(before[at - 1]!)) return null
  const query = before.slice(at + 1)
  // Names may hold one space ("Ada Lovelace"), not a line break or a second word gap.
  if (/\n|\s\s/.test(query) || query.split(' ').length > 3) return null
  return { start: at, query }
}

/** Members whose display name starts with (or has a word starting with) `query`; at most 5. */
export function suggest(members: readonly Mentionable[], query: string): Mentionable[] {
  const q = query.trim().toLowerCase()
  return members
    .filter((m) => {
      const name = m.displayName.toLowerCase()
      return !q || name.startsWith(q) || name.split(/\s+/).some((w) => w.startsWith(q))
    })
    .slice(0, 5)
}

/** Replaces the `@query` at `start..caret` with `@Name `; returns the text and new caret. */
export function insertMention(text: string, start: number, caret: number, name: string) {
  const inserted = `@${name} `
  return {
    text: text.slice(0, start) + inserted + text.slice(caret),
    caret: start + inserted.length,
  }
}

/**
 * Wraps `@Name` for each mentioned member in rendered HTML with a highlight span. Names are
 * escaped and matched case-insensitively, longest first, outside tags.
 */
export function highlightMentions(html: string, names: readonly string[]): string {
  const sorted = [...new Set(names)].filter(Boolean).sort((a, b) => b.length - a.length)
  if (sorted.length === 0) return html
  const escaped = sorted.map((n) => escapeHtml(n).replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
  const pattern = new RegExp(`(^|[^\\p{L}\\p{N}])@(${escaped.join('|')})(?![\\p{L}\\p{N}])`, 'giu')
  return html
    .split(/(<[^>]*>)/)
    .map((part) =>
      part.startsWith('<') ? part : part.replace(pattern, '$1<span class="mention">@$2</span>'),
    )
    .join('')
}

function escapeHtml(text: string) {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}
