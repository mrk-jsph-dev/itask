import { describe, expect, it } from 'vitest'

import { activeMention, highlightMentions, insertMention, suggest } from '../mentions'

const members = [
  { userId: '1', displayName: 'Ada Lovelace' },
  { userId: '2', displayName: 'Alan Turing' },
  { userId: '3', displayName: 'Grace Hopper' },
]

describe('mentions', () => {
  it('finds the @query before the caret', () => {
    expect(activeMention('hi @Ad', 6)).toEqual({ start: 3, query: 'Ad' })
    expect(activeMention('@Ada Lo', 7)).toEqual({ start: 0, query: 'Ada Lo' })
    expect(activeMention('mail ada@example.com', 20)).toBeNull()
    expect(activeMention('@Ada\nnext', 9)).toBeNull()
    expect(activeMention('no mention', 10)).toBeNull()
  })

  it('suggests members by name or word start', () => {
    expect(suggest(members, 'a').map((m) => m.userId)).toEqual(['1', '2'])
    expect(suggest(members, 'hop').map((m) => m.userId)).toEqual(['3'])
    expect(suggest(members, '')).toHaveLength(3)
  })

  it('inserts the chosen name and moves the caret after it', () => {
    expect(insertMention('hi @Ad there', 3, 6, 'Ada Lovelace')).toEqual({
      text: 'hi @Ada Lovelace  there',
      caret: 17,
    })
  })

  it('highlights mentions outside tags only', () => {
    const html = '<p>Thanks @ada lovelace, see <a href="/@Ada Lovelace">x</a></p>'

    expect(highlightMentions(html, ['Ada Lovelace'])).toBe(
      '<p>Thanks <span class="mention">@ada lovelace</span>, see <a href="/@Ada Lovelace">x</a></p>',
    )
    expect(highlightMentions('<p>@Adam</p>', ['Ada'])).toBe('<p>@Adam</p>')
  })
})
