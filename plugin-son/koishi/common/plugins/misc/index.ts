import { Context, Session, h } from 'koishi'
import { existsSync, readFileSync, writeFileSync, mkdirSync, readdirSync, rmSync } from 'fs'
import { join, dirname } from 'path'
import { createHash } from 'crypto'
import { renderHtml, helpCard } from '../../utils/render'
import { HELP_SECTIONS } from '../../utils/help-data'

export const name = 'tshock-misc'

// ══════════════════════════════════════════════════════════
//  help 图片缓存（内存 + 磁盘双层）
//  1) 加载时优先读磁盘缓存：数据未变（hash 相同）→ 直接复用，零渲染
//  2) 无缓存才渲染一次并写盘（15s 超时防卡死），失败自动重试 5 次
//  3) 发送 help：内存 → 磁盘 → 现场渲染（最后兜底）。
//     正常路径 = 读内存 buffer + base64，无 playwright 参与，毫秒级
//  元数据唯一来源：common/utils/help-data.ts（HELP_SECTIONS），
//  内容变更后 hash 变化 → 自动渲染新图并清理旧缓存文件
// ══════════════════════════════════════════════════════════

let helpImageBuf: Buffer | null = null
let helpPending: Promise<Buffer> | null = null   // 并发渲染去重（预热与 help 请求共享）

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

/** help 数据指纹：元数据变更 → hash 变化 → 重新渲染新图 */
const helpHash = createHash('md5').update(JSON.stringify(HELP_SECTIONS)).digest('hex').slice(0, 12)

function helpCacheFile(baseDir: string): string {
  return join(baseDir, 'data', `tsweb-help-${helpHash}.png`)
}

/** 清理旧版本的 help 缓存文件（保留当前 hash） */
function cleanupOldCaches(baseDir: string): void {
  try {
    const dir = join(baseDir, 'data')
    if (!existsSync(dir)) return
    const current = `tsweb-help-${helpHash}.png`
    for (const f of readdirSync(dir)) {
      if (f.startsWith('tsweb-help-') && f !== current) {
        try { rmSync(join(dir, f)) } catch { /* 忽略 */ }
      }
    }
  } catch { /* 忽略 */ }
}

/**
 * 确保 help 图片可用（返回内存 buffer）：
 *   内存 → 磁盘 → 渲染+写盘；并发调用共享同一渲染 Promise
 */
async function ensureHelpImage(baseDir: string): Promise<Buffer> {
  if (helpImageBuf) return helpImageBuf
  if (helpPending) return helpPending

  helpPending = (async () => {
    // ① 磁盘缓存命中（跨重启直接复用，零渲染）
    const file = helpCacheFile(baseDir)
    try {
      if (existsSync(file)) {
        const buf = readFileSync(file)
        helpImageBuf = buf
        return buf
      }
    } catch { /* 读失败继续渲染 */ }

    // ② 渲染（带超时，防 playwright 卡死）
    const buf = await Promise.race([
      renderHtml(helpCard(), 2, '.wrap'),
      new Promise<Buffer>((_, rej) => setTimeout(() => rej(new Error('help 渲染超时(15s)')), 15000)),
    ])
    helpImageBuf = buf
    // ③ 写盘（失败不影响本次发送）
    try {
      mkdirSync(dirname(file), { recursive: true })
      writeFileSync(file, buf)
    } catch { /* 忽略 */ }
    return buf
  })()

  try {
    return await helpPending
  } finally {
    helpPending = null
  }
}

export function apply(ctx: Context) {
  ctx.logger.info('[tshock-misc] 杂项处理器已加载')
  cleanupOldCaches(ctx.baseDir)

  // — 加载时预热：磁盘命中即秒就绪；渲染失败自动重试（最多 5 次，间隔 3s） —
  const warm = async (attempt = 1) => {
    try {
      const buf = await ensureHelpImage(ctx.baseDir)
      ctx.logger.info('[tshock-misc] help 图片就绪 (%d KB, 源=%s)', Math.round(buf.length / 1024), helpImageBuf === buf ? 'disk/mem' : 'render')
    } catch (err: any) {
      ctx.logger.warn('[tshock-misc] help 图片生成失败 (%d/5): %s', attempt, err.message)
      if (attempt < 5) setTimeout(() => warm(attempt + 1), 3000)
    }
  }
  warm()

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

  // — help（群聊私聊均生效）：内存 → 磁盘 → 现场渲染，兜底文本 —
  ctx.on('message', async (session: Session) => {
    if (session.content.trim() !== 'help') return
    try {
      const buf = await ensureHelpImage(ctx.baseDir)
      await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
    } catch (err: any) {
      ctx.logger.error('[tshock-misc] help 图片不可用，回退文本:', err.message)
      await session.send(HELP_TEXT)
    }
  })
}
