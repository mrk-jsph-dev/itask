/** "ada@example.com" → "a**@example.com": enough to recognise the address on a shared screen. */
export function maskEmail(email: string): string {
  const at = email.lastIndexOf('@')
  if (at < 1) return email
  const name = email.slice(0, at)
  return `${name[0]}${'*'.repeat(Math.max(name.length - 1, 1))}${email.slice(at)}`
}

/** Keeps only digits, at most `length` of them (pasted codes often carry spaces or dashes). */
export function cleanCode(value: string, length = 6): string {
  return value.replace(/\D/g, '').slice(0, length)
}

/** Seconds to wait after a code was sent before another can be requested. */
export const resendCooldownSeconds = 60
