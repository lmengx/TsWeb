import { Schema } from 'koishi'

export interface Config {
  生效群列表: number[]
  /** TSWeb 后端地址（机器人所有命令对接后端，如 127.0.0.1:3000） */
  后端地址: string
  /** 后端 bot token（config.bot.token） */
  机器人密钥: string
}

export const Config: Schema<Config> = Schema.object({
  生效群列表: Schema.array(Schema.number()).description('机器人响应的群号列表').default([]),
  后端地址: Schema.string().description('TSWeb 后端地址（host:port，如 127.0.0.1:3000）').default(''),
  机器人密钥: Schema.string().role('secret').description('后端 bot token（config.json bot.token）').default(''),
})

/** 安全调用 REST API（GET），不暴露地址、密钥等调试信息 */
export async function safeHttpGet(ctx: any, url: string, params: any): Promise<{ ok: true; data: any } | { ok: false; msg: string }> {
  try {
    const res = await ctx.http.get(url, { params, timeout: 8000 })
    return { ok: true, data: res }
  } catch (err: any) {
    const serverMsg = err.response?.data?.error
    if (serverMsg) {
      return { ok: false, msg: serverMsg }
    }
    return { ok: false, msg: '服务器错误，请联系管理员' }
  }
}

/** 安全调用 REST API（POST JSON），不暴露地址、密钥等调试信息 */
export async function safeHttpPost(ctx: any, url: string, params: any, body: any): Promise<{ ok: true; data: any } | { ok: false; msg: string }> {
  try {
    const res = await ctx.http.post(url, body, { params, timeout: 8000 })
    return { ok: true, data: res }
  } catch (err: any) {
    const serverMsg = err.response?.data?.error
    if (serverMsg) {
      return { ok: false, msg: serverMsg }
    }
    return { ok: false, msg: '服务器错误，请联系管理员' }
  }
}
