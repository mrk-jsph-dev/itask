import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/shared/api/http'

import { insightsApi, type Insights } from '../api'
import { useInsightsStore } from '../store'

const result = (days: 30 | 60 | 90): Insights => ({
  days,
  from: '2026-11-01',
  to: '2026-11-30',
  throughput: [],
  created: [],
  cycleTime: { count: 0, medianDays: null, p85Days: null },
  timeInList: [],
  aging: [],
  wip: [],
})

describe('insights store', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => vi.restoreAllMocks())

  it('loads a board range', async () => {
    const get = vi.spyOn(insightsApi, 'get').mockResolvedValue(result(60))
    const store = useInsightsStore()

    await store.load('b1', 60)

    expect(get).toHaveBeenCalledWith('b1', 60)
    expect(store.data?.days).toBe(60)
    expect(store.loading).toBe(false)
  })

  it('drops an older answer that arrives after a newer one', async () => {
    let resolveSlow: (value: Insights) => void = () => {}
    vi.spyOn(insightsApi, 'get')
      .mockImplementationOnce(() => new Promise((r) => (resolveSlow = r)))
      .mockResolvedValueOnce(result(90))
    const store = useInsightsStore()

    const slow = store.load('b1', 30)
    await store.load('b1', 90)
    resolveSlow(result(30))
    await slow

    expect(store.data?.days).toBe(90)
  })

  it('keeps the shown range while another loads, but clears for a new board', async () => {
    vi.spyOn(insightsApi, 'get').mockResolvedValue(result(30))
    const store = useInsightsStore()
    await store.load('b1', 30)

    void store.load('b1', 60)
    expect(store.data).not.toBeNull()

    void store.load('b2', 30)
    expect(store.data).toBeNull()
  })

  it('flags a missing board', async () => {
    vi.spyOn(insightsApi, 'get').mockRejectedValue(new ApiError(404, 'Not found'))
    const store = useInsightsStore()

    await store.load('b1', 30)

    expect(store.missing).toBe(true)
    expect(store.failed).toBe(false)
  })
})
