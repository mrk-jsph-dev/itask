import { describe, expect, it } from 'vitest'

import { moveItem, neighbours } from '../ordering'

const items = ['a', 'b', 'c', 'd'].map((id) => ({ id }))

describe('moveItem', () => {
  it('moves forward and backward without touching the input', () => {
    expect(moveItem(['a', 'b', 'c', 'd'], 0, 2)).toEqual(['b', 'c', 'a', 'd'])
    expect(moveItem(['a', 'b', 'c', 'd'], 3, 0)).toEqual(['d', 'a', 'b', 'c'])
    const input = ['a', 'b']
    moveItem(input, 0, 1)
    expect(input).toEqual(['a', 'b'])
  })
})

describe('neighbours', () => {
  it('returns the ids on either side', () => {
    expect(neighbours(items, 1)).toEqual({ beforeId: 'a', afterId: 'c' })
  })

  it('uses null at the edges', () => {
    expect(neighbours(items, 0)).toEqual({ beforeId: null, afterId: 'b' })
    expect(neighbours(items, 3)).toEqual({ beforeId: 'c', afterId: null })
    expect(neighbours([{ id: 'x' }], 0)).toEqual({ beforeId: null, afterId: null })
  })
})
