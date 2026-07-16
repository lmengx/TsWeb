import { Context, Session, h } from 'koishi'
import type { Config } from '../utils/config'
import { CONFIG_DEFAULTS } from '../utils/config'
import MarkdownIt from 'markdown-it'
import { chromium } from 'playwright'

export const name = 'dify-group'
export const reusable = true

// ══════════════════════════════════════════════════════════
//  全局共享（浏览器单例、Markdown 解析器）
// ══════════════════════════════════════════════════════════

const _md = new MarkdownIt({ html: false, breaks: true, linkify: true })
let _browser: import('playwright').Browser | null = null

async function getBrowser(): Promise<import('playwright').Browser> {
  if (_browser?.isConnected()) return _browser
  _browser = await chromium.launch({ args: ['--no-sandbox'] })
  return _browser
}

/** 生成带样式的完整 HTML */
function buildHtml(mdHtml: string): string {
  const bg = '#ffffff'
  const text = '#1a1a2e'
  const codeBg = '#1e1e2e'
  const codeText = '#cdd6f4'
  const border = '#e0e0e0'
  const accent = '#7c3aed'
  return `<!DOCTYPE html><html><head><meta charset="utf-8"><style>
*{margin:0;padding:0;box-sizing:border-box}
body{
  font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif;
  font-size:16px;line-height:1.7;color:${text};background:${bg};
  padding:28px 32px;width:720px;word-wrap:break-word
}
h1{font-size:22px;font-weight:700;margin:18px 0 10px;color:${accent};border-bottom:2px solid ${accent};padding-bottom:4px}
h2{font-size:19px;font-weight:700;margin:16px 0 8px;color:#333}
h3{font-size:17px;font-weight:600;margin:14px 0 6px}
p{margin:6px 0}
ul,ol{margin:6px 0;padding-left:24px}
li{margin:3px 0}
code{
  font-family:"JetBrains Mono","Fira Code","Consolas",monospace;
  font-size:14px;background:${codeBg};color:${codeText};
  padding:2px 6px;border-radius:4px
}
pre{background:${codeBg};color:${codeText};padding:14px 16px;border-radius:8px;overflow-x:auto;margin:10px 0}
pre code{background:none;color:inherit;padding:0;font-size:14px;line-height:1.5}
blockquote{border-left:4px solid ${accent};padding:6px 14px;margin:10px 0;background:#f5f3ff;color:#555}
table{border-collapse:collapse;width:100%;margin:10px 0;font-size:14px}
th,td{border:1px solid ${border};padding:8px 12px;text-align:left}
th{background:#f5f3ff;font-weight:600}
tr:nth-child(even){background:#fafafa}
a{color:${accent};text-decoration:none}
img{max-width:100%;border-radius:6px;margin:8px 0}
hr{border:none;border-top:2px solid ${border};margin:16px 0}
strong{font-weight:700}
</style></head><body>${mdHtml}</body></html>`
}

async function renderToImage(answer: string): Promise<Buffer> {
  const mdHtml = _md.render(answer)
  const fullHtml = buildHtml(mdHtml)

  const browser = await getBrowser()
  const page = await browser.newPage()
  try {
    await page.setContent(fullHtml, { waitUntil: 'networkidle' })
    const box = await page.locator('body').boundingBox()
    // 放大视口以覆盖全部内容，防止超出初始视口(720px)的像素未被渲染合成
    await page.setViewportSize({ width: Math.ceil(box!.width), height: Math.ceil(box!.height) + 50 })
    // 重新获取稳定后的 bounding box
    const finalBox = await page.locator('body').boundingBox()
    const buf = await page.screenshot({
      clip: { x: 0, y: 0, width: finalBox!.width, height: finalBox!.height },
      type: 'png',
    })
    return buf
  } finally {
    await page.close()
  }
}

// ══════════════════════════════════════════════════════════
//  插件入口（可多实例）
// ══════════════════════════════════════════════════════════

export function apply(ctx: Context, config: Config) {
  // ── 实例级上下文存储 ──
  const contexts = new Map<string, { entries: { role: 'user' | 'assistant'; content: string }[]; lastActive: number; conversationId: string }>()

  function getContextKey(guildId: string, userId: string): string {
    return `${guildId}_${userId}`
  }

  function pushContext(key: string, userMsg: string, botMsg: string, maxSize: number) {
    let c = contexts.get(key)
    if (!c) {
      c = { entries: [], lastActive: Date.now(), conversationId: '' }
      contexts.set(key, c)
    }
    c.lastActive = Date.now()
    c.entries.push({ role: 'user', content: userMsg })
    c.entries.push({ role: 'assistant', content: botMsg })
    while (c.entries.length > maxSize * 2) c.entries.splice(0, 2)
  }

  function touchContext(key: string, resetTime: number) {
    const c = contexts.get(key)
    if (!c) return null
    if (Date.now() - c.lastActive > resetTime * 1000) { contexts.delete(key); return null }
    c.lastActive = Date.now()
    return c
  }

  // ── 清理定时器 ──
  const timer = setInterval(() => {
    const now = Date.now()
    const threshold = config.上下文重置时间 * 1000
    let cleaned = 0
    for (const [key, data] of contexts) {
      if (now - data.lastActive > threshold) { contexts.delete(key); cleaned++ }
    }
    if (cleaned > 0) ctx.logger.info(`[DifyChat] 清理了 ${cleaned} 个过期上下文`)
  }, CONFIG_DEFAULTS.清理间隔 * 1000)
  ctx.on('dispose', () => clearInterval(timer))

  // ── Dify API 调用 ──
  async function callDify(
    query: string,
    user: string,
    conversationId: string,
  ): Promise<{ answer: string; conversationId: string } | null> {
    const baseUrl = config.目标地址.replace(/\/+$/, '')
    const body = JSON.stringify({
      inputs: {},
      query,
      response_mode: 'streaming',
      user,
      conversation_id: conversationId || '',
      auto_generate_name: false,
    })

    try {
      const res = await fetch(`${baseUrl}/chat-messages`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${config.接口密钥}`, 'Content-Type': 'application/json' },
        body,
        signal: AbortSignal.timeout(config.请求超时 * 1000),
      })
      if (!res.ok) { ctx.logger.error(`[DifyChat] HTTP ${res.status}: ${await res.text().catch(() => '')}`); return null }

      const reader = res.body!.getReader()
      const decoder = new TextDecoder()
      let buffer = '', answer = '', newConversationId = conversationId || ''

      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() || ''
        for (const line of lines) {
          const t = line.trim()
          if (!t.startsWith('data: ')) continue
          try {
            const ev = JSON.parse(t.slice(6))
            if (ev.event === 'agent_message') answer += ev.answer || ''
            else if (ev.event === 'message_end' && ev.conversation_id) newConversationId = ev.conversation_id
            else if (ev.event === 'error') { ctx.logger.error(`[DifyChat] 流错误: ${ev.message}`); return null }
          } catch { /* skip malformed */ }
        }
      }
      return answer ? { answer, conversationId: newConversationId } : null
    } catch (err: any) {
      ctx.logger.error(`[DifyChat] 请求异常: ${err.message || err}`)
      return null
    }
  }

  // ── 消息监听 ──
  const groupSet = new Set(config.生效群列表)
  ctx.logger.info(`[DifyChat] 载入完成，生效群数: ${groupSet.size}`)
  ctx.logger.info(`[DifyChat] 生效群号: ${JSON.stringify(config.生效群列表)}`)

  ctx.on('message', async (session: Session) => {
    if (!session.guildId) return
    if (!groupSet.has(Number(session.guildId))) return

    const selfId = session.bot.selfId
    const isAtBot = h.select(session.elements, 'at').some((el: any) => String(el.attrs.id) === String(selfId))
    if (!isAtBot) return

    const userMsg = h.select(session.elements, 'text').map((el: any) => el.attrs.content).join('').trim()
    if (!userMsg) return

    ctx.logger.info(`[DifyChat] @消息: userId=${session.userId}, "${userMsg.slice(0, 50)}"`)

    const contextKey = getContextKey(session.guildId, session.userId)
    const existingCtx = touchContext(contextKey, config.上下文重置时间)
    const convId = existingCtx?.conversationId ?? ''

    const result = await callDify(userMsg, session.userId, convId)
    if (!result) {
      await session.send(h('at', { id: session.userId }) + ' 抱歉，AI 暂时无法回复')
      return
    }

    const { answer, conversationId: newConvId } = result
    ctx.logger.info(`[DifyChat] 返回 ${answer.length} 字, convId=${newConvId}`)

    // 存入上下文
    pushContext(contextKey, userMsg, answer, config.最大上下文大小)
    const updated = contexts.get(contextKey)
    if (updated && newConvId) updated.conversationId = newConvId

    // ── Markdown → 图片发送 ──
    try {
      const buf = await renderToImage(answer)
      await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
    } catch (err: any) {
      ctx.logger.error(`[DifyChat] 截图失败: ${err.message}，降级为纯文本`)
      await session.send(h('at', { id: session.userId }) + ' ' + answer)
    }
  })
}
