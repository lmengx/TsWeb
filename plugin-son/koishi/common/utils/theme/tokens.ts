// ══════════════════════════════════════════════════════════
//  卡片主题令牌层
//  BASE_TOKENS = 改造前的视觉基准（深夜玻璃），主题只写差异项，
//  由 resolveTokens() 与基准深合并。新增主题见
//  scripts/文档/Koishi卡片主题系统设计.md 第十二节。
// ══════════════════════════════════════════════════════════

export interface ThemeTokens {
  // — 画布 —
  bg: string
  font: string
  fontMono: string

  // — 文字四级 + 反色 —
  text: string
  textBody: string
  textMuted: string
  textSubtle: string
  onAccent: string
  divider: string

  // — 卡片容器 —
  cardBg: string
  cardBorder: string
  cardRadius: string
  cardShadow: string
  cardBlur: string
  cardPad: string

  // — 内嵌块（服务块 / Boss 卡 / 投票轮次） —
  tileBg: string
  tileBorder: string
  tileRadius: string
  tileRadiusLg: string
  tileBorderDone: string

  // — 标签胶囊 —
  chipBg: string
  chipBorder: string
  chipText: string

  // — 进度条轨道 —
  barTrack: string

  // — 语义色四件套 —
  accent: string
  accentSoft: string
  accentBorder: string
  gradAccent: string
  info: string
  infoSoft: string
  infoBorder: string
  gradInfo: string
  success: string
  successSoft: string
  successBorder: string
  gradSuccess: string
  warning: string
  warningSoft: string
  warningBorder: string
  danger: string
  dangerSoft: string
  dangerBorder: string
  gradDanger: string
  neutralSoft: string
  neutralBorder: string

  // — 徽章 —
  badgeBg: string
  badgeBorder: string

  // — Boss / 事件图标 —
  imgBg: string
  imgFilter: string

  // — 光晕装饰 —
  glow1: string
  glow2: string

  // — 版式 —
  wInfo: string
  wList: string
  wWide: string
  wHelp: string
  gap: string

  // — 主题附加样式（仅结构性差异使用，随主题差异项合并） —
  customCss?: string
}

export interface Theme {
  id: string
  name: string
  description: string
  /** 令牌或 customCss 变更时递增，参与 help 图片缓存指纹 */
  version: number
  tokens: Partial<ThemeTokens>
}

/** 视觉基准：改造前 render.ts 的实际取值（深夜玻璃） */
export const BASE_TOKENS: ThemeTokens = {
  bg: 'linear-gradient(135deg,#0f0c29,#302b63,#24243e)',
  font: '-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif',
  fontMono: '"JetBrains Mono","Consolas","Courier New",monospace',

  text: '#ffffff',
  textBody: 'rgba(255,255,255,0.9)',
  textMuted: 'rgba(255,255,255,0.5)',
  textSubtle: 'rgba(255,255,255,0.4)',
  onAccent: '#ffffff',
  divider: 'rgba(255,255,255,0.07)',

  cardBg: 'rgba(255,255,255,0.07)',
  cardBorder: 'rgba(255,255,255,0.11)',
  cardRadius: '18px',
  cardShadow: '0 25px 50px -12px rgba(0,0,0,0.6)',
  cardBlur: '20px',
  cardPad: '24px 22px',

  tileBg: 'rgba(255,255,255,0.06)',
  tileBorder: 'rgba(255,255,255,0.09)',
  tileRadius: '10px',
  tileRadiusLg: '14px',
  tileBorderDone: 'rgba(16,185,129,0.35)',

  chipBg: 'rgba(255,255,255,0.07)',
  chipBorder: 'rgba(255,255,255,0.1)',
  chipText: 'rgba(255,255,255,0.9)',

  barTrack: 'rgba(255,255,255,0.08)',

  accent: '#a78bfa',
  accentSoft: 'rgba(124,58,237,0.2)',
  accentBorder: 'rgba(124,58,237,0.3)',
  gradAccent: 'linear-gradient(135deg,#8b5cf6,#a78bfa)',
  info: '#93c5fd',
  infoSoft: 'rgba(59,130,246,0.1)',
  infoBorder: 'rgba(59,130,246,0.2)',
  gradInfo: 'linear-gradient(90deg,#3b82f6,#60a5fa)',
  success: '#34d399',
  successSoft: 'rgba(16,185,129,0.15)',
  successBorder: 'rgba(16,185,129,0.33)',
  gradSuccess: 'linear-gradient(135deg,#10b981,#34d399)',
  warning: '#fbbf24',
  warningSoft: 'rgba(251,191,36,0.15)',
  warningBorder: 'rgba(251,191,36,0.3)',
  danger: '#ef4444',
  dangerSoft: 'rgba(239,68,68,0.15)',
  dangerBorder: 'rgba(239,68,68,0.35)',
  gradDanger: 'linear-gradient(135deg,#ef4444,#dc2626)',
  neutralSoft: 'rgba(107,114,128,0.2)',
  neutralBorder: 'rgba(107,114,128,0.3)',

  badgeBg: 'rgba(99,102,241,0.15)',
  badgeBorder: 'rgba(99,102,241,0.3)',

  imgBg: 'linear-gradient(135deg,#1a1a2e,#16213e)',
  imgFilter: 'drop-shadow(0 2px 4px rgba(0,0,0,0.4))',

  glow1: 'rgba(124,58,237,0.25)',
  glow2: 'rgba(59,130,246,0.2)',

  wInfo: '320px',
  wList: '520px',
  wWide: '680px',
  wHelp: '560px',
  gap: '14px',
}

/** 主题差异项与基准深合并（令牌全为扁平字符串，直接覆盖） */
export function resolveTokens(theme?: Theme | null, patch?: ThemePatch): ThemeTokens {
  return {
    ...BASE_TOKENS,
    ...(theme?.tokens ?? {}),
    ...(patch ? patchToTokens(patch) : {}),
  }
}

/** 控制台「样式微调」：只暴露安全且通用的三项 */
export interface ThemePatch {
  /** 强调色（主色）覆盖 */
  accent?: string
  /** 卡片圆角 */
  cardRadius?: string
  /** 整体宽度缩放（0.6 ~ 1.6） */
  widthScale?: number
}

function scalePx(value: string, scale: number): string {
  const m = /^(\d+(?:\.\d+)?)px$/.exec(value.trim())
  if (!m) return value
  return `${Math.round(Number(m[1]) * scale)}px`
}

function patchToTokens(patch: ThemePatch): Partial<ThemeTokens> {
  const out: Partial<ThemeTokens> = {}
  if (patch.accent) out.accent = patch.accent
  if (patch.cardRadius) out.cardRadius = patch.cardRadius
  const scale = Number(patch.widthScale)
  if (Number.isFinite(scale) && scale > 0 && scale !== 1) {
    out.wInfo = scalePx(BASE_TOKENS.wInfo, scale)
    out.wList = scalePx(BASE_TOKENS.wList, scale)
    out.wWide = scalePx(BASE_TOKENS.wWide, scale)
    out.wHelp = scalePx(BASE_TOKENS.wHelp, scale)
  }
  return out
}
