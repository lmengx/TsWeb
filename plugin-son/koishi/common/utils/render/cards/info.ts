import { escapeHtml, footerBadgeText, frame, toNum } from '../frame'

export interface PlayerInfoData {
  player: string
  qq: string
  group: string
  registered: string
  online_minutes: number
  deaths: number
  fishing_quests: number
}

/** 玩家信息卡片（调用方截图选择器：.card） */
export function playerInfoCard(data: PlayerInfoData): string {
  // 时长来自后端 JSON：先数值化，非有限数回退 0（否则会渲染出 NaN小时）
  const minutes = toNum(data.online_minutes)
  const hours = Math.floor(minutes / 60)
  const mins = minutes % 60
  const onlineStr = hours > 0 ? `${hours}小时${mins}分钟` : `${mins}分钟`

  return frame(`
<div class="card glow">
  <div class="head">
    <div>
      <div class="head-title">玩家信息</div>
      <div class="head-sub">Player Profile</div>
    </div>
  </div>
  <div class="info-grid">
    <div class="info-item">
      <div class="label">角色名</div>
      <div class="value accent">${escapeHtml(data.player)}</div>
    </div>
    <div class="info-item">
      <div class="label">用户组</div>
      <div class="value blue">${escapeHtml(data.group)}</div>
    </div>
    <div class="info-item">
      <div class="label">在线时长</div>
      <div class="value green">${escapeHtml(onlineStr)}</div>
    </div>
    <div class="info-item">
      <div class="label">死亡次数</div>
      <div class="value gold">${toNum(data.deaths)} 次</div>
    </div>
    <div class="info-item full-row">
      <div class="label">钓鱼任务</div>
      <div class="value">${toNum(data.fishing_quests)} 次</div>
    </div>
    <div class="info-item full-row">
      <div class="label">注册时间</div>
      <div class="value">${escapeHtml(data.registered)}</div>
    </div>
  </div>
  <div class="footer">
    <span class="footer-qq">QQ ${escapeHtml(data.qq)}</span>
    <span class="footer-badge">${escapeHtml(footerBadgeText('TSHOCK'))}</span>
  </div>
</div>`, { wrapClass: 'w-info' })
}
