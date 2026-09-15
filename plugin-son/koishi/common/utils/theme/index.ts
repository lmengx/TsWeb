// ══════════════════════════════════════════════════════════
//  主题注册表与当前主题状态
//  解析优先级（当前只实现全局档，其余为后续扩展预留）：
//    用户级 -> 群级 -> 插件配置 -> 内置默认 dark-glass
//  未知 id 一律回退到默认并告警，绝不因配置错误导致出不了图。
// ══════════════════════════════════════════════════════════

import {
  BASE_TOKENS,
  resolveTokens,
  type Theme,
  type ThemePatch,
  type ThemeTokens,
} from './tokens'
import { buildCssVars, THEME_VAR_NAMES } from './css'

import darkGlass from './themes/dark-glass'
import neonCyber from './themes/neon-cyber'
import terraria from './themes/terraria'
import warm from './themes/warm'
import aurora from './themes/aurora'

export type { Theme, ThemePatch, ThemeTokens }
export { BASE_TOKENS, buildCssVars, THEME_VAR_NAMES }

export const DEFAULT_THEME_ID = 'dark-glass'

/** 内置主题列表（顺序即控制台下拉顺序） */
export const THEMES: Theme[] = [darkGlass, neonCyber, terraria, warm, aurora]

export const THEME_IDS: string[] = THEMES.map(t => t.id)

export function getTheme(id?: string | null): Theme | null {
  if (!id) return null
  return THEMES.find(t => t.id === id) ?? null
}

export function listThemes(): Array<{ id: string; name: string; description: string }> {
  return THEMES.map(t => ({ id: t.id, name: t.name, description: t.description }))
}

// ── 当前主题状态 ──

let activeId = DEFAULT_THEME_ID
let activePatch: ThemePatch | undefined
let activeTokens: ThemeTokens = { ...BASE_TOKENS }
let activeThemeObj: Theme | null = null
let warn: (msg: string) => void = () => {}

/** 注入日志函数（插件载入时由入口传入 ctx.logger.warn） */
export function setThemeLogger(fn: (msg: string) => void) {
  warn = fn
}

/**
 * 设置全局主题。id 未知时回退默认并告警。
 * Koishi 控制台改配置会重载插件实例，本函数随之被再次调用。
 */
export function setActiveTheme(id?: string | null, patch?: ThemePatch) {
  const theme = getTheme(id)
  if (id && !theme) {
    warn(`[theme] 未知主题 "${id}"，已回退到 ${DEFAULT_THEME_ID}`)
  }
  activeId = theme?.id ?? DEFAULT_THEME_ID
  activeThemeObj = theme ?? getTheme(DEFAULT_THEME_ID)
  activePatch = patch
  activeTokens = resolveTokens(activeThemeObj, patch)
}

export function activeThemeId(): string {
  return activeId
}

export function activeThemeVersion(): number {
  return activeThemeObj?.version ?? 0
}

export function activeTokensOr(): ThemeTokens {
  return activeTokens
}

/** 当前主题的完整样式（变量定义 + 主题附加样式），供 frame 使用 */
export function activeStyleBlock(): string {
  const vars = buildCssVars(activeTokens)
  // customCss 属于令牌（ThemeTokens.customCss），随主题差异项一起合并
  const extra = activeTokens.customCss
    ? `\n/* theme:${activeId} */${activeTokens.customCss}`
    : ''
  return `${vars}${extra}`
}
