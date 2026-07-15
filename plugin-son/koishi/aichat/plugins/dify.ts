import { Context, Session, h } from 'koishi'
import type { Config } from '../utils/config'
import { CONFIG_DEFAULTS } from '../utils/config'

export const name = 'dify-group'

// ══════════════════════════════════════════════════════════
//  上下文存储
// ══════════════════════════════════════════════════════════

interface ContextEntry {
  role: 'user' | 'assistant'
  content: string
}

interface UserContext {
  entries: ContextEntry[]
  lastActive: number
  /** Dify conversation_id，用于服务端保持上下文 */
  conversationId: string
}

const contexts = new Map<string, UserContext>()

// ══════════════════════════════════════════════════════════
//  上下文工具
// ══════════════════════════════════════════════════════════

function getContextKey(guildId: string, userId: string): string {
  return `${guildId}_${userId}`
}

function pushContext(key: string, userMsg: string, botMsg: string, maxSize: number) {
  let ctx = contexts.get(key)
  if (!ctx) {
    ctx = { entries: [], lastActive: Date.now(), conversationId: '' }
    contexts.set(key, ctx)
  }
  ctx.lastActive = Date.now()
  ctx.entries.push({ role: 'user', content: userMsg })
  ctx.entries.push({ role: 'assistant', content: botMsg })

  while (ctx.entries.length > maxSize * 2) {
    ctx.entries.splice(0, 2)
  }
}

function touchContext(key: string, resetTime: number): UserContext | null {
  const ctx = contexts.get(key)
  if (!ctx) return null
  if (Date.now() - ctx.lastActive > resetTime * 1000) {
    contexts.delete(key)
    return null
  }
  ctx.lastActive = Date.now()
  return ctx
}

// ══════════════════════════════════════════════════════════
//  清理定时器
// ══════════════════════════════════════════════════════════

function startCleanup(ctx: Context, resetTime: number) {
  const interval = CONFIG_DEFAULTS.清理间隔 * 1000
  const timer = setInterval(() => {
    const now = Date.now()
    const threshold = resetTime * 1000
    let cleaned = 0
    for (const [key, data] of contexts) {
      if (now - data.lastActive > threshold) {
        contexts.delete(key)
        cleaned++
      }
    }
    if (cleaned > 0) {
      ctx.logger.info(`[DifyChat] 定时清理: 已清除 ${cleaned} 个过期上下文`)
    }
  }, interval)
  ctx.on('dispose', () => clearInterval(timer))
}

// ══════════════════════════════════════════════════════════
//  调用 Dify Agent API（流式，只收集 agent_message）
//  Agent 应用仅支持 streaming 模式
// ══════════════════════════════════════════════════════════

async function callDify(
  ctx: Context,
  config: Config,
  query: string,
  user: string,
  conversationId: string,
): Promise<{ answer: string; conversationId: string } | null> {
  const baseUrl = config.目标地址.replace(/\/+$/, '')
  const url = `${baseUrl}/chat-messages`

  const body = JSON.stringify({
    inputs: {},
    query,
    response_mode: 'streaming',
    user,
    conversation_id: conversationId || '',
    auto_generate_name: false,
  })

  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${config.接口密钥}`,
        'Content-Type': 'application/json',
      },
      body,
      signal: AbortSignal.timeout(CONFIG_DEFAULTS.请求超时 * 1000),
    })

    if (!res.ok) {
      const errBody = await res.text().catch(() => '')
      ctx.logger.error(`[DifyChat] API HTTP ${res.status}: ${errBody}`)
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
        const trimmed = line.trim()
        if (!trimmed.startsWith('data: ')) continue

        try {
          const event = JSON.parse(trimmed.slice(6))
          switch (event.event) {
            case 'agent_message':
              answer += event.answer || ''
              break
            case 'message_end':
              // 从 message_end 获取 conversation_id
              if (event.conversation_id) {
                newConversationId = event.conversation_id
              }
              break
            case 'error':
              ctx.logger.error(`[DifyChat] 流错误: ${event.message || ''}`)
              return null
          }
        } catch {
          // JSON 解析跳过
        }
      }
    }

    return answer ? { answer, conversationId: newConversationId } : null
  } catch (err: any) {
    ctx.logger.error(`[DifyChat] 请求异常: ${err.message || err}`)
    return null
  }
}

// ══════════════════════════════════════════════════════════
//  插件入口
// ══════════════════════════════════════════════════════════

export function apply(ctx: Context, config: Config) {
  const groupSet = new Set(config.生效群列表)
  ctx.logger.info(`[DifyChat] 载入完成，生效群数: ${groupSet.size}`)
  ctx.logger.info(`[DifyChat] 生效群号列表: ${JSON.stringify(config.生效群列表)}`)
  ctx.logger.info(`[DifyChat] 目标地址: ${config.目标地址}`)
  ctx.logger.info(`[DifyChat] 最大上下文: ${config.最大上下文大小} 对, 重置时间: ${config.上下文重置时间}秒`)

  startCleanup(ctx, config.上下文重置时间)

  ctx.on('message', async (session: Session) => {
    // ── 群消息过滤 ──
    if (!session.guildId) {
      ctx.logger.debug('[DifyChat] 非群消息，跳过')
      return
    }

    const gid = Number(session.guildId)
    ctx.logger.debug(`[DifyChat] 收到群消息: guildId=${gid}, userId=${session.userId}, content="${session.content}"`)

    if (!groupSet.has(gid)) {
      ctx.logger.debug(`[DifyChat] 群 ${gid} 不在生效列表 ${JSON.stringify(config.生效群列表)} 中，跳过`)
      return
    }

    // ── 检测 @机器人 ──
    const selfId = session.bot.selfId
    const ats = h.select(session.elements, 'at')
    ctx.logger.debug(`[DifyChat] @元素列表: ${JSON.stringify(ats)}`)
    ctx.logger.debug(`[DifyChat] 机器人自身ID: ${selfId}`)

    const isAtBot = ats.some((el: any) => String(el.attrs.id) === String(selfId))
    ctx.logger.info(`[DifyChat] 被@? ${isAtBot}`)

    if (!isAtBot) {
      ctx.logger.debug('[DifyChat] 未@机器人，跳过')
      return
    }

    // ── 提取纯文本 ──
    const userMsg = h
      .select(session.elements, 'text')
      .map((el: any) => el.attrs.content)
      .join('')
      .trim()

    ctx.logger.info(`[DifyChat] >>> 收到 @消息: userId=${session.userId}, 内容="${userMsg}"`)

    if (!userMsg) {
      ctx.logger.info('[DifyChat] 消息内容为空，回复测试提示')
      await session.send(h('at', { id: session.userId }) + ' 你好，请发送你想要咨询的内容')
      return
    }

    // ── 检查并获取上下文 ──
    const contextKey = getContextKey(session.guildId, session.userId)
    const existingCtx = touchContext(contextKey, config.上下文重置时间)
    const entryCount = existingCtx ? existingCtx.entries.length : 0
    const convId = existingCtx ? existingCtx.conversationId : ''
    const contextSummary = existingCtx ? formatContextSummary(existingCtx.entries) : '无'

    ctx.logger.info(`[DifyChat] 上下文 key=${contextKey}, 条目数=${entryCount}, conversationId=${convId || '(新会话)'}`)

    // ── 调用 Dify ──
    ctx.logger.info(`[DifyChat] 调用 API: query="${userMsg.slice(0, 50)}", convId=${convId || '(新)'}`)
    const result = await callDify(ctx, config, userMsg, session.userId, convId)

    if (!result) {
      ctx.logger.error('[DifyChat] API 返回空')
      await session.send(h('at', { id: session.userId }) + ' Dify API 调用失败，请检查配置和网络')
      return
    }

    const { answer, conversationId: newConvId } = result
    ctx.logger.info(`[DifyChat] API 返回成功, 答案长度=${answer.length}字, convId=${newConvId}`)

    // ── 存入上下文 ──
    pushContext(contextKey, userMsg, answer, config.最大上下文大小)
    // 更新 conversationId
    const updatedCtx = contexts.get(contextKey)
    if (updatedCtx && newConvId) {
      updatedCtx.conversationId = newConvId
    }
    ctx.logger.debug(`[DifyChat] 上下文已更新, 当前条目=${contexts.get(contextKey)?.entries.length}`)

    // ── 发送回答 ──
    await session.send(
      h('at', { id: session.userId }) + ' ' + answer
    )
  })
}
