import { Context } from 'koishi'
import { Config } from './utils/config'

export const name = 'dify-chat'
export { Config }

export function apply(ctx: Context, config: Config) {
  ctx.logger.info('[DifyChat] 插件载入成功')

  ctx.plugin(require('./plugins/dify'), config)
}
