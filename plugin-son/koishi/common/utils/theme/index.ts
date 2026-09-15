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

/** 控制台「样式微调」的中文配置键（与 utils/config.ts 的 ThemePatchConfig 结构一致） */
export interface ThemePatchConfigLike {
  主色?: string
  卡片圆角?: string
  宽度缩放?: number
}

/**
 * 把控制台「样式微调」配置键映射为 ThemePatch。
 * 配置键是中文，ThemePatch 是内部英文键，历史上直接透传导致微调静默失效；
 * 此函数独立于 Koishi，可脱离运行环境单测。
 */
export function patchFromConfig(cfg?: ThemePatchConfigLike | null): ThemePatch | undefined {
  if (!cfg) return undefined
  const patch: ThemePatch = {}
  if (typeof cfg.主色 === 'string' && cfg.主色.trim()) patch.accent = cfg.主色.trim()
  if (typeof cfg.卡片圆角 === 'string' && cfg.卡片圆角.trim()) patch.cardRadius = cfg.卡片圆角.trim()
  const scale = Number(cfg.宽度缩放)
  if (Number.isFinite(scale) && scale > 0 && scale !== 1) patch.widthScale = scale
  return Object.keys(patch).length > 0 ? patch : undefined
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

/** 当前主题的 CSS 变量定义（必须最先发射，供组件样式表引用） */
export function activeCssVars(): string {
  return buildCssVars(activeTokens)
}

/** 当前主题的附加皮肤样式（在组件样式表之后发射，可覆盖组件规则） */
export function activeExtraCss(): string {
  // customCss 属于令牌（ThemeTokens.customCss），随主题差异项一起合并
  return activeTokens.customCss
    ? `/* theme:${activeId} */${activeTokens.customCss}`
    : ''
}

/** 变量 + 皮肤（兼容旧调用，frame 已改成分段发射） */
export function activeStyleBlock(): string {
  return `${activeCssVars()}${activeExtraCss()}`
}
