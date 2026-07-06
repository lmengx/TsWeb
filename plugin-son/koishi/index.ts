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

  ctx.on('message', async (session: Session) => {
    // guildId 存在代表群消息
    if (session.guildId) {
      const groupId = Number(session.guildId)
      const senderQQ = session.userId
      const msgTime = session.timestamp
      const timeStr = new Date(msgTime).toLocaleString()
      const content = session.content.trim()

      // 只处理配置指定群
      if (groupId !== config.目标群号) {
        return
      }

      // 群聊 — 绑定已有角色到QQ
      if (content.startsWith("绑定 ")) {
        const playerName = content.replace("绑定 ", "").trim()
        const apiUrl = `http://${config.服务器地址}/data/qq/bind`
        const params = {
          token: config.接口密钥,
          player: playerName,
          qq: senderQQ
        }

        try {
          const res = await ctx.http.get(apiUrl, { params })
          await session.send("✅绑定成功✅\n服务器地址：tlry.me，端口37777\n请愉快地游玩吧 ")
        } catch (err) {
          const e = err as any
          const responseData = e.response?.data ?? {}
          const reason = responseData.error ?? e.message
          await session.send(`🔴绑定失败🔴\n${reason}`)
          console.error("[绑定]QQ",senderQQ,content,"失败：", reason)
        }
        return
      }

      // 群聊 — 注册新角色并绑定QQ
      if (content.startsWith("注册 ")) {
        const playerName = content.replace("注册 ", "").trim()
        const apiUrl = `http://${config.服务器地址}/data/qq/register`
        const params = {
          token: config.接口密钥,
          player: playerName,
          qq: senderQQ
        }

        try {
          const res = await ctx.http.get(apiUrl, { params })
          await session.send(`✅注册成功✅\n角色名：${playerName}\n请私聊我：改密码 密码 来设置您的登录密码`)
          console.log("[注册]QQ", senderQQ, "注册角色", playerName, "成功")
        } catch (err) {
          const e = err as any
          const responseData = e.response?.data ?? {}
          const reason = responseData.error ?? e.message
          await session.send(`🔴注册失败🔴\n${reason}`)
          console.error("[注册]QQ", senderQQ, "注册", playerName, "失败：", reason)
        }
        return
      }

      // 群聊 — 误发改密码，自动撤回并提示
      if (content.startsWith("改密码")) {
        try {
          await session.bot.deleteMessage(session.channelId, session.messageId)
        } catch {}
        await session.send(`${session.userId} 改密码请私聊我发送，已撤回`)
        return
      }
    }

    // 私聊 — 改密码
    if (!session.guildId) {
      const content = session.content.trim()

      if (content.startsWith("改密码 ")) {
        const newPassword = content.replace("改密码 ", "").trim()
        if (!newPassword) {
          await session.send("🔴密码不能为空")
          return
        }
        const apiUrl = `http://${config.服务器地址}/data/qq/reset-password`
        const params = {
          token: config.接口密钥,
          qq: session.userId,
          password: newPassword
        }

        try {
          const res = await ctx.http.get(apiUrl, { params })
          await session.send(`✅密码修改成功✅\n请使用新密码登录游戏`)
          console.log("[改密码]QQ", session.userId, "密码已修改")
        } catch (err) {
          const e = err as any
          const responseData = e.response?.data ?? {}
          const reason = responseData.error ?? e.message
          await session.send(`🔴修改失败🔴\n${reason}`)
          console.error("[改密码]QQ", session.userId, "失败：", reason)
        }
        return
      }
    }
  })
}
