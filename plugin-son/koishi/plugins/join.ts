import { Context, Session, h } from 'koishi'
import { getConfig } from '../utils/config'

export const name = 'tshock-join'

export function apply(ctx: Context) {
  const config = getConfig()
  ctx.logger.info('[tshock-join] 入群欢迎处理器已加载')

  ctx.on('guild-member-added', async (session: Session) => {
    if (Number(session.guildId) !== config.目标群号) return

    const qq = session.userId
    ctx.logger.info('[入群] 新成员:', qq)

    await session.send(h('at', { id: qq }) + ' 欢迎加入服务器群！\n发送「绑定 角色名」绑定你的游戏角色\n发送「注册 角色名」创建新角色并绑定')
  })
}
