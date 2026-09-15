import type { Theme } from '../tokens'

/**
 * 霓虹（终端/赛博）：近黑画布 + 青/品红网格 + CRT 扫描线。
 * 排版语言：终端风——标题带 > 提示符、字段用 [标签] 方括号、
 * 数字全等宽发光、分割线用虚线、命令带 $ 前缀、徽章直角。
 * 皮肤在组件样式表之后发射（frame.ts），可自然覆盖。
 */
const theme: Theme = {
  id: 'neon-cyber',
  name: '霓虹',
  description: '终端风格：黑底网格 + 扫描线，标题带 > 提示符，数字等宽发光',
  version: 2,
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
/* — 画布：网格 + CRT 扫描线 — */
body{
  background-color:#05060a;
  background-image:
    repeating-linear-gradient(0deg,rgba(0,229,255,0.055) 0 1px,transparent 1px 28px),
    repeating-linear-gradient(90deg,rgba(255,43,214,0.045) 0 1px,transparent 1px 28px);
}
body::after{
  content:'';position:fixed;inset:0;pointer-events:none;z-index:99;
  background:repeating-linear-gradient(0deg,rgba(0,229,255,0.025) 0 1px,transparent 1px 3px);
}

/* — 卡片：发光描边，直角内框 — */
.card{
  border:1px solid rgba(0,229,255,0.5);
  border-radius:8px;
  box-shadow:0 0 18px rgba(0,229,255,0.22),0 0 50px rgba(255,43,214,0.12);
  background:rgba(9,14,26,0.9);
}

/* — 标题：> 提示符 + 辉光 — */
.head{flex-wrap:wrap;gap:8px;padding-bottom:12px;border-bottom:1px dashed rgba(0,229,255,0.3);margin-bottom:16px}
.head-title,.title,.vd-title,.vs-title{
  text-shadow:0 0 12px rgba(0,229,255,0.5),0 0 30px rgba(0,229,255,0.25);
  letter-spacing:1px;
}
.head-title::before,.title::before{content:'> ';color:var(--tw-info);font-family:var(--tw-font-mono)}
.head-sub,.subtitle,.foot-tag,.sv-count{
  font-family:var(--tw-font-mono);letter-spacing:2px;
}
.head-sub::before{content:'// ';color:rgba(0,229,255,0.5)}

/* — 玩家信息：单列终端行，[标签] 方括号 — */
.info-grid{grid-template-columns:1fr}
.info-item{
  padding:9px 0;border-bottom:1px dashed rgba(0,229,255,0.12);
  display:flex;justify-content:space-between;align-items:baseline;gap:12px
}
.info-item:last-child{border-bottom:none}
.label{
  font-family:var(--tw-font-mono);letter-spacing:1px;margin-bottom:0;
}
.label::before{content:'[';color:rgba(0,229,255,0.55)}
.label::after{content:']';color:rgba(0,229,255,0.55)}
.value{
  font-family:var(--tw-font-mono);text-align:right;
  text-shadow:0 0 8px rgba(0,229,255,0.3);
}
.footer{border-top:1px dashed rgba(0,229,255,0.3)}
.footer-qq{font-family:var(--tw-font-mono);text-shadow:0 0 8px rgba(0,229,255,0.35)}
.footer-badge{
  border-radius:3px;font-family:var(--tw-font-mono);letter-spacing:2px;
  box-shadow:0 0 10px rgba(255,43,214,0.35);
}

/* — Boss 卡：直角图块 + 等宽数字 — */
.section-head h3{letter-spacing:1px;text-shadow:0 0 10px rgba(0,229,255,0.35)}
.pct{font-family:var(--tw-font-mono);border-radius:3px;letter-spacing:1px}
.bar,.occ-bar{height:8px;border-radius:2px;background:rgba(0,229,255,0.10)}
.bar-inner,.occ-inner,.vo-fill{border-radius:2px;box-shadow:0 0 8px rgba(0,229,255,0.35)}
.bc{border:1px solid rgba(0,229,255,0.25);border-radius:4px}
.bc.done{border-color:rgba(57,255,136,0.5)}
.bc-badge{border-radius:3px;box-shadow:0 0 10px rgba(0,229,255,0.4)}
.bc-count{font-family:var(--tw-font-mono)}

/* — 在线列表：终端行胶囊 — */
.online-pill{
  border-radius:3px;font-family:var(--tw-font-mono);
  border:1px solid rgba(0,229,255,0.3);
}
.chip{border-radius:3px;font-family:var(--tw-font-mono)}
.only-badge{border-radius:3px}
.ob-num,.online-num{font-family:var(--tw-font-mono);text-shadow:0 0 12px rgba(0,229,255,0.45)}
.sv{border:1px solid rgba(0,229,255,0.22);border-radius:8px}

/* — 投票：列表项直角 + 编号方块 — */
.rv{border-radius:6px;border:1px solid rgba(0,229,255,0.2)}
.rv-num{border-radius:3px;font-family:var(--tw-font-mono);border:1px solid var(--tw-info-border)}
.rv-title{letter-spacing:0.5px}
.vo-bar{height:8px;border-radius:2px;background:rgba(0,229,255,0.10)}
.vo-tag{border-radius:3px}
.vo-check{border-radius:3px;box-shadow:0 0 8px rgba(57,255,136,0.4)}
.vs-stats{border:1px solid rgba(0,229,255,0.25);border-radius:6px}
.vs-stat+.vs-stat{border-left:1px solid rgba(0,229,255,0.25)}
.vs-num{font-family:var(--tw-font-mono);text-shadow:0 0 10px rgba(0,229,255,0.4)}
.vd-desc{border-left:3px solid var(--tw-info);background:rgba(0,229,255,0.06)}
.vd-badge,.rv-badge,.vs-ident-tag{border-radius:3px}

/* — help：终端指令块，$ 前缀 — */
.sec-title{
  font-family:var(--tw-font-mono);letter-spacing:1px;background:none;
  border-left:3px solid var(--tw-info);
}
.sec-title::before{content:'> ';color:var(--tw-info)}
.sec-private .sec-title{background:none;border-left-color:var(--tw-accent);color:var(--tw-accent)}
.sec-private .sec-title::before{content:'> ';color:var(--tw-accent)}
code{
  background:#04121a;border:1px solid rgba(0,229,255,0.35);border-radius:3px;
  color:#7cf7ff;text-shadow:0 0 8px rgba(0,229,255,0.35);
}
code::before{content:'$ ';color:var(--tw-success)}
.ch{border-radius:3px;font-family:var(--tw-font-mono)}
`,
  },
}

export default theme
