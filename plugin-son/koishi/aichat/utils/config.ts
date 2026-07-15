import { Schema } from 'koishi'

export const CONFIG_DEFAULTS = {
  /** 上下文最大条数 */
  最大上下文大小: 20,
  /** 上下文重置时间（秒），用户超过此时间未发言则清空上下文 */
  上下文重置时间: 600,
  /** 上下文清理间隔（秒） */
  清理间隔: 60,
  /** Dify API 超时（秒），Agent 带知识库时建议 120 秒以上 */
  请求超时: 120,
} as const

export interface Config {
  目标地址: string
  接口密钥: string
  生效群列表: number[]
  最大上下文大小: number
  上下文重置时间: number
  请求超时: number
}

export const Config: Schema<Config> = Schema.object({
  目标地址: Schema.string()
    .description('Dify API 地址（完整 URL，如 http://127.0.0.1:18000/v1）')
    .required(),
  接口密钥: Schema.string()
    .role('secret')
    .description('Dify Agent 应用 API 密钥')
    .required(),
  生效群列表: Schema.array(Schema.number())
    .description('机器人响应的群号列表')
    .default([]),
  最大上下文大小: Schema.number()
    .description('单个用户上下文存储的最大消息对数（超出删除最旧的）')
    .default(CONFIG_DEFAULTS.最大上下文大小),
  上下文重置时间: Schema.number()
    .description('用户超过此秒数未发言则清空其上下文（默认 600 秒 = 10 分钟）')
    .default(CONFIG_DEFAULTS.上下文重置时间),
  请求超时: Schema.number()
    .description('Dify API 请求超时时间（秒），Agent 带知识库检索时建议 120 秒以上')
    .default(CONFIG_DEFAULTS.请求超时)
    .min(10)
    .max(600),
})
