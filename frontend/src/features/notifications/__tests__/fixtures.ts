import type { AppNotification } from '../api'

export const notification = (change: Partial<AppNotification> = {}): AppNotification => ({
  id: 'n1',
  type: 'mentioned',
  data: { card: { key: 'ITK-1', title: 'Fix login' }, commentId: 'c9' },
  createdAt: '2026-11-19T10:00:00Z',
  readAt: null,
  cardId: 'card1',
  board: { id: 'b1', name: 'Launch', emoji: '🚀' },
  actor: { id: 'u1', displayName: 'Ada', avatarColor: '#1D5BF5' },
  ...change,
})
