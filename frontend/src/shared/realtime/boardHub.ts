import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { readonly, ref } from 'vue'

import { baseUrl, setLiveConnection } from '@/shared/api/http'

/** A change on the board, from the API's `BoardEvent`. Ids only; fetch the rest. */
export interface BoardEvent {
  /** Activity type such as `card.moved`, or `board.deleted`. */
  type: string
  boardId: string
  cardId: string | null
  /** The card's list and position after the change, for card events. */
  listId: string | null
  position: number | null
  /** The member a `member.*` event is about. */
  userId: string | null
  actorId: string
  at: string
}

export interface PresenceUser {
  userId: string
  displayName: string
  avatarColor: string
}

/**
 * `paused`: disconnected on purpose (tab hidden or user idle) so the API can scale to zero;
 * `offline`: the connection failed and gave up retrying until the user is back.
 */
export type LiveStatus = 'off' | 'connecting' | 'live' | 'reconnecting' | 'paused' | 'offline'

export interface BoardLiveHandlers {
  event: (e: BoardEvent) => void
  presence: (users: PresenceUser[]) => void
  /** The user is no longer a member, or the board is gone. */
  accessLost: () => void
  /** Events may have been missed (reconnected or resumed); reload what the board shows. */
  resync: () => void
}

/** The parts of `@microsoft/signalr`'s HubConnection the client uses. */
export interface HubLike {
  readonly connectionId: string | null
  start(): Promise<void>
  stop(): Promise<void>
  invoke<T>(method: string, ...args: unknown[]): Promise<T>
  on(method: string, handler: (...args: never[]) => void): void
  onreconnecting(handler: () => void): void
  onreconnected(handler: () => void): void
  onclose(handler: () => void): void
}

export interface BoardHubOptions {
  connect?: () => HubLike
  /** Disconnect after this long without input. */
  idleMs?: number
  /** Disconnect after the tab has been hidden this long. */
  hiddenMs?: number
}

const minute = 60_000
const activityEvents = ['pointerdown', 'keydown', 'wheel', 'touchstart'] as const

function connectToApi(): HubLike {
  return new HubConnectionBuilder()
    .withUrl(`${baseUrl}/api/hubs/board`, { withCredentials: true })
    .withAutomaticReconnect([0, 2000, 5000, 10_000, 30_000])
    .configureLogging(LogLevel.None)
    .build()
}

/**
 * One live connection for the board on screen. An open connection keeps the API awake, so it
 * only exists while a board is open and the user is around: it closes after `hiddenMs` in a
 * background tab or `idleMs` without input, and comes back (with a resync) on the next input.
 */
export function createBoardHub(options: BoardHubOptions = {}) {
  const connect = options.connect ?? connectToApi
  const idleMs = options.idleMs ?? 10 * minute
  const hiddenMs = options.hiddenMs ?? minute

  const status = ref<LiveStatus>('off')
  let connection: HubLike | null = null
  let boardId: string | null = null
  let handlers: BoardLiveHandlers | null = null
  let idleTimer: ReturnType<typeof setTimeout> | undefined
  let hiddenTimer: ReturnType<typeof setTimeout> | undefined
  let lastInput = 0

  function build(): HubLike {
    const c = connect()
    c.on('event', (e: BoardEvent) => {
      if (connection === c && e.boardId === boardId) handlers?.event(e)
    })
    c.on('presence', (p: { boardId: string; users: PresenceUser[] }) => {
      if (connection === c && p.boardId === boardId) handlers?.presence(p.users)
    })
    c.on('accessLost', (a: { boardId: string }) => {
      if (connection === c && a.boardId === boardId) handlers?.accessLost()
    })
    c.onreconnecting(() => {
      if (connection !== c) return
      status.value = 'reconnecting'
      setLiveConnection(null)
    })
    c.onreconnected(() => void join(c, true))
    c.onclose(() => {
      if (connection !== c) return
      connection = null
      setLiveConnection(null)
      status.value = 'offline'
    })
    return c
  }

  async function start(resync: boolean) {
    if (!boardId || connection) return
    const c = build()
    connection = c
    status.value = 'connecting'
    try {
      await c.start()
    } catch {
      if (connection === c) {
        connection = null
        status.value = 'offline'
      }
      return
    }
    await join(c, resync)
  }

  async function join(c: HubLike, resync: boolean) {
    const id = boardId
    if (connection !== c || !id) return
    try {
      const users = await c.invoke<PresenceUser[]>('Join', id)
      if (connection !== c || boardId !== id) return
      setLiveConnection(c.connectionId)
      status.value = 'live'
      handlers?.presence(users)
      if (resync) handlers?.resync()
    } catch (e) {
      if (connection !== c || boardId !== id) return
      if (e instanceof Error && /not found/i.test(e.message)) handlers?.accessLost()
      else status.value = 'offline'
    }
  }

  async function disconnect() {
    const c = connection
    connection = null
    setLiveConnection(null)
    if (c) await c.stop().catch(() => undefined)
  }

  function pause() {
    if (!boardId || status.value === 'paused') return
    clearTimeout(idleTimer)
    status.value = 'paused'
    void disconnect()
  }

  /** Reconnects after a pause or a failed connection; missed events are covered by a resync. */
  function resume() {
    if (!boardId || connection) return
    void start(true)
  }

  function armIdle() {
    clearTimeout(idleTimer)
    idleTimer = setTimeout(pause, idleMs)
  }

  function onInput() {
    const now = Date.now()
    if (status.value === 'paused' || status.value === 'offline') resume()
    // Re-arming on every keystroke is wasted work; a few seconds of slack is fine.
    if (now - lastInput < 5000) return
    lastInput = now
    armIdle()
  }

  function onVisibility() {
    clearTimeout(hiddenTimer)
    if (document.visibilityState === 'hidden') {
      hiddenTimer = setTimeout(pause, hiddenMs)
    } else {
      resume()
      armIdle()
    }
  }

  function watchUser(on: boolean) {
    const method = on ? 'addEventListener' : 'removeEventListener'
    for (const name of activityEvents) window[method](name, onInput, { passive: true })
    window[method]('online', resume)
    document[method]('visibilitychange', onVisibility)
  }

  /** Shows `id` live; replaces the board shown before. */
  function open(id: string, next: BoardLiveHandlers) {
    const first = boardId === null
    boardId = id
    handlers = next
    if (first) watchUser(true)
    armIdle()
    if (connection && status.value === 'live') void join(connection, false)
    else void start(false)
  }

  function close() {
    if (boardId === null) return
    boardId = null
    handlers = null
    watchUser(false)
    clearTimeout(idleTimer)
    clearTimeout(hiddenTimer)
    status.value = 'off'
    void disconnect()
  }

  return { status: readonly(status), open, close }
}

export type BoardHub = ReturnType<typeof createBoardHub>

/** The app's live connection. */
export const boardHub = createBoardHub()
