import type { Theme } from '../tokens'

/**
 * 暖色（杂志/纸张）：暖棕画布 + 琥珀强调，衬线标题。
 * 排版语言：杂志版式——
 * 刊头 masthead（粗双线+细线、衬线大写宽字距标题、斜体副标题）、
 * 信息字段带杂志条目编号（01/02/…）、正文两栏竖线分栏、
 * help 分节标题带节编号 + 底部双线、引言块大引号、页脚双细线、极淡纸张横纹。
 * 皮肤在组件样式表之后发射（frame.ts），可自然覆盖。
 */
const theme: Theme = {
  id: 'warm',
  name: '暖色',
  description: '杂志风：刊头双线衬线标题，条目编号，两栏竖线分栏，引言大引号',
  version: 3,
  tokens: {
    bg: 'linear-gradient(135deg,#2b1a12,#3f2418,#1d120c)',
    font: '-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif',
    fontMono: '"Consolas","JetBrains Mono",monospace',

    text: '#fff3e6',
    textBody: 'rgba(255,243,230,0.92)',
    textMuted: 'rgba(224,196,166,0.72)',
    textSubtle: 'rgba(200,168,134,0.55)',
    onAccent: '#2b1a12',
    divider: 'rgba(255,214,150,0.22)',

    cardBg: 'rgba(60,36,22,0.75)',
    cardBorder: 'rgba(255,214,150,0.35)',
    cardRadius: '16px',
    cardShadow: '0 14px 34px rgba(0,0,0,0.4)',
    cardBlur: '0px',
    cardPad: '26px 24px',

    tileBg: 'rgba(255,214,150,0.06)',
    tileBorder: 'rgba(255,214,150,0.28)',
    tileRadius: '10px',
    tileRadiusLg: '12px',
    tileBorderDone: 'rgba(158,224,152,0.5)',

    chipBg: 'rgba(255,214,150,0.08)',
    chipBorder: 'rgba(255,214,150,0.32)',
    chipText: '#ffe6c4',

    barTrack: 'rgba(255,214,150,0.14)',

    accent: '#ff9f45',
    accentSoft: 'rgba(255,159,69,0.14)',
    accentBorder: 'rgba(255,159,69,0.4)',
    gradAccent: 'linear-gradient(135deg,#ffb366,#ff8c2e)',
    info: '#ffcf7a',
    infoSoft: 'rgba(255,207,122,0.12)',
    infoBorder: 'rgba(255,207,122,0.35)',
    gradInfo: 'linear-gradient(90deg,#ffb35c,#ffcf7a)',
    success: '#9ee098',
    successSoft: 'rgba(158,224,152,0.12)',
    successBorder: 'rgba(158,224,152,0.35)',
    gradSuccess: 'linear-gradient(135deg,#9ee098,#6fbf6a)',
    warning: '#ffcf7a',
    warningSoft: 'rgba(255,207,122,0.14)',
    warningBorder: 'rgba(255,207,122,0.4)',
    danger: '#ff8b7a',
    dangerSoft: 'rgba(255,139,122,0.12)',
    dangerBorder: 'rgba(255,139,122,0.38)',
    gradDanger: 'linear-gradient(135deg,#ff8b7a,#e0553f)',
    neutralSoft: 'rgba(255,214,150,0.12)',
    neutralBorder: 'rgba(255,214,150,0.3)',

    badgeBg: 'rgba(255,159,69,0.16)',
    badgeBorder: 'rgba(255,159,69,0.42)',

    imgBg: 'linear-gradient(135deg,#3a2416,#241408)',
    imgFilter: 'drop-shadow(0 2px 3px rgba(0,0,0,0.5))',

    glow1: 'rgba(255,159,69,0.16)',
    glow2: 'rgba(255,207,122,0.12)',

    customCss: `
/* — 卡片：纸感圆角 + 极淡纸张横纹 — */
.card{
  border:1px solid rgba(255,214,150,0.35);
  border-radius:16px;
  box-shadow:0 14px 34px rgba(0,0,0,0.4);
  background:
    repeating-linear-gradient(0deg,rgba(255,255,255,0.016) 0 1px,transparent 1px 3px),
    rgba(60,36,22,0.8);
  counter-reset:twSec;
}

/* — 刊头 masthead：粗双线 + 下方细线，衬线大写宽字距标题，斜体副标题 — */
.head{
  flex-wrap:wrap;gap:8px;
  padding-top:16px;
  border-top:4px double rgba(255,214,150,0.55);
  box-shadow:0 7px 0 -5px rgba(255,214,150,0.3);
  margin-bottom:22px;
}
.head>div:only-child{width:100%;text-align:center}
.head-title,.title,.vd-title,.vs-title{
  font-family:Georgia,'Times New Roman',serif;
  font-weight:700;letter-spacing:5px;font-size:25px;
}
.head-sub,.subtitle{
  font-family:Georgia,'Times New Roman',serif;
  font-style:italic;letter-spacing:4px;font-size:12px;
  text-transform:uppercase;color:rgba(255,207,122,0.78);
}
.head.rule{border-bottom:1px solid rgba(255,214,150,0.35)}

/* — 玩家信息：两栏竖分割线 + 每条目杂志编号（01/02/…） — */
.info-grid{
  grid-template-columns:1fr 1fr;
  counter-reset:twMag;
}
.info-item{
  padding:13px 0;
  counter-increment:twMag;
}
.info-item:nth-child(even):not(.full-row){border-left:1px solid rgba(255,214,150,0.28);padding-left:18px}
.info-item:nth-last-child(-n+2){border-bottom:none}
.info-item::before{
  content:counter(twMag,decimal-leading-zero);
  display:block;font-family:Georgia,'Times New Roman',serif;
  font-size:11px;letter-spacing:1px;
  color:rgba(255,159,69,0.55);margin-bottom:3px;
}
.label{
  font-family:Georgia,'Times New Roman',serif;
  font-size:12px;letter-spacing:2px;color:rgba(255,207,122,0.85);
  margin-bottom:5px;
}
.value{font-weight:600}
.footer{
  border-top:1px solid rgba(255,214,150,0.35);
  box-shadow:0 5px 0 -3px rgba(255,214,150,0.18);
  margin-top:18px;padding-top:16px;
}
.footer-qq{font-family:Georgia,'Times New Roman',serif;font-style:italic}
.footer-badge{
  border-radius:4px;border:1px solid var(--tw-warning-border);
  background:var(--tw-warning-soft);color:var(--tw-warning);
}

/* — Boss 卡 — */
.server-name{border-radius:4px}
.section-head h3{
  font-family:Georgia,'Times New Roman',serif;
  letter-spacing:2px;font-size:17px;
}
.pct{border-radius:4px}
.bar,.occ-bar{height:8px;border-radius:4px;background:rgba(255,214,150,0.14)}
.bar-inner,.occ-inner,.vo-fill{border-radius:4px}
.bc{border-radius:10px}
.bc-badge{border-radius:50%}
.bc-count{font-style:italic}

/* — 在线列表 — */
.online-pill{border-radius:12px}
.chip{border-radius:12px;font-weight:500}
.only-badge{border-radius:12px}
.sv{border-radius:12px}
.sv-name{font-family:Georgia,'Times New Roman',serif;letter-spacing:1px}

/* — 投票：软圆角条目 + 引言块大引号 — */
.rv{border-radius:12px}
.rv-num{
  border-radius:50%;border:1px solid var(--tw-warning-border);
  background:var(--tw-warning-soft);color:var(--tw-warning);
  font-family:Georgia,'Times New Roman',serif;
}
.rv-title{letter-spacing:0.5px}
.vo-bar{height:8px;border-radius:4px;background:rgba(255,214,150,0.14)}
.vo-tag{border-radius:4px}
.vo-check{border-radius:50%}
.vs-stats{border-radius:12px;background:rgba(255,207,122,0.08)}
.vs-stat+.vs-stat{border-left:1px solid rgba(255,207,122,0.3)}
.vs-num{font-family:Georgia,'Times New Roman',serif}
.vd-desc{
  border-left:3px solid var(--tw-warning);border-radius:0 10px 10px 0;
  background:rgba(255,214,150,0.1);
  position:relative;padding:14px 16px 14px 28px;font-style:italic;
}
.vd-desc::before{
  content:'"';position:absolute;left:9px;top:2px;
  font-family:Georgia,'Times New Roman',serif;font-size:38px;line-height:1;
  color:rgba(255,159,69,0.4);
}
.vd-badge,.rv-badge,.vs-ident-tag{border-radius:4px}

/* — help：杂志栏目——衬线大写 + 节编号（01/02/…）+ 底部双线 — */
.sec{counter-increment:twSec}
.sec-title{
  font-family:Georgia,'Times New Roman',serif;
  font-size:15px;font-weight:700;
  letter-spacing:4px;
  color:var(--tw-warning);
  border-left:none;background:none;
  border-bottom:1px solid rgba(255,214,150,0.4);
  box-shadow:0 4px 0 -2px rgba(255,214,150,0.2);
  padding:0 0 9px;margin-bottom:12px;
}
.sec-title::before{
  content:counter(twSec,decimal-leading-zero) '  ';
  color:rgba(255,159,69,0.7);
}
.sec-private .sec-title{
  border-bottom-color:rgba(255,159,69,0.5);
  color:var(--tw-accent);
}
.sec-private .sec-title::before{color:var(--tw-accent)}
code{
  border-radius:4px;background:rgba(255,214,150,0.10);
  border:1px solid rgba(255,214,150,0.3);color:#ffd9a0;
}
.ch{border-radius:4px}
`,
  },
}

export default theme
