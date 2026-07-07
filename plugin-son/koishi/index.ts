// index.ts 插件入口
// 职责：加载配置、注册子插件，不做具体业务处理
import { Context } from 'koishi'
import { getConfig } from './utils/config'

export const name = 'tshock-bind'

export function apply(ctx: Context) {
  // 预加载配置（失败直接抛错，防止子插件在无配置状态下运行）
  getConfig()

  ctx.logger.info('[TShock] 插件载入成功，正在加载子模块...')

  // 每个子插件有独立的 ctx 作用域，可独立注册事件监听
  ctx.plugin(require('./plugins/temp'))
  ctx.plugin(require('./plugins/group'))
  ctx.plugin(require('./plugins/private'))
  ctx.plugin(require('./plugins/join'))
}
