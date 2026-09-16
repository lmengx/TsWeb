import { escapeHtml, footerBadgeText, frame, toNum } from '../frame'

export interface RegisterSuccessData {
  /** 角色名（后端返回，与请求一致） */
  player: string
  /** QQ 号 */
  qq: string
  /** 后端 message（提示改密文案，可覆盖默认） */
  message?: string
}

export interface BindSuccessData {
  /** 角色名（后端返回，缺省回退请求值） */
  player: string
  /** QQ 号 */
  qq: string
  /** 来源服务器名 */
  server?: string
  /** 绑定即时 UUID 同步结果（非空 = 已开通全服免密） */
  uuidSync?: unknown
  /** 后端 message */
  message?: string
}

const FOOTER = 'TSHOCK'

/** 注册成功卡片（调用方截图选择器：.card） */
export function registerSuccessCard(data: RegisterSuccessData): string {
  const qq = escapeHtml(data.qq)
  const hint = data.message
    ? escapeHtml(data.message)
    : '私聊发送「改密码 密码」设置你的登录密码'

  return frame(`
<div class="card glow">
  <div class="head">
    <div>
      <div class="head-title">注册成功</div>
      <div class="head-sub">REGISTER OK</div>
    </div>
  </div>
  <div class="vs-banner">已创建角色，可在所有服务器登录</div>
  <div class="info-grid">
    <div class="info-item">
      <div class="label">角色名</div>
      <div class="value accent">${escapeHtml(data.player)}</div>
    </div>
    <div class="info-item">
      <div class="label">QQ</div>
      <div class="value blue">${qq}</div>
    </div>
    <div class="info-item full-row">
      <div class="label">下一步</div>
      <div class="value">${hint}</div>
    </div>
  </div>
  <div class="footer">
    <span class="footer-qq">QQ ${qq}</span>
    <span class="footer-badge">${escapeHtml(footerBadgeText(FOOTER))}</span>
  </div>
</div>`, { wrapClass: 'w-info' })
}

/** 绑定成功卡片（调用方截图选择器：.card） */
export function bindSuccessCard(data: BindSuccessData): string {
  const qq = escapeHtml(data.qq)
  // uuidSync 非空 = 绑定即时同步成功 → 全服免密已开通；否则待玩家下次登录触发 UUID 上报
  const uuidReady = data.uuidSync != null
  const uuidLine = uuidReady
    ? '<div class="value green">已开通 · 全服免密登录</div>'
    : '<div class="value">待登录后自动开通</div>'

  return frame(`
<div class="card glow">
  <div class="head">
    <div>
      <div class="head-title">绑定成功</div>
      <div class="head-sub">BIND OK</div>
    </div>
  </div>
  <div class="vs-banner">已绑定角色，可在所有服务器使用该角色登录</div>
  <div class="info-grid">
    <div class="info-item">
      <div class="label">角色名</div>
      <div class="value accent">${escapeHtml(data.player)}</div>
    </div>
    <div class="info-item">
      <div class="label">来源服</div>
      <div class="value blue">${escapeHtml(data.server || '-')}</div>
    </div>
    <div class="info-item full-row">
      <div class="label">QQ</div>
      <div class="value">${qq}</div>
    </div>
    <div class="info-item full-row">
      <div class="label">免密登录</div>
      ${uuidLine}
    </div>
  </div>
  <div class="footer">
    <span class="footer-qq">QQ ${qq}</span>
    <span class="footer-badge">${escapeHtml(footerBadgeText(FOOTER))}</span>
  </div>
</div>`, { wrapClass: 'w-info' })
}
