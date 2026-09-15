import type { Theme } from '../tokens'

/**
 * 泰拉（像素/物品栏）：深蓝画布 + 亮蓝 2px 描边、硬阴影。
 * 排版语言：游戏 UI——信息字段做成物品槽格子、标题硬投影、
 * 徽章直角、进度条粗分段、底部像物品栏。
 * 皮肤在组件样式表之后发射（frame.ts），可自然覆盖。
 */
const theme: Theme = {
  id: 'terraria',
  name: '泰拉',
  description: '游戏 UI 风：信息字段做成物品槽格子，粗描边 + 硬投影',
  version: 2,
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
/* — 卡片：粗描边 + 硬投影，去掉柔和光晕 — */
.card{
  border:2px solid rgba(127,168,216,0.8);
  border-radius:4px;
  box-shadow:0 5px 0 rgba(0,0,0,0.55);
  background:rgba(26,44,74,0.92);
}
.card.glow::before,.card.glow::after{display:none}

/* — 标题：硬投影，居中（仅单块标题头） — */
.head{flex-wrap:wrap;gap:8px}
.head>div:only-child{width:100%;text-align:center}
.head-title,.title,.vd-title,.vs-title{
  text-shadow:2px 2px 0 rgba(0,0,0,0.7);
  color:var(--tw-accent);letter-spacing:2px;
}
.head-sub,.subtitle{
  text-shadow:1px 1px 0 rgba(0,0,0,0.6);
  letter-spacing:3px;color:rgba(168,196,232,0.75);
}

/* — 玩家信息：物品槽格子（2 列带边框小格） — */
.info-grid{grid-template-columns:1fr 1fr;gap:8px}
.info-item{
  background:rgba(12,26,46,0.85);
  border:2px solid rgba(127,168,216,0.45);
  border-radius:3px;padding:9px 10px;margin:0;
}
.info-item:nth-last-child(-n+2),.info-item.full-row{border-bottom:2px solid rgba(127,168,216,0.45)}
.info-item.full-row{grid-column:1/-1}
.label{
  font-size:11px;letter-spacing:1px;color:rgba(168,196,232,0.85);
  margin-bottom:3px;
}
.value{font-weight:700}
.footer{
  border-top:2px solid rgba(127,168,216,0.5);
  margin-top:16px;padding-top:12px;
}
.footer-qq{font-family:var(--tw-font-mono);color:rgba(168,196,232,0.7)}
.footer-badge{
  border-radius:3px;border:2px solid var(--tw-accent-border);
  background:var(--tw-accent-soft);color:var(--tw-accent);font-weight:700;
}

/* — Boss 卡：方角图块 + 硬徽章 — */
.section-head h3{text-shadow:2px 2px 0 rgba(0,0,0,0.65);letter-spacing:1px}
.pct{border-radius:3px;font-weight:700;letter-spacing:1px}
.bar,.occ-bar{height:10px;border-radius:0;background:#08131f;border:1px solid rgba(127,168,216,0.35)}
.bar-inner,.occ-inner,.vo-fill{border-radius:0}
.bc{
  border:2px solid rgba(127,168,216,0.55);border-radius:3px;
  box-shadow:0 3px 0 rgba(0,0,0,0.45);
}
.bc.done{border-color:rgba(110,227,110,0.7)}
.bc-badge{border-radius:2px;box-shadow:2px 2px 0 rgba(0,0,0,0.6)}
.bc-name{font-weight:700}
.bc-count{font-family:var(--tw-font-mono)}

/* — 在线列表：物品胶囊 — */
.online-pill{
  border-radius:3px;border:2px solid rgba(127,168,216,0.5);
  background:rgba(12,26,46,0.85);
}
.chip{
  border-radius:3px;border:2px solid rgba(255,215,94,0.4);
  background:rgba(255,215,94,0.08);font-weight:600;
}
.only-badge{border-radius:3px;border:2px solid var(--tw-badge-border)}
.ob-num,.online-num{font-family:var(--tw-font-mono)}
.sv{border:2px solid rgba(127,168,216,0.55);border-radius:3px}

/* — 投票：方块编号 + 分段条 — */
.rv{border:2px solid rgba(127,168,216,0.45);border-radius:3px;background:rgba(12,26,46,0.8)}
.rv-num{
  border-radius:3px;border:2px solid var(--tw-info-border);
  background:var(--tw-info-soft);font-family:var(--tw-font-mono);
}
.rv-badge,.vd-badge,.vs-ident-tag{border-radius:3px}
.vo-bar{height:10px;border-radius:0;background:#08131f;border:1px solid rgba(127,168,216,0.35)}
.vo-tag{border-radius:3px}
.vo-check{border-radius:3px;box-shadow:2px 2px 0 rgba(0,0,0,0.5)}
.vs-stats{border:2px solid rgba(127,168,216,0.45);border-radius:3px}
.vs-stat+.vs-stat{border-left:2px solid rgba(127,168,216,0.45)}
.vs-num{font-family:var(--tw-font-mono)}
.vd-desc{border-left:4px solid var(--tw-info);border-radius:0;background:rgba(60,120,200,0.12)}

/* — help：像素指令块 — */
.sec-title{
  border-radius:3px;letter-spacing:1px;
  background:rgba(60,120,200,0.28);border-left:4px solid var(--tw-info);
}
.sec-private .sec-title{background:rgba(255,215,94,0.18);border-left-color:var(--tw-accent);color:var(--tw-accent)}
code{
  border-radius:3px;border:1px solid rgba(127,168,216,0.55);
  background:rgba(12,26,46,0.9);
}
.ch{border-radius:3px}
`,
  },
}

export default theme
