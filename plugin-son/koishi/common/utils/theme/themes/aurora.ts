import type { Theme } from '../tokens'

/**
 * 流光（极简数据流）：青紫流光渐变画布，薄荷强调。
 * 排版语言：极简面板——标题渐变字、字段用「标签…点线…值」数据行、
 * 细进度条、大圆角无重边框、光带扫过动画。
 * 皮肤在组件样式表之后发射（frame.ts），可自然覆盖。
 */
const theme: Theme = {
  id: 'aurora',
  name: '流光',
  description: '极简数据流：标题渐变字，字段标签点线连到值，光带扫过',
  version: 2,
  tokens: {
    bg: 'linear-gradient(135deg,#0b1035,#1b2a5e,#2b1b5e,#143a4e,#0b1035)',
    font: '-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif',
    fontMono: '"JetBrains Mono","Consolas",monospace',

    text: '#eef6ff',
    textBody: 'rgba(234,244,255,0.92)',
    textMuted: 'rgba(200,216,240,0.62)',
    textSubtle: 'rgba(170,190,220,0.5)',
    onAccent: '#06121f',
    divider: 'rgba(255,255,255,0.14)',

    cardBg: 'rgba(255,255,255,0.055)',
    cardBorder: 'rgba(110,240,216,0.25)',
    cardRadius: '22px',
    cardShadow: '0 22px 60px rgba(2,10,30,0.55)',
    cardBlur: '18px',
    cardPad: '26px 24px',

    tileBg: 'rgba(255,255,255,0.05)',
    tileBorder: 'rgba(255,255,255,0.16)',
    tileRadius: '12px',
    tileRadiusLg: '16px',
    tileBorderDone: 'rgba(110,240,216,0.55)',

    chipBg: 'rgba(255,255,255,0.06)',
    chipBorder: 'rgba(255,255,255,0.16)',
    chipText: 'rgba(238,246,255,0.92)',

    barTrack: 'rgba(255,255,255,0.10)',

    accent: '#6ef0d8',
    accentSoft: 'rgba(110,240,216,0.10)',
    accentBorder: 'rgba(110,240,216,0.35)',
    gradAccent: 'linear-gradient(90deg,#6ef0d8,#8ab4ff)',
    info: '#8ab4ff',
    infoSoft: 'rgba(138,180,255,0.10)',
    infoBorder: 'rgba(138,180,255,0.3)',
    gradInfo: 'linear-gradient(90deg,#6ef0d8,#8ab4ff,#c3a6ff)',
    success: '#7ce8b8',
    successSoft: 'rgba(124,232,184,0.10)',
    successBorder: 'rgba(124,232,184,0.32)',
    gradSuccess: 'linear-gradient(90deg,#7ce8b8,#4ecdc4)',
    warning: '#ffd98e',
    warningSoft: 'rgba(255,217,142,0.12)',
    warningBorder: 'rgba(255,217,142,0.35)',
    danger: '#ff8fa8',
    dangerSoft: 'rgba(255,143,168,0.12)',
    dangerBorder: 'rgba(255,143,168,0.35)',
    gradDanger: 'linear-gradient(90deg,#ff8fa8,#ff6b8b)',
    neutralSoft: 'rgba(255,255,255,0.10)',
    neutralBorder: 'rgba(255,255,255,0.22)',

    badgeBg: 'rgba(110,240,216,0.10)',
    badgeBorder: 'rgba(110,240,216,0.32)',

    imgBg: 'linear-gradient(135deg,#0d1440,#10243f)',
    imgFilter: 'drop-shadow(0 2px 6px rgba(0,0,0,0.45))',

    glow1: 'rgba(110,240,216,0.14)',
    glow2: 'rgba(195,166,255,0.12)',

    customCss: `
/* — 画布：流光渐变，缓慢流动 — */
body{
  background-size:220% 220%;
  animation:twAuroraShift 22s ease-in-out infinite alternate;
}
@keyframes twAuroraShift{
  0%{background-position:0% 0%}
  100%{background-position:100% 100%}
}

/* — 卡片：无重边框，光带扫过 — */
.card{
  border:1px solid rgba(110,240,216,0.22);
  border-radius:22px;
  box-shadow:0 22px 60px rgba(2,10,30,0.55);
  background:rgba(255,255,255,0.06);
}
.card.glow::before,.card.glow::after,
.card::before{
  content:'';position:absolute;top:0;left:-70%;width:45%;height:100%;
  background:linear-gradient(105deg,transparent,rgba(255,255,255,0.09),transparent);
  animation:twSweep 7s ease-in-out infinite;pointer-events:none;
}
.card.glow::after{display:none}
@keyframes twSweep{
  0%{left:-70%}
  60%{left:130%}
  100%{left:130%}
}

/* — 标题：渐变字 + 居中（仅单块标题头） — */
.head{flex-wrap:wrap;gap:8px;margin-bottom:20px}
.head>div:only-child{width:100%;text-align:center}
.head-title,.title,.vd-title,.vs-title{
  background:linear-gradient(90deg,#6ef0d8,#8ab4ff,#c3a6ff);
  -webkit-background-clip:text;background-clip:text;color:transparent;
  letter-spacing:1px;
}
.head-sub,.subtitle{
  letter-spacing:4px;text-transform:uppercase;color:rgba(255,255,255,0.5);
}

/* — 玩家信息：单列「标签…点线…值」数据行 — */
.info-grid{display:flex;flex-direction:column}
.info-item{
  display:flex;align-items:baseline;padding:13px 0;
  border-bottom:none;border-bottom:1px solid rgba(255,255,255,0.07);
}
.info-item:last-child{border-bottom:none}
.label{
  display:flex;flex:1;align-items:baseline;margin:0;
  font-size:13px;color:rgba(255,255,255,0.55);letter-spacing:1.5px;
}
.label::after{
  content:'';flex:1;margin:0 10px;
  border-bottom:1px dotted rgba(255,255,255,0.28);
  transform:translateY(-3px);
}
.value{
  flex:none;font-size:16px;font-weight:600;
  background:linear-gradient(90deg,#eaf6ff,#cfe8ff);
  -webkit-background-clip:text;background-clip:text;color:transparent;
}
.footer{
  border-top:1px solid rgba(255,255,255,0.14);
  margin-top:16px;padding-top:14px;
}
.footer-qq{letter-spacing:1px}
.footer-badge{
  border-radius:14px;border:1px solid rgba(110,240,216,0.4);
  color:var(--tw-accent);background:rgba(110,240,216,0.08);font-weight:600;
}

/* — Boss 卡：细进度条 + 柔和图块 — */
.section-head h3{font-weight:600;letter-spacing:0.5px}
.pct{border-radius:14px;font-weight:600}
.bar,.occ-bar{height:4px;border-radius:2px}
.bar-inner,.occ-inner,.vo-fill{border-radius:2px}
.bc{border-radius:12px}
.bc-badge{border-radius:50%}
.bc-name{font-weight:500}

/* — 在线列表：轻胶囊 — */
.online-pill{border-radius:16px;background:rgba(255,255,255,0.05)}
.chip{border-radius:14px;font-weight:500}
.only-badge{border-radius:14px}
.sv{border-radius:16px;border:1px solid rgba(255,255,255,0.14)}

/* — 投票：柔和条目 — */
.rv{border-radius:16px;border:1px solid rgba(255,255,255,0.12)}
.rv-num{border-radius:9px;border:1px solid rgba(110,240,216,0.35)}
.rv-title{letter-spacing:0.5px}
.vo-bar{height:6px;border-radius:3px}
.vo-tag{border-radius:6px}
.vo-check{border-radius:50%}
.vs-stats{border-radius:16px;border:1px solid rgba(110,240,216,0.22);background:rgba(110,240,216,0.05)}
.vs-stat+.vs-stat{border-left:1px solid rgba(110,240,216,0.22)}
.vs-num{font-weight:700}
.vd-desc{border-left:3px solid var(--tw-accent);background:rgba(110,240,216,0.06);border-radius:0 10px 10px 0}
.vd-badge,.rv-badge,.vs-ident-tag{border-radius:10px}

/* — help：小标题细下划线 — */
.sec-title{
  background:none;border-left:none;font-weight:700;letter-spacing:3px;
  color:var(--tw-accent);border-bottom:1px solid rgba(255,255,255,0.12);
  padding-bottom:7px;margin-bottom:8px;
}
.sec-private .sec-title{background:none;color:var(--tw-info);border-bottom-color:rgba(138,180,255,0.4)}
code{
  border-radius:5px;background:rgba(255,255,255,0.06);
  border:1px solid rgba(255,255,255,0.15);color:#9be8ff;
}
.ch{border-radius:10px}
`,
  },
}

export default theme
