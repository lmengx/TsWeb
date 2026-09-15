import { Schema } from 'koishi'
import { DEFAULT_THEME_ID, THEMES } from './theme'

/** 主题微调（全部可选，留空即用主题默认值） */
export interface ThemePatchConfig {
  主色?: string
  卡片圆角?: string
  宽度缩放?: number
}

export interface Config {
  生效群列表: number[]
  /** TSWeb 后端地址（机器人所有命令对接后端，如 127.0.0.1:3000） */
  后端地址: string
  /** 后端 bot token（config.bot.token） */
  机器人密钥: string
  /** 卡片样式主题（见 utils/theme/themes/*） */
  样式主题: string
  /** 卡片样式微调 */
  样式微调: ThemePatchConfig
  /** 右下角徽标文字（信息卡 TSHOCK / 在线卡 LIVE 统一替换），留空用各卡默认 */
  徽标文字: string
}

export const Config: Schema<Config> = Schema.object({
  生效群列表: Schema.array(Schema.number()).description('机器人响应的群号列表').default([]),
  后端地址: Schema.string().description('TSWeb 后端地址（host:port，如 127.0.0.1:3000）').default(''),
  机器人密钥: Schema.string().role('secret').description('后端 bot token（config.json bot.token）').default(''),
  样式主题: Schema.union(
    THEMES.map(t => Schema.const(t.id).description(`${t.name}：${t.description}`)),
  ).description('卡片样式主题（改完重载插件即生效）').default(DEFAULT_THEME_ID),
  样式微调: Schema.object({
    主色: Schema.string().description('覆盖主题强调色，如 #ff9f45；留空用主题默认'),
    卡片圆角: Schema.string().description('覆盖卡片圆角，如 12px；留空用主题默认'),
    宽度缩放: Schema.number().min(0.6).max(1.6).step(0.05).description('卡片整体宽度缩放').default(1),
  }).description('主题微调（可选）'),
  徽标文字: Schema.string().description('右下角徽标文字（信息卡 TSHOCK / 在线卡 LIVE），留空用各卡默认').default(''),
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
