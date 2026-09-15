// ══════════════════════════════════════════════════════════
//  令牌 → CSS 变量字符串
//  卡片样式表只引用 var(--tw-*)，切换主题只替换本函数的输出，
//  因此新增主题无需改动任何卡片代码。
// ══════════════════════════════════════════════════════════

import type { ThemeTokens } from './tokens'

/** 令牌字段 → CSS 变量名（顺序即输出顺序，便于排查） */
const VAR_MAP: Array<[keyof ThemeTokens, string]> = [
  ['bg', '--tw-bg'],
  ['font', '--tw-font'],
  ['fontMono', '--tw-font-mono'],

  ['text', '--tw-text'],
  ['textBody', '--tw-text-body'],
  ['textMuted', '--tw-text-muted'],
  ['textSubtle', '--tw-text-subtle'],
  ['onAccent', '--tw-on-accent'],
  ['divider', '--tw-divider'],

  ['cardBg', '--tw-card-bg'],
  ['cardBorder', '--tw-card-border'],
  ['cardRadius', '--tw-card-radius'],
  ['cardShadow', '--tw-card-shadow'],
  ['cardBlur', '--tw-blur'],
  ['cardPad', '--tw-card-pad'],

  ['tileBg', '--tw-tile-bg'],
  ['tileBorder', '--tw-tile-border'],
  ['tileRadius', '--tw-tile-radius'],
  ['tileRadiusLg', '--tw-tile-radius-lg'],
  ['tileBorderDone', '--tw-tile-border-done'],

  ['chipBg', '--tw-chip-bg'],
  ['chipBorder', '--tw-chip-border'],
  ['chipText', '--tw-chip-text'],

  ['barTrack', '--tw-bar-track'],

  ['accent', '--tw-accent'],
  ['accentSoft', '--tw-accent-soft'],
  ['accentBorder', '--tw-accent-border'],
  ['gradAccent', '--tw-grad-accent'],
  ['info', '--tw-info'],
  ['infoSoft', '--tw-info-soft'],
  ['infoBorder', '--tw-info-border'],
  ['gradInfo', '--tw-grad-info'],
  ['success', '--tw-success'],
  ['successSoft', '--tw-success-soft'],
  ['successBorder', '--tw-success-border'],
  ['gradSuccess', '--tw-grad-success'],
  ['warning', '--tw-warning'],
  ['warningSoft', '--tw-warning-soft'],
  ['warningBorder', '--tw-warning-border'],
  ['danger', '--tw-danger'],
  ['dangerSoft', '--tw-danger-soft'],
  ['dangerBorder', '--tw-danger-border'],
  ['gradDanger', '--tw-grad-danger'],
  ['neutralSoft', '--tw-neutral-soft'],
  ['neutralBorder', '--tw-neutral-border'],

  ['badgeBg', '--tw-badge-bg'],
  ['badgeBorder', '--tw-badge-border'],

  ['imgBg', '--tw-img-bg'],
  ['imgFilter', '--tw-img-filter'],

  ['glow1', '--tw-glow-1'],
  ['glow2', '--tw-glow-2'],

  ['wInfo', '--tw-w-info'],
  ['wList', '--tw-w-list'],
  ['wWide', '--tw-w-wide'],
  ['wHelp', '--tw-w-help'],
  ['gap', '--tw-gap'],
]

/** 全部变量名（供校验脚本比对，确保卡片引用的变量都有定义） */
export const THEME_VAR_NAMES: string[] = VAR_MAP.map(([, name]) => name)

/** 生成 `:root{--tw-*:...}` 片段 */
export function buildCssVars(tokens: ThemeTokens): string {
  const lines = VAR_MAP.map(([key, name]) => `  ${name}:${tokens[key]};`)
  return `:root{\n${lines.join('\n')}\n}`
}
