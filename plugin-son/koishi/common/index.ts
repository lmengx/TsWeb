import { Context } from 'koishi'
import { Config } from './utils/config'

export const name = 'tshock-bind'
export { Config }

export function apply(ctx: Context, config: Config) {
  ctx.logger.info('[TShock] 插件载入成功')

  // 分流：群聊消息 → group，私聊消息 → private
  ctx.guild().plugin(require('./plugins/group'), config)
  ctx.private().plugin(require('./plugins/private'), config)

  // 入群事件、好友请求等不受 guild/private 限制
  ctx.plugin(require('./plugins/join'), config)
  ctx.plugin(require('./plugins/misc'), config)
}
