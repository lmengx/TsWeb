import { Context, Session, h } from 'koishi'
import type { Config } from '../utils/config'

export const name = 'tshock-join'

export function apply(ctx: Context, config: Config) {
  const groupSet = new Set(config.生效群列表)
  ctx.logger.info('[tshock-join] 入群欢迎处理器已加载')

  ctx.on('guild-member-added', async (session: Session) => {
    if (!groupSet.has(Number(session.guildId))) return

    const qq = session.userId
    ctx.logger.info('[入群] 新成员:', qq)

    await session.send(
      h('at', { id: qq }) +
      ' 欢迎加入服务器群！\n发送help获取帮助'
    )
  })
}
