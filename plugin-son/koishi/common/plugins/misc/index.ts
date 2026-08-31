import { Context, Session, h } from 'koishi'
import { renderHtml, helpCard } from '../../utils/render'
import { HELP_SECTIONS } from '../../utils/help-data'

export const name = 'tshock-misc'

// ══════════════════════════════════════════════════════════
//  help 图片缓存：插件加载时异步渲染一次 PNG 存内存，
//  之后每次发送直接复用缓存（零渲染开销）；失败回退现场渲染/文本。
//  指令元数据唯一来源：common/utils/help-data.ts（HELP_SECTIONS），
//  图片卡片与文本兜底都由它生成，编辑只需改那一份。
// ══════════════════════════════════════════════════════════

let helpImageBuf: Buffer | null = null

/** 显示宽度：中文按 2 列（用于对齐） */
function dispWidth(s: string): number {
  return [...s].reduce((n, c) => n + (c.charCodeAt(0) > 255 ? 2 : 1), 0)
}

/** 按显示宽度右侧补空格 */
function padDisp(s: string, w: number): string {
  return s + ' '.repeat(Math.max(0, w - dispWidth(s)))
}

/** 由 HELP_SECTIONS 程序化生成文本兜底（与图片卡片同一数据源） */
function buildHelpText(): string {
  const lines: string[] = ['━━━ 机器人指令 ━━━', '']
  for (const sec of HELP_SECTIONS) {
    lines.push(`【${sec.title}】`)
    const w = Math.max(...sec.items.map(i => dispWidth(i.cmd))) + 2
    for (const it of sec.items) {
      const ch = it.channel === '@' ? '@' : it.channel
      lines.push(`${padDisp(it.cmd, w)}${it.desc}（${ch}）`)
    }
    lines.push('')
  }
  return lines.join('\n').trimEnd()
}

const HELP_TEXT = buildHelpText()

export function apply(ctx: Context) {
  ctx.logger.info('[tshock-misc] 杂项处理器已加载')

  // — 插件加载时预生成 help 图片并缓存（静态内容，渲染一次即可） —
  renderHtml(helpCard(), 2, '.wrap')
    .then(buf => {
      helpImageBuf = buf
      ctx.logger.info('[tshock-misc] help 图片已缓存 (%d KB)', Math.round(buf.length / 1024))
    })
    .catch((err: any) => {
      ctx.logger.warn('[tshock-misc] help 图片预生成失败，将按需渲染:', err.message)
    })

  // — 自动同意好友请求 —
  ctx.on('friend-request', async (session: Session) => {
    ctx.logger.info('[好友请求] 来自:', session.userId)
    try {
      await session.approve()
      ctx.logger.info('[好友请求] 已同意:', session.userId)
    } catch (e: any) {
      ctx.logger.error('[好友请求] 同意失败:', e.message)
    }
  })

  // — help（群聊私聊均生效）：优先发缓存图片，失败回退文本 —
  ctx.on('message', async (session: Session) => {
    if (session.content.trim() !== 'help') return
    try {
      let buf = helpImageBuf
      if (!buf) {
        buf = await renderHtml(helpCard(), 2, '.wrap')
        helpImageBuf = buf
      }
      await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
    } catch (err: any) {
      ctx.logger.error('[tshock-misc] help 图片发送失败，回退文本:', err.message)
      await session.send(HELP_TEXT)
    }
  })
}
