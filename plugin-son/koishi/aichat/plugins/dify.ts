import { Context, Session, h } from 'koishi'
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'fs'
import { dirname } from 'path'
import type { Config } from '../utils/config'
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
    await page.setViewportSize({ width: Math.ceil(box!.width), height: Math.ceil(box!.height) + 50 })
    const finalBox = await page.locator('body').boundingBox()
    return await page.screenshot({
      clip: { x: 0, y: 0, width: finalBox!.width, height: finalBox!.height },
      type: 'png',
    })
  } finally {
    await page.close()
  }
}

// ══════════════════════════════════════════════════════════
//  本地持久化：QQ号 → Dify conversation_id
// ══════════════════════════════════════════════════════════

interface SessionStore {
  [key: string]: {
    /** Dify 会话 ID */
    conversationId: string
    /** 最后活跃时间戳 */
    lastActive: number
  }
}

let store: SessionStore = {}
let storePath = ''

function loadStore(path: string) {
  storePath = path
  try {
    const dir = dirname(path)
    if (!existsSync(dir)) mkdirSync(dir, { recursive: true })
    if (existsSync(path)) {
      store = JSON.parse(readFileSync(path, 'utf-8'))
    }
  } catch { store = {} }
}

function saveStore() {
  try { writeFileSync(storePath, JSON.stringify(store, null, 2), 'utf-8') } catch { /* skip */ }
}

// ══════════════════════════════════════════════════════════
//  Dify API
// ══════════════════════════════════════════════════════════

/** Dify 端返回的会话信息 */
interface DifyConversation {
  id: string
  name: string
  created_at: number
  updated_at: number
}

async function listConversations(apiBase: string, apiKey: string, user: string): Promise<DifyConversation[]> {
  const url = `${apiBase}/conversations?user=${encodeURIComponent(user)}&limit=20`
  const res = await fetch(url, {
    headers: { Authorization: `Bearer ${apiKey}` },
  })
  if (!res.ok) return []
  const json = await res.json()
  return json.data || []
}

async function callDify(
  apiBase: string,
  apiKey: string,
  timeout: number,
  query: string,
  user: string,
  conversationId: string,
  logger: Context['logger'],
): Promise<{ answer: string; conversationId: string } | null> {
  const body = JSON.stringify({
    inputs: {},
    query,
    response_mode: 'streaming',
    user,
    conversation_id: conversationId || '',
    files: [],
  })

  try {
    const res = await fetch(`${apiBase}/chat-messages`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${apiKey}`,
        'Content-Type': 'application/json',
      },
      body,
      signal: AbortSignal.timeout(timeout * 1000),
    })

    if (!res.ok) {
      logger.error(`[DifyChat] HTTP ${res.status}: ${await res.text().catch(() => '')}`)
      return null
    }

    const reader = res.body!.getReader()
    const decoder = new TextDecoder()
    let buffer = ''
    let answer = ''
    let newConversationId = conversationId || ''

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
          if (ev.event === 'agent_message') {
            answer += ev.answer || ''
          } else if (ev.event === 'message_end') {
            if (ev.conversation_id) newConversationId = ev.conversation_id
          } else if (ev.event === 'error') {
            logger.error(`[DifyChat] 流错误: ${ev.message}`)
            return null
          }
        } catch { /* skip malformed */ }
      }
    }

    return answer ? { answer, conversationId: newConversationId } : null
  } catch (err: any) {
    logger.error(`[DifyChat] 请求异常: ${err.message || err}`)
    return null
  }
}

// ══════════════════════════════════════════════════════════
//  插件入口
// ══════════════════════════════════════════════════════════

export function apply(ctx: Context, config: Config) {
  const apiBase = config.目标地址.replace(/\/+$/, '')    // 不带末尾斜杠，期望已含 /v1
  const apiKey = config.接口密钥
  const resetTime = config.上下文重置时间 * 1000
  const logger = ctx.logger

  // 持久化文件
  loadStore(`${ctx.baseDir}/data/dify-sessions.json`)

  // 生效群组
  const groupSet = new Set(config.生效群列表)
  logger.info(`[DifyChat] 载入完成, API: ${apiBase}, 生效群: ${JSON.stringify([...groupSet])}`)

  ctx.on('message', async (session: Session) => {
    if (!session.guildId) return
    if (!groupSet.has(Number(session.guildId))) return

    // 必须是 @ 机器人
    const selfId = session.bot.selfId
    const isAtBot = h.select(session.elements, 'at')
      .some((el: any) => String(el.attrs.id) === String(selfId))
    if (!isAtBot) return

    const rawText = h.select(session.elements, 'text')
      .map((el: any) => el.attrs.content).join('').trim()

    // ════════════════════════════════════════════════════
    //  命令
    // ════════════════════════════════════════════════════

    // /new — 开始新会话
    if (rawText === '/new') {
      delete store[session.userId]
      saveStore()
      await session.send(h('at', { id: session.userId }) + ' 已开始新会话')
      return
    }

    // /list — 列出 Dify 端所有会话
    if (rawText === '/list') {
      const list = await listConversations(apiBase, apiKey, session.userId)
      if (!list.length) {
        await session.send('暂无历史会话')
        return
      }
      const current = store[session.userId]?.conversationId
      const lines = list.map((c, i) => {
        const marker = c.id === current ? ' *' : ''
        const name = c.name || '未命名'
        const date = new Date(c.updated_at * 1000).toLocaleString('zh-CN')
        return `${i + 1}.${marker} ${name}  ${date}  \`${c.id.slice(0, 8)}\``
      })
      await session.send(lines.join('\n'))
      return
    }

    // /switch <序号|名称|ID前缀> — 切换会话
    if (rawText.startsWith('/switch ')) {
      const target = rawText.slice(8).trim()
      const list = await listConversations(apiBase, apiKey, session.userId)
      let found: DifyConversation | undefined
      const idx = parseInt(target)
      if (!isNaN(idx) && idx > 0 && idx <= list.length) {
        found = list[idx - 1]
      } else {
        found = list.find(c =>
          c.name === target ||
          c.id === target ||
          c.id.startsWith(target),
        )
      }
      if (!found) {
        await session.send('未找到该会话，使用 /list 查看')
        return
      }
      store[session.userId] = { conversationId: found.id, lastActive: Date.now() }
      saveStore()
      await session.send(`已切换到: ${found.name || found.id.slice(0, 8)}`)
      return
    }

    // /help — 帮助
    if (rawText === '/help') {
      await session.send([
        '**Dify Chat 命令**',
        '`/new` — 开始新会话',
        '`/list` — 列出所有历史会话（* 标记当前）',
        '`/switch <序号|名称>` — 切换到指定会话',
        '直接 @我 发送消息 — 继续当前会话',
      ].join('\n'))
      return
    }

    // ════════════════════════════════════════════════════
    //  正常对话
    // ════════════════════════════════════════════════════

    if (!rawText) return

    logger.info(`[DifyChat] @消息: userId=${session.userId}, "${rawText.slice(0, 50)}"`)

    // 查本地存储，获取 conversationId
    const entry = store[session.userId]
    let convId = ''
    if (entry) {
      // 超时则开新会话
      if (Date.now() - entry.lastActive > resetTime) {
        delete store[session.userId]
        saveStore()
      } else {
        convId = entry.conversationId
      }
    }

    const result = await callDify(apiBase, apiKey, config.请求超时, rawText, session.userId, convId, logger)
    if (!result) {
      await session.send(h('at', { id: session.userId }) + ' 抱歉，AI 暂时无法回复')
      return
    }

    logger.info(`[DifyChat] 返回 ${result.answer.length} 字, convId=${result.conversationId}`)

    // 持久化对话映射
    store[session.userId] = {
      conversationId: result.conversationId,
      lastActive: Date.now(),
    }
    saveStore()

    // 渲染 Markdown → 图片发送
    try {
      const buf = await renderToImage(result.answer)
      await session.send(h('image', { url: `base64://${buf.toString('base64')}` }))
    } catch (err: any) {
      logger.error(`[DifyChat] 截图失败: ${err.message}，降级为纯文本`)
      await session.send(h('at', { id: session.userId }) + ' ' + result.answer)
    }
  })
}
