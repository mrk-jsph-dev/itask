import type { Component, InjectionKey } from 'vue'

/**
 * A component the app layout places in every panel header, just before the avatar (the
 * notification bell). Provided by the layout so shared UI doesn't depend on features.
 */
export const panelHeaderExtra: InjectionKey<Component> = Symbol('panelHeaderExtra')
