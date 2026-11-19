import { providerLabels, type ExternalProvider } from '@/features/auth/external'
import type { MyLogins } from './api'

export interface ConnectionRow {
  provider: ExternalProvider
  label: string
  connected: boolean
  accountName: string | null
}

/** One row per offered provider, plus connected ones the server no longer offers (so they can be removed). */
export function connectionRows(logins: MyLogins): ConnectionRow[] {
  const providers = [...logins.providers]
  for (const login of logins.logins) {
    if (!providers.includes(login.provider)) providers.push(login.provider)
  }
  return providers.map((provider) => {
    const login = logins.logins.find((l) => l.provider === provider)
    return {
      provider,
      label: providerLabels[provider],
      connected: login !== undefined,
      accountName: login?.accountName ?? null,
    }
  })
}

/** The account must keep a way to sign in: a password or another connected provider. */
export function canDisconnect(hasPassword: boolean, connectedCount: number): boolean {
  return hasPassword || connectedCount > 1
}
