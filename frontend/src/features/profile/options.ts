/** Avatar colours offered on the profile page; all pass WCAG AA with white initials. */
export const avatarColors = [
  '#1D5BF5',
  '#7C3AED',
  '#0F766E',
  '#E11D48',
  '#C2410C',
  '#4D7C0F',
  '#0369A1',
  '#374151',
] as const

type IntlWithZones = typeof Intl & { supportedValuesOf?: (key: 'timeZone') => string[] }

/** IANA zones the browser knows, with UTC first. */
export function timeZones(): string[] {
  const zones = (Intl as IntlWithZones).supportedValuesOf?.('timeZone') ?? []
  return ['UTC', ...zones.filter((zone) => zone !== 'UTC')]
}
