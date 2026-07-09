import { Schema } from 'koishi'

export interface Config {
  生效群列表: number[]
  服务器地址: string
  接口密钥: string
}

export const Config: Schema<Config> = Schema.object({
  生效群列表: Schema.array(Schema.number()).description('机器人响应的群号列表').default([]),
  服务器地址: Schema.string().description('TShock REST API 地址（host:port，如 127.0.0.1:7878）'),
  接口密钥: Schema.string().role('secret').description('TShock REST API 密钥'),
})

/** 安全调用 REST API，不暴露地址、密钥等调试信息 */
export async function safeHttpGet(ctx: any, url: string, params: any): Promise<{ ok: true; data: any } | { ok: false; msg: string }> {
  try {
    const res = await ctx.http.get(url, { params, timeout: 5000 })
    return { ok: true, data: res }
  } catch (err: any) {
    const serverMsg = err.response?.data?.error
    if (serverMsg) {
      return { ok: false, msg: serverMsg }
    }
    return { ok: false, msg: '服务器错误，请联系管理员' }
  }
}
