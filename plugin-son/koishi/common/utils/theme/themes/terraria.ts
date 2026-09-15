import type { Theme } from '../tokens'

/**
 * 泰拉（亮色像素风，参考 TRBBS 泰拉瑞亚中文论坛首页）：
 * 画布 = 蓝天 → 海面 → 沙滩三带渐变，云与太阳画入背景层（不遮挡内容）；
 * 卡片 = 不透明奶油面板 + 棕色 2px 像素边框 + 硬阴影；
 * Boss 卡无 .card 容器，由 .wrap.w-wide 承担面板；
 * 分节标题 = 绿色横条（对应论坛绿色导航条）；物品槽格子保留。
 * 皮肤在组件样式表之后发射（frame.ts），可自然覆盖。
 */
const theme: Theme = {
  id: 'terraria',
  name: '泰拉',
  description: '亮色像素风：蓝天白云海面沙滩画布，绿色导航条分节标题，棕色物品槽面板',
  version: 3,
  tokens: {
    bg: 'linear-gradient(180deg,#3f9ee8 0%,#6cbcf5 34%,#a8ddfb 52%,#2f92d4 54%,#2f92d4 63%,#f6e3a8 65%,#f2d98c 80%,#e8c97a 100%)',
    font: '"Trebuchet MS",-apple-system,BlinkMacSystemFont,"Noto Sans SC",sans-serif',
    fontMono: '"Consolas","JetBrains Mono",monospace',

    text: '#2b2418',
    textBody: 'rgba(43,36,24,0.9)',
    textMuted: 'rgba(43,36,24,0.68)',
    textSubtle: 'rgba(43,36,24,0.55)',
    onAccent: '#fffdf2',
    divider: 'rgba(43,36,24,0.14)',

    cardBg: '#fffaf0',
    cardBorder: '#7a5c2e',
    cardRadius: '4px',
    cardShadow: '0 4px 0 rgba(90,60,20,0.4), 0 12px 24px rgba(60,40,10,0.28)',
    cardBlur: '0px',
    cardPad: '22px 20px',

    tileBg: '#f3e9cd',
    tileBorder: '#a3864f',
    tileRadius: '3px',
    tileRadiusLg: '4px',
    tileBorderDone: 'rgba(63,174,74,0.8)',

    chipBg: '#f6ecd2',
    chipBorder: '#b39a63',
    chipText: '#3a2f1d',

    barTrack: '#ddd2b4',

    accent: '#3f9d3f',
    accentSoft: 'rgba(63,157,63,0.16)',
    accentBorder: 'rgba(63,157,63,0.55)',
    gradAccent: 'linear-gradient(180deg,#57b057,#2f8a2f)',
    info: '#2b7fd0',
    infoSoft: 'rgba(43,127,208,0.12)',
    infoBorder: 'rgba(43,127,208,0.45)',
    gradInfo: 'linear-gradient(180deg,#4a9ae8,#2b7fd0)',
    success: '#3fae4a',
    successSoft: 'rgba(63,174,74,0.14)',
    successBorder: 'rgba(63,174,74,0.55)',
    gradSuccess: 'linear-gradient(180deg,#57c35f,#2f9a3a)',
    warning: '#c8931f',
    warningSoft: 'rgba(200,147,31,0.16)',
    warningBorder: 'rgba(200,147,31,0.5)',
    danger: '#c0392b',
    dangerSoft: 'rgba(192,57,43,0.12)',
    dangerBorder: 'rgba(192,57,43,0.5)',
    gradDanger: 'linear-gradient(180deg,#d44a3a,#a83226)',
    neutralSoft: 'rgba(43,36,24,0.08)',
    neutralBorder: 'rgba(43,36,24,0.2)',

    badgeBg: 'rgba(63,157,63,0.14)',
    badgeBorder: 'rgba(63,157,63,0.5)',

    imgBg: '#e9e2cc',
    imgFilter: 'drop-shadow(2px 2px 0 rgba(90,60,20,0.25))',

    glow1: 'rgba(255,255,255,0)',
    glow2: 'rgba(255,255,255,0)',

    customCss: `
/* — 画布：云 + 太阳画入背景层（在卡片之后，绝不遮挡内容） — */
body{
  background:
    /* 云 1-4（白色圆角云朵，位于天空带） */
    radial-gradient(46px 22px at 12% 14%,rgba(255,255,255,0.92) 60%,transparent 61%),
    radial-gradient(46px 22px at 12% 18%,rgba(255,255,255,0.92) 60%,transparent 61%),
    radial-gradient(60px 24px at 34% 9%,rgba(255,255,255,0.88) 60%,transparent 61%),
    radial-gradient(40px 18px at 68% 16%,rgba(255,255,255,0.85) 60%,transparent 61%),
    radial-gradient(52px 22px at 86% 11%,rgba(255,255,255,0.9) 60%,transparent 61%),
    /* 太阳（右上角天空） */
    radial-gradient(circle at calc(100% - 60px) 46px,#ffd75e 0 30px,rgba(255,215,94,0.5) 31px 40px,transparent 41px),
    /* 天空 → 海面 → 沙滩 */
    linear-gradient(180deg,#3f9ee8 0%,#6cbcf5 34%,#a8ddfb 52%,#2f92d4 54%,#2f92d4 63%,#f6e3a8 65%,#f2d98c 80%,#e8c97a 100%);
  background-attachment:fixed;
}

/* — 卡片：不透明奶油面板 + 棕色像素边框；底部加留白给装饰带 — */
.card{
  border:2px solid #7a5c2e;
  border-radius:4px;
  box-shadow:0 4px 0 rgba(90,60,20,0.4),0 12px 24px rgba(60,40,10,0.28);
  background:#fffaf0;
  padding-bottom:56px;
}

/* — 底部彩虹像素条（泰拉彩蛋）：红橙黄绿蓝紫 6px 一格，缓慢流动 — */
.card::after,.card.glow::after,.wrap.w-wide::after{
  content:'';position:absolute;top:auto;right:0;bottom:0;left:0;
  width:auto;height:6px;pointer-events:none;
  background:repeating-linear-gradient(90deg,
    #e74c3c 0 6px,#e67e22 6px 12px,#f1c40f 12px 18px,
    #2ecc71 18px 24px,#3498db 24px 30px,#9b59b6 30px 36px);
  background-size:36px 100%;
  animation:twTerraRainbow 1.2s steps(6) infinite;
}
@keyframes twTerraRainbow{to{background-position:36px 0}}

/* — Boss 卡无 .card 容器，由 .wrap.w-wide 承担面板（含底部装饰带） — */
.wrap.w-wide{
  position:relative;
  background:#fffaf0;
  border:2px solid #7a5c2e;border-radius:4px;
  padding:22px 20px 56px;
  box-shadow:0 4px 0 rgba(90,60,20,0.4),0 12px 24px rgba(60,40,10,0.28);
}

/* — 标题：深棕字 + 白描边（亮底反相的游戏文字），单块标题居中 — */
.head{flex-wrap:wrap;gap:8px}
.head>div:only-child{width:100%;text-align:center}
.head-title,.title,.vd-title,.vs-title{
  color:#4a3520;
  text-shadow:1px 1px 0 rgba(255,255,255,0.9);
  letter-spacing:2px;
}
.head-sub,.subtitle{
  color:rgba(74,53,32,0.7);
  text-shadow:1px 1px 0 rgba(255,255,255,0.8);
  letter-spacing:3px;
}

/* — 玩家信息：奶油物品槽格子（2 列带边框小格） — */
.info-grid{grid-template-columns:1fr 1fr;gap:8px}
.info-item{
  background:#f3e9cd;
  border:2px solid #a3864f;
  border-radius:3px;padding:9px 10px;margin:0;
}
.info-item:nth-last-child(-n+2),.info-item.full-row{border-bottom:2px solid #a3864f}
.info-item.full-row{grid-column:1/-1}
.label{
  font-size:11px;letter-spacing:1px;color:#8a6d3b;
  margin-bottom:3px;
}
.value{font-weight:700;color:#2b2418}
.footer{
  border-top:2px solid #a3864f;
  margin-top:16px;padding-top:12px;
}
.footer-qq{font-family:var(--tw-font-mono);color:rgba(74,53,32,0.7)}
.footer-badge{
  border-radius:3px;border:2px solid #2f8a2f;
  background:#3f9d3f;color:#fffdf2;font-weight:700;
  text-shadow:1px 1px 0 rgba(0,0,0,0.3);
}

/* — Boss 卡：棕色像素图块 — */
.server-name{
  border-radius:3px;border:2px solid #2f8a2f;
  background:#3f9d3f;color:#fffdf2;
  text-shadow:1px 1px 0 rgba(0,0,0,0.3);
}
.section-head h3{color:#4a3520;text-shadow:1px 1px 0 rgba(255,255,255,0.9);letter-spacing:1px}
.pct{border-radius:3px;font-weight:700;letter-spacing:1px}
.bar,.occ-bar{
  height:10px;border-radius:0;background:#ddd2b4;
  border:1px solid #c9bc97;
}
.bar-inner,.occ-inner,.vo-fill{border-radius:0}
.bc{
  border:2px solid #a3864f;border-radius:3px;background:#f3e9cd;
  box-shadow:0 3px 0 rgba(90,60,20,0.3);
}
.bc.done{border-color:rgba(63,174,74,0.8)}
.bc-badge{border-radius:2px;box-shadow:2px 2px 0 rgba(0,0,0,0.35)}
.bc-name{color:#4a3520;font-weight:700}
.bc-count{font-family:var(--tw-font-mono);color:rgba(74,53,32,0.6)}

/* — 在线列表：奶油物品胶囊 — */
.online-pill{
  border-radius:3px;border:2px solid #a3864f;
  background:#f6ecd2;
}
.chip{
  border-radius:3px;border:2px solid #b39a63;
  background:#f6ecd2;font-weight:600;color:#3a2f1d;
}
.only-badge{border-radius:3px;border:2px solid #b39a63}
.ob-num,.online-num{font-family:var(--tw-font-mono)}
.sv{border:2px solid #a3864f;border-radius:3px;background:#fbf4e0}

/* — 投票：奶油条目 + 绿色编号块 — */
.rv{border:2px solid #a3864f;border-radius:3px;background:#f6ecd2}
.rv-num{
  border-radius:3px;border:2px solid #2f8a2f;
  background:#3f9d3f;color:#fffdf2;font-family:var(--tw-font-mono);
  text-shadow:1px 1px 0 rgba(0,0,0,0.3);
}
.rv-badge,.vd-badge,.vs-ident-tag{border-radius:3px}
.vo-bar{height:10px;border-radius:0;background:#ddd2b4;border:1px solid #c9bc97}
.vo-tag{border-radius:3px}
.vo-check{border-radius:3px;box-shadow:2px 2px 0 rgba(0,0,0,0.35)}
.vs-stats{border:2px solid #a3864f;border-radius:3px;background:rgba(63,157,63,0.08)}
.vs-stat+.vs-stat{border-left:2px solid #a3864f}
.vs-num{font-family:var(--tw-font-mono)}
.vd-desc{border-left:4px solid #2b7fd0;border-radius:0;background:rgba(43,127,208,0.08)}
.vo-pct{color:rgba(43,36,24,0.6)}

/* — help：绿色横条分节标题（对应论坛绿色导航条） — */
.head.rule{border-bottom:2px solid #a3864f;padding-bottom:10px}
.sec-title{
  border-radius:3px;letter-spacing:1px;
  background:#3f9d3f;color:#fffdf2;
  border-left:4px solid #2f8a2f;
  text-shadow:1px 1px 0 rgba(0,0,0,0.25);
}
.sec-private .sec-title{
  background:#3f9d3f;border-left-color:#2f8a2f;color:#fffdf2;
}
.sec-private .row{background:rgba(63,157,63,0.08);border-radius:3px}
code{
  border-radius:3px;border:2px solid #b39a63;
  background:#efe6c8;color:#4a3520;
}
.ch{border-radius:3px}
.tip,.empty{color:rgba(43,36,24,0.6)}
.row.muted{color:rgba(43,36,24,0.5)}
`,
  },
}

export default theme
