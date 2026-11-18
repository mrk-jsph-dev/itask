import { describe, expect, it } from 'vitest'

import { renderMarkdown } from '../markdown'

describe('renderMarkdown', () => {
  it('renders common markdown', () => {
    const html = renderMarkdown('## Notes\n\n- **one**\n- `two`')

    expect(html).toContain('<h2>Notes</h2>')
    expect(html).toContain('<strong>one</strong>')
    expect(html).toContain('<code>two</code>')
  })

  it('strips scripts, handlers and javascript links', () => {
    const html = renderMarkdown(
      '<script>alert(1)</script><img src=x onerror=alert(1)> <b onclick="x()">b</b>',
    )
    const link = renderMarkdown('[x](javascript:alert(1))')

    expect(html).not.toMatch(/<script|onerror|onclick|<img/i)
    expect(link).not.toMatch(/href="javascript:/i)
  })

  it('opens links in a new tab without opener access', () => {
    expect(renderMarkdown('[site](https://example.com)')).toContain(
      'target="_blank" rel="noopener noreferrer"',
    )
  })
})
