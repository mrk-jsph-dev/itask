import { colorPalette } from '@/shared/ui/palette'

/** Avatar colours offered on the profile page. */
export const avatarColors = colorPalette

type IntlWithZones = typeof Intl & { supportedValuesOf?: (key: 'timeZone') => string[] }

/** IANA zones the browser knows, with UTC first. */
export function timeZones(): string[] {
  const zones = (Intl as IntlWithZones).supportedValuesOf?.('timeZone') ?? []
  return ['UTC', ...zones.filter((zone) => zone !== 'UTC')]
}
