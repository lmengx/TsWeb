import { Context, Session } from 'koishi'
import type { Config } from '../../utils/config'

export const name = 'tshock-misc'

export function apply(ctx: Context, config: Config) {
  ctx.logger.info('[tshock-misc] 杂项处理器已加载')

  // — 自动同意好友请求 —
  ctx.on('friend-request', async (session: Session) => {
    ctx.logger.info('[好友请求] 来自:', session.userId)
    try {
      await session.approve()
      ctx.logger.info('[好友请求] 已同意:', session.userId)
    } catch (e: any) {
      ctx.logger.error('[好友请求] 同意失败:', e.message)
    }
  })
}
