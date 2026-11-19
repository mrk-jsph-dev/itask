import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createBoardHub, type BoardLiveHandlers, type HubLike } from '../boardHub'

type Handler = (arg: unknown) => void

class FakeHub implements HubLike {
  connectionId: string | null = 'c1'
  readonly handlers = new Map<string, Handler>()
  reconnecting = () => {}
  reconnected = () => {}
  closed = () => {}
  start = vi.fn<() => Promise<void>>(async () => {})
  stop = vi.fn<() => Promise<void>>(async () => {})
  // `never` fits HubLike's generic result type.
  invoke = vi.fn<(...args: unknown[]) => Promise<never>>(
    async () => [{ userId: 'u2', displayName: 'Bo', avatarColor: '#000' }] as never,
  )

  on(method: string, handler: (...args: never[]) => void) {
    this.handlers.set(method, handler as unknown as Handler)
  }
  onreconnecting(h: () => void) {
    this.reconnecting = h
  }
  onreconnected(h: () => void) {
    this.reconnected = h
  }
  onclose(h: () => void) {
    this.closed = h
  }
  push(method: string, arg: unknown) {
    this.handlers.get(method)!(arg)
  }
}

function handlers() {
  return {
    event: vi.fn<BoardLiveHandlers['event']>(),
    presence: vi.fn<BoardLiveHandlers['presence']>(),
    accessLost: vi.fn<BoardLiveHandlers['accessLost']>(),
    resync: vi.fn<BoardLiveHandlers['resync']>(),
  } satisfies BoardLiveHandlers
}

const flush = () => vi.advanceTimersByTimeAsync(0)

describe('board hub', () => {
  let hubs: FakeHub[]
  let hub: ReturnType<typeof createBoardHub>

  beforeEach(() => {
    vi.useFakeTimers()
    hubs = []
    hub = createBoardHub({
      connect: () => {
        const h = new FakeHub()
        hubs.push(h)
        return h
      },
      idleMs: 10_000,
      hiddenMs: 1_000,
    })
  })

  afterEach(() => {
    hub.close()
    vi.useRealTimers()
  })

  it('connects, joins the board and reports presence', async () => {
    const h = handlers()
    hub.open('b1', h)
    expect(hub.status.value).toBe('connecting')
    await flush()

    expect(hubs[0]!.invoke).toHaveBeenCalledWith('Join', 'b1')
    expect(hub.status.value).toBe('live')
    expect(h.presence).toHaveBeenCalledWith([expect.objectContaining({ userId: 'u2' })])
    expect(h.resync).not.toHaveBeenCalled()
  })

  it('passes on events for the open board only', async () => {
    const h = handlers()
    hub.open('b1', h)
    await flush()

    hubs[0]!.push('event', { type: 'card.moved', boardId: 'b1' })
    hubs[0]!.push('event', { type: 'card.moved', boardId: 'other' })
    hubs[0]!.push('accessLost', { boardId: 'b1' })

    expect(h.event).toHaveBeenCalledTimes(1)
    expect(h.accessLost).toHaveBeenCalledTimes(1)
  })

  it('rejoins and resyncs after reconnecting', async () => {
    const h = handlers()
    hub.open('b1', h)
    await flush()

    hubs[0]!.reconnecting()
    expect(hub.status.value).toBe('reconnecting')
    hubs[0]!.reconnected()
    await flush()

    expect(hubs[0]!.invoke).toHaveBeenCalledTimes(2)
    expect(hub.status.value).toBe('live')
    expect(h.resync).toHaveBeenCalledTimes(1)
  })

  it('treats a not-found join as lost access', async () => {
    const h = handlers()
    hub.open('b1', h)
    hubs[0]!.invoke.mockRejectedValueOnce(new Error('HubException: Board not found.'))
    await flush()

    expect(h.accessLost).toHaveBeenCalled()
  })

  it('pauses when idle and resumes with a resync on input', async () => {
    const h = handlers()
    hub.open('b1', h)
    await flush()

    await vi.advanceTimersByTimeAsync(10_000)
    expect(hub.status.value).toBe('paused')
    expect(hubs[0]!.stop).toHaveBeenCalled()

    window.dispatchEvent(new Event('keydown'))
    await flush()

    expect(hubs).toHaveLength(2)
    expect(hub.status.value).toBe('live')
    expect(h.resync).toHaveBeenCalledTimes(1)
  })

  it('pauses in a hidden tab and comes back when shown', async () => {
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
    hub.open('b1', handlers())
    await flush()

    document.dispatchEvent(new Event('visibilitychange'))
    await vi.advanceTimersByTimeAsync(1_000)
    expect(hub.status.value).toBe('paused')

    visibility.mockReturnValue('visible')
    document.dispatchEvent(new Event('visibilitychange'))
    await flush()
    expect(hub.status.value).toBe('live')
  })

  it('goes offline when the connection closes, and stops on close()', async () => {
    hub.open('b1', handlers())
    await flush()

    hubs[0]!.closed()
    expect(hub.status.value).toBe('offline')

    hub.close()
    expect(hub.status.value).toBe('off')
  })

  it('switches boards on the same connection', async () => {
    hub.open('b1', handlers())
    await flush()
    hub.open('b2', handlers())
    await flush()

    expect(hubs).toHaveLength(1)
    expect(hubs[0]!.invoke).toHaveBeenLastCalledWith('Join', 'b2')
  })
})
