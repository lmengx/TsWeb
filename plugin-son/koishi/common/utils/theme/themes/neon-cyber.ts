import type { Theme } from '../tokens'

/**
 * 霓虹：近黑画布 + 青/品红细网格，发光描边与文字辉光。
 * 网格用 repeating-linear-gradient 实现（不得外链资源：
 * 渲染走 page.setContent + waitUntil:networkidle）。
 */
const theme: Theme = {
  id: 'neon-cyber',
  name: '霓虹',
  description: '近黑底 + 青色网格与发光描边，适合年轻群',
  version: 1,
  tokens: {
    bg: '#05060a',
    font: '-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif',
    fontMono: '"JetBrains Mono","Consolas",monospace',

    text: '#e8fbff',
    textBody: 'rgba(214,246,255,0.9)',
    textMuted: 'rgba(150,235,255,0.55)',
    textSubtle: 'rgba(120,200,225,0.42)',
    onAccent: '#04121a',
    divider: 'rgba(0,229,255,0.14)',

    cardBg: 'rgba(9,14,26,0.82)',
    cardBorder: 'rgba(0,229,255,0.35)',
    cardRadius: '8px',
    cardShadow: '0 0 24px rgba(0,229,255,0.18), 0 0 60px rgba(255,43,214,0.10)',
    cardBlur: '0px',
    cardPad: '22px 20px',

    tileBg: 'rgba(0,229,255,0.05)',
    tileBorder: 'rgba(0,229,255,0.22)',
    tileRadius: '6px',
    tileRadiusLg: '8px',
    tileBorderDone: 'rgba(57,255,136,0.5)',

    chipBg: 'rgba(0,229,255,0.08)',
    chipBorder: 'rgba(0,229,255,0.28)',
    chipText: '#d7f6ff',

    barTrack: 'rgba(0,229,255,0.12)',

    accent: '#ff2bd6',
    accentSoft: 'rgba(255,43,214,0.14)',
    accentBorder: 'rgba(255,43,214,0.4)',
    gradAccent: 'linear-gradient(135deg,#ff2bd6,#ff7ce5)',
    info: '#00e5ff',
    infoSoft: 'rgba(0,229,255,0.10)',
    infoBorder: 'rgba(0,229,255,0.3)',
    gradInfo: 'linear-gradient(90deg,#00e5ff,#ff2bd6)',
    success: '#39ff88',
    successSoft: 'rgba(57,255,136,0.12)',
    successBorder: 'rgba(57,255,136,0.35)',
    gradSuccess: 'linear-gradient(135deg,#39ff88,#00c46a)',
    warning: '#ffd166',
    warningSoft: 'rgba(255,209,102,0.14)',
    warningBorder: 'rgba(255,209,102,0.35)',
    danger: '#ff4d6d',
    dangerSoft: 'rgba(255,77,109,0.14)',
    dangerBorder: 'rgba(255,77,109,0.4)',
    gradDanger: 'linear-gradient(135deg,#ff4d6d,#c9184a)',
    neutralSoft: 'rgba(120,140,170,0.18)',
    neutralBorder: 'rgba(120,140,170,0.3)',

    badgeBg: 'rgba(0,229,255,0.12)',
    badgeBorder: 'rgba(0,229,255,0.35)',

    imgBg: 'linear-gradient(135deg,#02060c,#061420)',
    imgFilter: 'drop-shadow(0 0 6px rgba(0,229,255,0.35))',

    glow1: 'rgba(0,229,255,0.20)',
    glow2: 'rgba(255,43,214,0.16)',

    customCss: `
body{
  background-color:#05060a;
  background-image:
    repeating-linear-gradient(0deg,rgba(0,229,255,0.055) 0 1px,transparent 1px 28px),
    repeating-linear-gradient(90deg,rgba(255,43,214,0.045) 0 1px,transparent 1px 28px);
}
.head-title,.title,.vs-title,.online-num,.sv-count,.ob-num{
  text-shadow:0 0 12px rgba(0,229,255,0.45);
}
.head-sub,.foot-tag{letter-spacing:2px}
code,.footer-qq{text-shadow:0 0 8px rgba(0,229,255,0.35)}
.bc-badge{box-shadow:0 0 10px rgba(0,229,255,0.35)}
`,
  },
}

export default theme
