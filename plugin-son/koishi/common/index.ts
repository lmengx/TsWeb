import { Context } from 'koishi'
import { Config } from './utils/config'
import { patchFromConfig, setActiveTheme, setThemeLogger } from './utils/theme'
import { setFooterBadge } from './utils/render/frame'

export const name = 'tshock-bind'
export { Config }

export function apply(ctx: Context, config: Config) {
  // 卡片样式主题：必须在子插件加载前设置，保证所有卡片用同一主题渲染
  // config.样式微调 是中文配置键，需经 patchFromConfig 映射为内部 ThemePatch
  setThemeLogger(msg => ctx.logger.warn(msg))
  setActiveTheme(config.样式主题, patchFromConfig(config.样式微调))
  setFooterBadge(config.徽标文字)
  ctx.logger.info(`[TShock] 插件载入成功（主题：${config.样式主题 || 'dark-glass'}）`)

  // 分流：群聊消息 → group，私聊消息 → private
  ctx.guild().plugin(require('./plugins/group'), config)
  ctx.private().plugin(require('./plugins/private'), config)

  // 入群事件、好友请求等不受 guild/private 限制
  ctx.plugin(require('./plugins/join'), config)
  ctx.plugin(require('./plugins/misc'), config)
}
