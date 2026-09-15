import type { Theme } from '../tokens'

/**
 * 暖色：棕橙画布 + 暖白文字 + 琥珀强调，长时间阅读更柔和。
 */
const theme: Theme = {
  id: 'warm',
  name: '暖色',
  description: '棕橙渐变底 + 暖白文字与琥珀强调，观感柔和',
  version: 1,
  tokens: {
    bg: 'linear-gradient(135deg,#2b1a12,#3f2418,#1d120c)',
    font: '-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif',
    fontMono: '"JetBrains Mono","Consolas",monospace',

    text: '#fff3e6',
    textBody: 'rgba(255,240,226,0.9)',
    textMuted: 'rgba(255,214,180,0.58)',
    textSubtle: 'rgba(240,196,158,0.44)',
    onAccent: '#331a06',
    divider: 'rgba(255,190,130,0.16)',

    cardBg: 'rgba(255,236,214,0.08)',
    cardBorder: 'rgba(255,190,130,0.22)',
    cardRadius: '16px',
    cardShadow: '0 25px 50px -12px rgba(60,20,0,0.6)',
    cardBlur: '20px',
    cardPad: '24px 22px',

    tileBg: 'rgba(255,236,214,0.07)',
    tileBorder: 'rgba(255,190,130,0.18)',
    tileRadius: '12px',
    tileRadiusLg: '14px',
    tileBorderDone: 'rgba(127,209,139,0.45)',

    chipBg: 'rgba(255,236,214,0.08)',
    chipBorder: 'rgba(255,190,130,0.22)',
    chipText: '#ffeada',

    barTrack: 'rgba(255,236,214,0.12)',

    accent: '#ff9f45',
    accentSoft: 'rgba(255,159,69,0.18)',
    accentBorder: 'rgba(255,159,69,0.42)',
    gradAccent: 'linear-gradient(135deg,#ff9f45,#ffc978)',
    info: '#ffcf7a',
    infoSoft: 'rgba(255,207,122,0.14)',
    infoBorder: 'rgba(255,207,122,0.34)',
    gradInfo: 'linear-gradient(90deg,#ff9f45,#ffd9a0)',
    success: '#7fd18b',
    successSoft: 'rgba(127,209,139,0.16)',
    successBorder: 'rgba(127,209,139,0.4)',
    gradSuccess: 'linear-gradient(135deg,#7fd18b,#4fae63)',
    warning: '#ffc857',
    warningSoft: 'rgba(255,200,87,0.16)',
    warningBorder: 'rgba(255,200,87,0.4)',
    danger: '#ff6b5a',
    dangerSoft: 'rgba(255,107,90,0.16)',
    dangerBorder: 'rgba(255,107,90,0.42)',
    gradDanger: 'linear-gradient(135deg,#ff6b5a,#c93a2b)',
    neutralSoft: 'rgba(255,214,180,0.12)',
    neutralBorder: 'rgba(255,214,180,0.25)',

    badgeBg: 'rgba(255,159,69,0.16)',
    badgeBorder: 'rgba(255,159,69,0.4)',

    imgBg: 'linear-gradient(135deg,#3a2418,#241611)',
    imgFilter: 'drop-shadow(0 2px 4px rgba(40,10,0,0.5))',

    glow1: 'rgba(255,159,69,0.22)',
    glow2: 'rgba(255,207,122,0.16)',

    customCss: `
.head-sub,.foot-tag{letter-spacing:1.5px}
.vs-banner{color:#bff0c6}
`,
  },
}

export default theme
