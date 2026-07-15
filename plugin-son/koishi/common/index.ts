import { Context } from 'koishi'
import { Config } from './utils/config'

export const name = 'tshock-bind'
export { Config }

export function apply(ctx: Context, config: Config) {
  ctx.logger.info('[TShock] 插件载入成功')

  ctx.plugin(require('./plugins/group'), config)
  ctx.plugin(require('./plugins/private'), config)
  ctx.plugin(require('./plugins/join'), config)
  ctx.plugin(require('./plugins/misc'), config)
}
