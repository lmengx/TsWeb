import type { Theme } from '../tokens'

/**
 * 流光：多停靠点冷色渐变画布 + 薄荷描边与顶部流光带。
 * 说明：send 的是静态截图，位移动画只在实时预览（浏览器打开生成的
 * HTML）时可见，截图上呈现为渐变光带 —— 这是截图渲染路径的固有限制。
 */
const theme: Theme = {
  id: 'aurora',
  name: '流光',
  description: '青紫多色渐变画布 + 薄荷流光描边（截图定格为渐变光带）',
  version: 1,
  tokens: {
    bg: 'linear-gradient(135deg,#04121f 0%,#072a33 26%,#12143a 52%,#2a1038 76%,#06121f 100%)',
    font: '-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif',
    fontMono: '"JetBrains Mono","Consolas",monospace',

    text: '#eafffb',
    textBody: 'rgba(226,255,250,0.9)',
    textMuted: 'rgba(160,235,225,0.58)',
    textSubtle: 'rgba(140,205,200,0.44)',
    onAccent: '#04211c',
    divider: 'rgba(140,255,232,0.16)',

    cardBg: 'rgba(255,255,255,0.07)',
    cardBorder: 'rgba(140,255,232,0.22)',
    cardRadius: '16px',
    cardShadow: '0 25px 50px -12px rgba(2,20,30,0.7), 0 0 40px rgba(110,240,216,0.10)',
    cardBlur: '18px',
    cardPad: '24px 22px',

    tileBg: 'rgba(255,255,255,0.06)',
    tileBorder: 'rgba(140,255,232,0.18)',
    tileRadius: '12px',
    tileRadiusLg: '14px',
    tileBorderDone: 'rgba(88,232,168,0.5)',

    chipBg: 'rgba(255,255,255,0.07)',
    chipBorder: 'rgba(140,255,232,0.2)',
    chipText: '#d8fff8',

    barTrack: 'rgba(255,255,255,0.1)',

    accent: '#6ef0d8',
    accentSoft: 'rgba(110,240,216,0.16)',
    accentBorder: 'rgba(110,240,216,0.38)',
    gradAccent: 'linear-gradient(135deg,#6ef0d8,#7fa8ff)',
    info: '#7fa8ff',
    infoSoft: 'rgba(127,168,255,0.13)',
    infoBorder: 'rgba(127,168,255,0.32)',
    gradInfo: 'linear-gradient(90deg,#7fa8ff,#c07cff)',
    success: '#58e8a8',
    successSoft: 'rgba(88,232,168,0.14)',
    successBorder: 'rgba(88,232,168,0.38)',
    gradSuccess: 'linear-gradient(135deg,#58e8a8,#2fbf85)',
    warning: '#ffd98a',
    warningSoft: 'rgba(255,217,138,0.14)',
    warningBorder: 'rgba(255,217,138,0.35)',
    danger: '#ff7a90',
    dangerSoft: 'rgba(255,122,144,0.14)',
    dangerBorder: 'rgba(255,122,144,0.38)',
    gradDanger: 'linear-gradient(135deg,#ff7a90,#d94a63)',
    neutralSoft: 'rgba(180,200,220,0.16)',
    neutralBorder: 'rgba(180,200,220,0.28)',

    badgeBg: 'rgba(110,240,216,0.14)',
    badgeBorder: 'rgba(110,240,216,0.36)',

    imgBg: 'linear-gradient(135deg,#072a33,#12143a)',
    imgFilter: 'drop-shadow(0 2px 4px rgba(0,0,0,0.45))',

    glow1: 'rgba(110,240,216,0.22)',
    glow2: 'rgba(192,124,255,0.20)',

    customCss: `
body{background-size:220% 220%;animation:twAuroraShift 18s ease-in-out infinite}
@keyframes twAuroraShift{
  0%{background-position:0% 50%}
  50%{background-position:100% 50%}
  100%{background-position:0% 50%}
}
.wrap.card,.sv::before{
  position:relative;
}
.sv,.rv{background-image:linear-gradient(115deg,transparent 40%,rgba(110,240,216,0.06) 50%,transparent 60%)}
.head-title,.title,.vs-title{text-shadow:0 0 18px rgba(110,240,216,0.35)}
.head-sub,.foot-tag{letter-spacing:2px}
`,
  },
}

export default theme
