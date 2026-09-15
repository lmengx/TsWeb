import type { Theme } from '../tokens'

/**
 * 泰拉：深蓝画布 + 亮蓝游戏 UI 描边、像素感硬边与硬阴影。
 * 结构差异（2px 描边、方角徽章、文字投影）走 customCss。
 */
const theme: Theme = {
  id: 'terraria',
  name: '泰拉',
  description: '深蓝底 + 亮蓝描边与硬阴影，贴合泰拉瑞亚 UI 质感',
  version: 1,
  tokens: {
    bg: 'linear-gradient(160deg,#10254a,#0a1220)',
    font: '"Trebuchet MS",-apple-system,BlinkMacSystemFont,"Noto Sans SC",sans-serif',
    fontMono: '"Consolas","JetBrains Mono",monospace',

    text: '#eaf3ff',
    textBody: 'rgba(226,238,255,0.9)',
    textMuted: 'rgba(168,196,232,0.68)',
    textSubtle: 'rgba(140,172,212,0.5)',
    onAccent: '#1a1206',
    divider: 'rgba(127,168,216,0.28)',

    cardBg: 'rgba(26,44,74,0.88)',
    cardBorder: 'rgba(127,168,216,0.75)',
    cardRadius: '4px',
    cardShadow: '0 4px 0 rgba(0,0,0,0.55)',
    cardBlur: '0px',
    cardPad: '20px 18px',

    tileBg: 'rgba(12,26,46,0.85)',
    tileBorder: 'rgba(127,168,216,0.55)',
    tileRadius: '3px',
    tileRadiusLg: '4px',
    tileBorderDone: 'rgba(110,227,110,0.7)',

    chipBg: 'rgba(255,215,94,0.10)',
    chipBorder: 'rgba(255,215,94,0.4)',
    chipText: '#ffe9a8',

    barTrack: '#08131f',

    accent: '#ffd75e',
    accentSoft: 'rgba(255,215,94,0.14)',
    accentBorder: 'rgba(255,215,94,0.45)',
    gradAccent: 'linear-gradient(180deg,#ffe9a8,#ffb52e)',
    info: '#a9d6ff',
    infoSoft: 'rgba(60,120,200,0.18)',
    infoBorder: 'rgba(127,168,216,0.45)',
    gradInfo: 'linear-gradient(180deg,#a9d6ff,#5a9be0)',
    success: '#6ee36e',
    successSoft: 'rgba(110,227,110,0.14)',
    successBorder: 'rgba(110,227,110,0.5)',
    gradSuccess: 'linear-gradient(180deg,#9bf59b,#43c443)',
    warning: '#ffd75e',
    warningSoft: 'rgba(255,215,94,0.14)',
    warningBorder: 'rgba(255,215,94,0.45)',
    danger: '#ff6b6b',
    dangerSoft: 'rgba(255,107,107,0.14)',
    dangerBorder: 'rgba(255,107,107,0.5)',
    gradDanger: 'linear-gradient(180deg,#ff9b9b,#e03b3b)',
    neutralSoft: 'rgba(127,168,216,0.14)',
    neutralBorder: 'rgba(127,168,216,0.35)',

    badgeBg: 'rgba(255,215,94,0.16)',
    badgeBorder: 'rgba(255,215,94,0.5)',

    imgBg: 'linear-gradient(160deg,#16305a,#0a1728)',
    imgFilter: 'drop-shadow(0 2px 0 rgba(0,0,0,0.6))',

    glow1: 'rgba(255,215,94,0.10)',
    glow2: 'rgba(110,227,110,0.08)',

    customCss: `
.wrap.card,.sv,.rv,.bc{
  border-width:2px;
  border-style:solid;
}
.head-title,.title,.vs-title,.rv-title,.sv-name,.section-head h3{
  text-shadow:1px 1px 0 rgba(0,0,0,0.65);
}
.pct,.badge,.rv-badge,.vd-badge,.vs-ident-tag,.ch{
  border-radius:3px;
}
.bc-badge{border-radius:2px;box-shadow:0 2px 0 rgba(0,0,0,0.6)}
.bc-img img{image-rendering:auto}
.only-badge{border-radius:3px}
`,
  },
}

export default theme
