import { escapeHtml, footerBadgeText, frame, toNum } from '../frame'

export interface LotteryWinner {
  username?: string
  nickname?: string
}

export interface LotteryResultData {
  winner?: LotteryWinner
  /** 本次开奖时的在线人数（奖池规模） */
  count?: number
}

/**
 * 在线抽奖结果卡（调用方截图选择器：.card）
 *
 * 刻意只呈现「谁中了」和「从多少人里抽的」两件事：
 * 奖池名单、指纹、种子、算法等留存在后端台账（data/lottery.json）里备查，
 * 不放进群里那张图 —— 群成员要的是结果，不是审计报告。
 */
export function lotteryResultCard(data: LotteryResultData): string {
  const w = data.winner || {}
  const count = toNum(data.count, 0)
  const nameTag = w.username
    ? `<span class="lt-tag">角色 ${escapeHtml(w.username)}</span>`
    : ''

  return frame(`
<div class="card glow">
  <div class="head">
    <div>
      <div class="head-title">在线抽奖</div>
      <div class="head-sub">ONLINE DRAW</div>
    </div>
  </div>
  <div class="lt-win">
    <div class="lt-crown">中奖</div>
    <div class="lt-wname">${escapeHtml(w.nickname || w.username || '未知')}</div>
    <div class="lt-wtags">${nameTag}</div>
  </div>
  <div class="foot">
    <span class="foot-name">本次在线 ${count} 人</span>
    <span class="foot-tag">${escapeHtml(footerBadgeText('DRAW'))}</span>
  </div>
</div>`)
}
