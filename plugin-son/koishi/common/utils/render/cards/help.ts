import { escapeHtml, frame } from '../frame'
import { HELP_SECTIONS } from '../../help-data'

const CHANNEL_CLS: Record<string, string> = {
  '群聊': 'ch-group',
  '私聊': 'ch-private',
  '@': 'ch-at',
}

/**
 * 机器人指令卡片（调用方截图选择器：.wrap）
 * 元数据唯一来源：common/utils/help-data.ts 的 HELP_SECTIONS。
 * 改造前本卡固定浅色，统一主题后随当前主题变化。
 */
export function helpCard(): string {
  const sectionsHtml = HELP_SECTIONS.map(sec => {
    const rows = sec.items.map(it => {
      const cls = CHANNEL_CLS[it.channel] || 'ch-group'
      return `<div class="row">
        <code>${escapeHtml(it.cmd)}</code>
        <span class="desc">${escapeHtml(it.desc)}</span>
        <span class="ch ${cls}">${escapeHtml(it.channel)}</span>
      </div>`
    }).join('\n')
    return `<div class="sec ${sec.private ? 'sec-private' : ''}">
      <div class="sec-title">${escapeHtml(sec.title)}</div>
      ${rows}
    </div>`
  }).join('\n')

  return frame(`
<div class="card">
  <div class="head rule">
    <div>
      <div class="head-title">机器人指令</div>
      <div class="head-sub">BOT COMMANDS</div>
    </div>
  </div>
  ${sectionsHtml}
  <div class="foot"><span class="foot-name">发送 help 查看本卡片</span></div>
</div>`, { wrapClass: 'w-help' })
}
