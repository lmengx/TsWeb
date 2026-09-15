// ══════════════════════════════════════════════════════════
//  卡片骨架：唯一 <style> 出口
//  - 变量定义与主题附加样式来自 theme 层（activeStyleBlock）
//  - 组件样式表只引用 var(--tw-*)，不含任何颜色字面值
//  - escapeHtml 是全局唯一转义入口（修复改造前的自反替换缺陷）
//  卡片函数只负责输出片段，不再自带 <style>。
// ══════════════════════════════════════════════════════════

import { activeStyleBlock } from '../theme'

const ENTITY_MAP: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
}

/**
 * HTML 转义（真转义）。
 * 玩家名、服务器名、世界名、用户组、投票标题/说明/选项文本/提案人、事件名
 * 等所有插值都必须经此函数，不可直接拼接。
 */
export function escapeHtml(value: unknown): string {
  return String(value ?? '').replace(/[&<>"']/g, ch => ENTITY_MAP[ch])
}

// ── 组件样式表（全部引用令牌变量） ──
const COMPONENT_CSS = `
*{margin:0;padding:0;box-sizing:border-box}
body{
  font-family:var(--tw-font);
  background:var(--tw-bg);
  min-height:100vh;padding:20px;color:var(--tw-text)
}
.wrap{width:var(--tw-w-list);margin:0 auto}
.wrap.w-info{width:var(--tw-w-info)}
.wrap.w-wide{width:var(--tw-w-wide)}
.wrap.w-help{width:var(--tw-w-help)}
.wrap.col{display:flex;flex-direction:column;gap:var(--tw-gap)}

/* — 卡片容器 — */
.card{
  background:var(--tw-card-bg);
  border:1px solid var(--tw-card-border);
  border-radius:var(--tw-card-radius);
  padding:var(--tw-card-pad);
  box-shadow:var(--tw-card-shadow);
  backdrop-filter:blur(var(--tw-blur));
  -webkit-backdrop-filter:blur(var(--tw-blur));
  position:relative;overflow:hidden
}
.card.glow::before{
  content:'';position:absolute;top:-60%;right:-30%;width:300px;height:300px;
  background:radial-gradient(circle,var(--tw-glow-1),transparent 70%);pointer-events:none
}
.card.glow::after{
  content:'';position:absolute;bottom:-40%;left:-20%;width:250px;height:250px;
  background:radial-gradient(circle,var(--tw-glow-2),transparent 70%);pointer-events:none
}

/* — 标题区 — */
.head{display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:18px;position:relative;z-index:1}
.head.rule{padding-bottom:12px;border-bottom:2px solid var(--tw-info)}
.head-title,.title{font-size:22px;font-weight:700;color:var(--tw-text);letter-spacing:0.5px}
.head-sub,.subtitle{color:var(--tw-text-muted);font-size:13px;margin-top:2px;letter-spacing:1px}

/* — 玩家信息卡 — */
.info-grid{display:grid;grid-template-columns:1fr 1fr;gap:0;position:relative;z-index:1}
.info-item{padding:12px 0;border-bottom:1px solid var(--tw-divider)}
.info-item:nth-last-child(-n+2){border-bottom:none}
.label{font-size:13px;color:var(--tw-text-subtle);text-transform:uppercase;letter-spacing:0.8px;margin-bottom:5px}
.value{font-size:17px;color:var(--tw-text-body);font-weight:500}
.value.accent{color:var(--tw-accent)}
.value.gold{color:var(--tw-warning)}
.value.green{color:var(--tw-success)}
.value.blue{color:var(--tw-info)}
.full-row{grid-column:1/-1;padding:12px 0;border-bottom:1px solid var(--tw-divider)}
.footer{
  margin-top:14px;padding-top:12px;border-top:1px solid var(--tw-divider);
  display:flex;justify-content:space-between;align-items:center;position:relative;z-index:1
}
.footer-qq{color:var(--tw-text-subtle);font-size:14px;font-family:var(--tw-font-mono)}
.footer-badge{
  background:var(--tw-accent-soft);color:var(--tw-accent);font-size:12px;
  padding:5px 14px;border-radius:20px;border:1px solid var(--tw-accent-border);letter-spacing:1px
}

/* — Boss 进度卡 — */
.server-head{text-align:center;margin-bottom:10px}
.server-name{
  display:inline-block;font-size:14px;font-weight:700;color:var(--tw-text);
  background:var(--tw-accent-soft);border:1px solid var(--tw-accent-border);
  padding:4px 18px;border-radius:20px;letter-spacing:1px
}
.section{margin-bottom:24px}
.section-head{display:flex;justify-content:space-between;align-items:center;margin-bottom:10px}
.section-head h3{font-size:18px;font-weight:700;color:var(--tw-text)}
.pct{padding:4px 14px;border-radius:20px;font-size:13px;font-weight:600;color:var(--tw-on-accent)}
.pct.green{background:var(--tw-grad-success)}
.pct.purple{background:var(--tw-grad-accent)}
.bar{height:6px;background:var(--tw-bar-track);border-radius:3px;overflow:hidden;margin-bottom:16px}
.bar-inner{height:100%;border-radius:3px}
.bar-inner.green{background:var(--tw-grad-success)}
.bar-inner.purple{background:var(--tw-grad-accent)}
.grid{display:grid;grid-template-columns:repeat(6,1fr);gap:8px}
.bc{
  background:var(--tw-tile-bg);border-radius:var(--tw-tile-radius);
  border:1px solid var(--tw-tile-border);overflow:hidden;text-align:center
}
.bc.done{border-color:var(--tw-tile-border-done)}
.bc-img{
  position:relative;height:80px;background:var(--tw-img-bg);
  display:flex;align-items:center;justify-content:center
}
.bc-img img{width:70%;height:70%;object-fit:contain;filter:var(--tw-img-filter)}
.bc-placeholder{
  width:60%;height:60%;display:flex;align-items:center;justify-content:center;
  background:var(--tw-neutral-soft);border-radius:8px;color:var(--tw-text-subtle);font-size:28px
}
.bc-badge{
  position:absolute;top:4px;right:4px;width:22px;height:22px;border-radius:50%;
  display:flex;align-items:center;justify-content:center;
  font-size:12px;font-weight:700;color:var(--tw-on-accent);box-shadow:0 1px 4px rgba(0,0,0,0.4)
}
.bc-ok{background:var(--tw-grad-success)}
.bc-no{background:var(--tw-grad-danger)}
.bc-name{padding:6px 4px;font-size:12px;font-weight:600;color:var(--tw-text-body)}
.bc-count{font-size:10px;color:var(--tw-text-subtle);padding-bottom:6px}

/* — 在线列表卡 — */
.online-pill{
  display:flex;align-items:center;gap:8px;background:var(--tw-tile-bg);
  border:1px solid var(--tw-tile-border);padding:8px 14px;border-radius:20px
}
.online-dot{width:10px;height:10px;border-radius:50%}
.online-num{font-size:17px;font-weight:700;color:var(--tw-text)}
.online-total{font-size:12px;color:var(--tw-text-muted)}
.occ-bar{height:6px;background:var(--tw-bar-track);border-radius:3px;overflow:hidden;margin-bottom:20px}
.occ-inner{height:100%;border-radius:3px}
.list{display:flex;flex-wrap:wrap;gap:6px}
.chip{
  background:var(--tw-chip-bg);border:1px solid var(--tw-chip-border);border-radius:20px;
  padding:4px 12px;font-size:12px;font-weight:600;color:var(--tw-chip-text);white-space:nowrap
}
.empty{text-align:center;color:var(--tw-text-muted);font-size:15px;padding:28px 0}
.foot{
  margin-top:18px;padding-top:14px;border-top:1px solid var(--tw-divider);
  display:flex;justify-content:space-between;align-items:center
}
.foot-name{color:var(--tw-text-subtle);font-size:12px}
.foot-tag{color:var(--tw-text-subtle);font-size:12px;font-family:var(--tw-font-mono)}

/* — 多服在线卡 — */
.sv{
  background:var(--tw-tile-bg);border:1px solid var(--tw-tile-border);
  border-radius:var(--tw-tile-radius-lg);padding:16px 18px
}
.sv-head{display:flex;justify-content:space-between;align-items:center;margin-bottom:10px}
.sv-name{font-size:16px;font-weight:700;color:var(--tw-text)}
.sv-count{
  font-size:14px;font-weight:700;color:var(--tw-text);
  background:var(--tw-tile-bg);border:1px solid var(--tw-tile-border);
  padding:4px 10px;border-radius:14px
}
.sv-list{display:flex;flex-wrap:wrap;gap:6px}
.row{display:flex;align-items:center;gap:10px;padding:5px 0 5px 10px}
.row.muted{color:var(--tw-text-subtle);font-weight:400}
.only-badge{
  width:100%;display:flex;align-items:baseline;justify-content:center;gap:6px;
  background:var(--tw-badge-bg);border:1px solid var(--tw-badge-border);
  border-radius:12px;padding:10px 0
}
.ob-num{font-size:24px;font-weight:800;color:var(--tw-info)}
.ob-txt{font-size:13px;color:var(--tw-text-body)}

/* — 投票列表卡 — */
.rv{
  background:var(--tw-tile-bg);border:1px solid var(--tw-tile-border);
  border-radius:var(--tw-tile-radius-lg);padding:14px 16px;
  display:flex;gap:12px;align-items:flex-start
}
.rv-num{
  flex-shrink:0;width:26px;height:26px;border-radius:50%;
  background:var(--tw-info-soft);border:1px solid var(--tw-info-border);color:var(--tw-info);
  font-size:13px;font-weight:700;display:flex;align-items:center;justify-content:center
}
.rv-main{flex:1;min-width:0}
.rv-title{
  font-size:15px;font-weight:700;color:var(--tw-text);
  display:flex;align-items:center;gap:8px;flex-wrap:wrap
}
.rv-badge{font-size:11px;font-weight:700;padding:2px 10px;border-radius:20px}
.rv-badge.open{background:var(--tw-success-soft);color:var(--tw-success);border:1px solid var(--tw-success-border)}
.rv-badge.closed{background:var(--tw-danger-soft);color:var(--tw-danger);border:1px solid var(--tw-danger-border)}
.rv-meta{font-size:12px;color:var(--tw-text-muted);margin-top:5px}
.tip{text-align:center;color:var(--tw-text-subtle);font-size:12px;padding:6px 0 2px}

/* — 投票详情卡 — */
.vd-top{display:flex;align-items:center;gap:10px;margin-bottom:8px}
.vd-badge{font-size:11px;font-weight:700;padding:3px 12px;border-radius:20px;letter-spacing:0.5px}
.vd-badge.open{background:var(--tw-success-soft);color:var(--tw-success);border:1px solid var(--tw-success-border)}
.vd-badge.closed{background:var(--tw-danger-soft);color:var(--tw-danger);border:1px solid var(--tw-danger-border)}
.vd-title{font-size:20px;font-weight:700;color:var(--tw-text);line-height:1.4;word-break:break-word}
.vd-desc{
  margin-top:8px;padding:10px 12px;background:var(--tw-info-soft);
  border-left:3px solid var(--tw-info);border-radius:0 8px 8px 0;
  color:var(--tw-text-body);font-size:13px;line-height:1.7;white-space:pre-wrap;word-break:break-word
}
.vd-meta{font-size:12px;color:var(--tw-text-muted);margin-top:8px}
.vd-meta b{color:var(--tw-info);font-weight:600}
.vd-rules{
  margin-top:10px;padding-top:10px;border-top:1px solid var(--tw-divider);
  font-size:12px;color:var(--tw-text-muted)
}
.vd-options{margin-top:12px;display:flex;flex-direction:column;gap:14px}
.vo-head{display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.vo-text{font-size:14px;font-weight:600;color:var(--tw-text-body);flex:1;min-width:0;word-break:break-word}
.vo-tag{
  font-size:10px;font-weight:600;padding:2px 8px;border-radius:8px;
  background:var(--tw-accent-soft);color:var(--tw-accent);
  border:1px solid var(--tw-accent-border);white-space:nowrap
}
.vo-tag.anon{background:var(--tw-neutral-soft);color:var(--tw-text-muted);border-color:var(--tw-neutral-border)}
.vo-score{font-size:12px;color:var(--tw-info);font-weight:700;white-space:nowrap}
.vo-bar{margin-top:6px;height:8px;background:var(--tw-bar-track);border-radius:4px;overflow:hidden}
.vo-fill{height:100%;border-radius:4px;background:var(--tw-grad-info)}
.vo-pct{font-size:11px;color:var(--tw-text-subtle);text-align:right;margin-top:2px}

/* — 投票/提案结果卡 — */
.vs-top{display:flex;justify-content:space-between;align-items:center;gap:10px;margin-bottom:10px}
.vs-title{font-size:18px;font-weight:700;color:var(--tw-text)}
.vs-ident-row{display:flex;align-items:center;gap:8px}
.vs-ident{font-size:13px;font-weight:700;color:var(--tw-info)}
.vs-ident-tag{
  font-size:10px;font-weight:700;padding:2px 8px;border-radius:10px;
  background:var(--tw-success-soft);color:var(--tw-success);border:1px solid var(--tw-success-border)
}
.vs-ident-tag.unbound{background:var(--tw-warning-soft);color:var(--tw-warning);border-color:var(--tw-warning-border)}
.vs-banner{
  margin:10px 0 12px;padding:10px 12px;border-radius:8px;
  background:var(--tw-success-soft);border:1px solid var(--tw-success-border);
  color:var(--tw-success);font-size:13px;font-weight:600;word-break:break-word
}
.vs-stats{
  display:flex;margin-bottom:14px;background:var(--tw-info-soft);
  border:1px solid var(--tw-info-border);border-radius:12px;padding:10px 0
}
.vs-stat{flex:1;text-align:center}
.vs-stat+.vs-stat{border-left:1px solid var(--tw-info-border)}
.vs-num{font-size:20px;font-weight:800;color:var(--tw-info);line-height:1.1}
.vs-label{font-size:11px;color:var(--tw-text-muted);margin-top:2px}
.vo.voted .vo-text{color:var(--tw-success)}
.vo.voted .vo-fill{background:var(--tw-grad-success)}
.vo-check{
  flex-shrink:0;width:20px;height:20px;border-radius:50%;
  background:var(--tw-grad-success);color:var(--tw-on-accent);
  font-size:12px;font-weight:800;display:flex;align-items:center;justify-content:center;
  box-shadow:0 2px 6px rgba(0,0,0,0.4)
}
.vs-rules{
  margin-top:12px;padding-top:10px;border-top:1px solid var(--tw-divider);
  font-size:12px;color:var(--tw-text-muted)
}
.vs-rules b{color:var(--tw-info);font-weight:600}

/* — 指令卡片（help） — */
.sec{margin-bottom:14px}
.sec-title{
  font-size:13px;font-weight:800;color:var(--tw-info);
  padding:4px 0 4px 10px;margin-bottom:4px;
  border-left:3px solid var(--tw-info);background:var(--tw-info-soft);border-radius:0 6px 6px 0
}
.sec-private .sec-title{color:var(--tw-accent);border-left-color:var(--tw-accent);background:var(--tw-accent-soft)}
.sec-private .row{background:var(--tw-accent-soft);border-radius:6px}
code{
  font-family:var(--tw-font-mono);font-size:13px;font-weight:700;color:var(--tw-info);
  background:var(--tw-info-soft);border:1px solid var(--tw-info-border);
  padding:1px 8px;border-radius:6px;white-space:nowrap
}
.desc{flex:1;font-size:12px;color:var(--tw-text-muted);min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.ch{flex-shrink:0;font-size:10px;font-weight:700;padding:1px 8px;border-radius:10px;white-space:nowrap}
.ch-group{background:var(--tw-info-soft);color:var(--tw-info);border:1px solid var(--tw-info-border)}
.ch-private{background:var(--tw-accent-soft);color:var(--tw-accent);border:1px solid var(--tw-accent-border)}
.ch-at{background:var(--tw-success-soft);color:var(--tw-success);border:1px solid var(--tw-success-border)}
`

export interface FrameOptions {
  /** 附加在 .wrap 上的类（w-info / w-wide / w-help / col / 自定义） */
  wrapClass?: string
}

/** 用当前主题包裹卡片片段，输出完整 HTML 文档 */
export function frame(bodyHtml: string, options: FrameOptions = {}): string {
  const cls = options.wrapClass ? ` ${options.wrapClass}` : ''
  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>
${activeStyleBlock()}
${COMPONENT_CSS}
</style>
</head>
<body>
<div class="wrap${cls}">${bodyHtml}</div>
</body>
</html>`
}

/** 供校验脚本读取组件样式表，确认不含颜色字面值 */
export function componentCss(): string {
  return COMPONENT_CSS
}
