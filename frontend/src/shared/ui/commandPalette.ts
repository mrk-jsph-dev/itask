/** One selectable row in the command palette. */
export interface PaletteItem {
  /** Unique across all sections. */
  id: string
  icon: string
  title: string
  subtitle?: string
  /** Short right-aligned text such as a card key. */
  hint?: string
}

export interface PaletteSection<T extends PaletteItem = PaletteItem> {
  title: string
  items: T[]
}
