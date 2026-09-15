// ══════════════════════════════════════════════════════════
//  渲染出口（桶文件）
//  - 卡片函数：8 张，全部来自 ./render/cards/*
//  - renderHtml：HTML -> PNG 原语（Playwright 截图）
//  - 主题：由 ./theme 统一管理，入口在插件 apply() 中设置
//  调用方（plugins/group.ts、plugins/misc/index.ts）保持原有导入不变。
// ══════════════════════════════════════════════════════════

import { chromium } from 'playwright'

export { escapeHtml, toNum, frame, componentCss } from './render/frame'
export { playerInfoCard } from './render/cards/info'
export { bossProgressCard } from './render/cards/boss'
export { onlineListCard, multiOnlineCard } from './render/cards/online'
export { voteListCard, voteDetailCard, voteStateCard } from './render/cards/vote'
export { helpCard } from './render/cards/help'

export type { PlayerInfoData } from './render/cards/info'
export type { BossProgressData, BossData, EventData } from './render/cards/boss'
export type { OnlineStatusData, OnlinePlayer, MultiOnlineData, MultiOnlineServer } from './render/cards/online'
export type { VoteRoundData, VoteOptionData, VoteStateData } from './render/cards/vote'

let _browser: import('playwright').Browser | null = null

async function getBrowser(): Promise<import('playwright').Browser> {
  if (_browser?.isConnected()) return _browser
  _browser = await chromium.launch({ args: ['--no-sandbox'] })
  return _browser
}

/** 将完整 HTML 渲染为 PNG 图片 Buffer（selector 决定截图裁剪范围） */
export async function renderHtml(html: string, scale: number = 2, selector: string = 'body'): Promise<Buffer> {
  const browser = await getBrowser()
  const context = await browser.newContext({ deviceScaleFactor: scale })
  const page = await context.newPage()
  try {
    await page.setContent(html, { waitUntil: 'networkidle' })
    const el = page.locator(selector)
    const box = await el.boundingBox()
    if (!box) throw new Error(`Element "${selector}" not found`)
    return await page.screenshot({
      clip: { x: box.x, y: box.y, width: box.width, height: box.height },
      type: 'png',
    })
  } finally {
    await page.close()
    await context.close()
  }
}
