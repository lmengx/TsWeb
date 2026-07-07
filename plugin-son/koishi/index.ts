// index.ts 插件主文件
import { Context, Session } from 'koishi'
import fs from 'fs'
import path from 'path'

// 读取同目录下 config.json
const configPath = path.resolve(__dirname, './config.json')
let config: {
  目标群号: number
  服务器地址: string
  接口密钥: string
}

try {
  const raw = fs.readFileSync(configPath, 'utf-8')
  config = JSON.parse(raw)
  console.log("[TShock绑定插件] 配置加载完成，目标群：", config.目标群号)
} catch (err) {
  console.error("[TShock绑定插件] 读取配置失败：", err)
  throw err
}

export const name = 'tshock-bind'

export function apply(ctx: Context) {
  ctx.logger.info("[TShock绑定插件] 插件载入成功")

  // 辅助：临时会话检测
  function isTempSession(session: Session): boolean {
    return !!(session.guildId && (session as any).subsubtype === 'private')
  }

  // 辅助：回复临时消息
  // 临时会话在 Koishi 适配层有问题（user_id 被转成内部ID、缺 group_id）
  // 这里直接用 bot.internal 调用 NapCat 原生 API
  async function replyTemp(session: Session, text: string) {
    // 方式1: 原生 API sendPrivateMsg + group_id（OneBot 标准方式）
    try {
      const bot = session.bot as any
      const qq = session.userId
      const groupId = Number(session.guildId)

      if (bot.internal?.sendPrivateMsg) {
        await bot.internal.sendPrivateMsg(qq, text, { group_id: groupId })
        return
      }
    } catch {}

    // 方式2: ctx.http 直调 NapCat HTTP API（兜底）
    try {
      // 从 bot 配置获取 NapCat 地址
      const bot = session.bot as any
      const napcatUrl = `http://127.0.0.1:${bot.port || 3001}`
      const token = bot.token || ''
      const qq = session.userId
      const groupId = Number(session.guildId)

      await ctx.http.post(`${napcatUrl}/send_private_msg`, {
        user_id: Number(qq),
        group_id: groupId,
        message: text
      }, {
        headers: { Authorization: `Bearer ${token}` }
      })
      return
    } catch (e: any) {
      console.log("[replyTemp] HTTP API 也失败:", e.message)
    }

    // 都失败则写日志，不阻塞流程
    console.log("[replyTemp] 无法回复临时会话, userId:", session.userId, "guildId:", session.guildId)
  }

  ctx.on('message', async (session: Session) => {
    // ===== 临时会话单独处理 =====
    if (isTempSession(session)) {
      const content = session.content.trim()
      const senderQQ = session.userId
      const groupId = Number(session.guildId)

      if (groupId !== config.目标群号) return

      // 临时会话 — 改密码
      if (content.startsWith("改密码 ")) {
        const newPassword = content.replace("改密码 ", "").trim()
        if (!newPassword) {
          await replyTemp(session, "密码不能为空")
          return
        }
        const apiUrl = `http://${config.服务器地址}/data/qq/reset-password`
        const params = {
          token: config.接口密钥,
          qq: senderQQ,
          password: newPassword
        }
        try {
          const res = await ctx.http.get(apiUrl, { params })
          await replyTemp(session, "密码修改成功，请使用新密码登录游戏")
          console.log("[改密码]QQ", senderQQ, "密码已修改（临时会话）")
        } catch (err) {
          const e = err as any
          const reason = e.response?.data?.error ?? e.message
          await replyTemp(session, `修改失败：${reason}`)
          console.error("[改密码]QQ", senderQQ, "失败：", reason)
        }
        return
      }
      return
    }

    // ===== 正常群聊 =====
    if (session.guildId) {
      const groupId = Number(session.guildId)
      const senderQQ = session.userId
      const content = session.content.trim()

      if (groupId !== config.目标群号) return

      if (content.startsWith("绑定 ")) {
        const playerName = content.replace("绑定 ", "").trim()
        const apiUrl = `http://${config.服务器地址}/data/qq/bind`
        const params = { token: config.接口密钥, player: playerName, qq: senderQQ }
        try {
          const res = await ctx.http.get(apiUrl, { params })
          await session.send("✅绑定成功✅\n服务器地址：tlry.me，端口37777\n请愉快地游玩吧 ")
        } catch (err) {
          const e = err as any
          const reason = e.response?.data?.error ?? e.message
          await session.send(`🔴绑定失败🔴\n${reason}`)
          console.error("[绑定]QQ", senderQQ, "失败：", reason)
        }
        return
      }

      if (content.startsWith("注册 ")) {
        const playerName = content.replace("注册 ", "").trim()
        const apiUrl = `http://${config.服务器地址}/data/qq/register`
        const params = { token: config.接口密钥, player: playerName, qq: senderQQ }
        try {
          const res = await ctx.http.get(apiUrl, { params })
          await session.send(`✅注册成功✅\n角色名：${playerName}\n请私聊我：改密码 密码 来设置您的登录密码`)
          console.log("[注册]QQ", senderQQ, "注册角色", playerName, "成功")
        } catch (err) {
          const e = err as any
          const reason = e.response?.data?.error ?? e.message
          await session.send(`🔴注册失败🔴\n${reason}`)
          console.error("[注册]QQ", senderQQ, "失败：", reason)
        }
        return
      }

      if (content.startsWith("改密码")) {
        try { await session.bot.deleteMessage(session.channelId, session.messageId) } catch {}
        await session.send(`${senderQQ} 改密码请私聊我发送，已撤回`)
        return
      }
      return
    }

    // ===== 好友私聊 — 改密码 =====
    if (!session.guildId) {
      const content = session.content.trim()
      if (content.startsWith("改密码 ")) {
        const newPassword = content.replace("改密码 ", "").trim()
        if (!newPassword) {
          await session.send("🔴密码不能为空")
          return
        }
        const apiUrl = `http://${config.服务器地址}/data/qq/reset-password`
        const params = { token: config.接口密钥, qq: session.userId, password: newPassword }
        try {
          const res = await ctx.http.get(apiUrl, { params })
          await session.send("✅密码修改成功✅\n请使用新密码登录游戏")
          console.log("[改密码]QQ", session.userId, "密码已修改（私聊）")
        } catch (err) {
          const e = err as any
          const reason = e.response?.data?.error ?? e.message
          await session.send(`🔴修改失败🔴\n${reason}`)
          console.error("[改密码]QQ", session.userId, "失败：", reason)
        }
        return
      }
    }
  })
}
