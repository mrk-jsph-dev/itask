export interface PasswordStrength {
  /** 0 (empty) to 4 (meets every rule the API enforces). */
  score: 0 | 1 | 2 | 3 | 4
  label: string
  hint: string
}

export const MIN_PASSWORD_LENGTH = 10

const rules: { test: (pw: string) => boolean; hint: string }[] = [
  {
    test: (pw) => pw.length >= MIN_PASSWORD_LENGTH,
    hint: `Use at least ${MIN_PASSWORD_LENGTH} characters.`,
  },
  { test: (pw) => /[a-z]/.test(pw) && /[A-Z]/.test(pw), hint: 'Mix upper and lower case letters.' },
  { test: (pw) => /\d/.test(pw), hint: 'Add a number.' },
  { test: (pw) => /[^A-Za-z0-9]/.test(pw), hint: 'Add a symbol to make it strong.' },
]

const labels = ['', 'Weak', 'Fair', 'Good', 'Strong'] as const

/** Scores a password against the same rules the API's Identity options require. */
export function passwordStrength(password: string): PasswordStrength {
  if (!password) return { score: 0, label: '', hint: rules[0]!.hint }

  const failed = rules.filter((rule) => !rule.test(password))
  let score = rules.length - failed.length
  if (password.length < MIN_PASSWORD_LENGTH) score = Math.min(score, 1)
  const clamped = Math.max(1, score) as PasswordStrength['score']

  return {
    score: clamped,
    label: labels[clamped],
    hint: failed[0]?.hint ?? '',
  }
}
