import { Context, Session } from 'koishi'

export const name = 'tshock-misc'

const HELP_TEXT = [
  '━━━ 机器人功能 ━━━',
  '【群聊指令】',
  '注册 [角色名]    创建新角色',
  '绑定 [角色名]    绑定已有角色',
  '我的信息          查询玩家信息卡片',
  '进度              查询主服务器Boss进度（进度 服名 查指定服）',
  '在线              查看当前在线玩家（在线 服名 查指定服）',
  '【私聊指令】',
  '改密码 [新密码]   修改密码（同步到所有服务器）',
].join('\n')

export function apply(ctx: Context) {
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

  // — help（群聊私聊均生效）—
  ctx.on('message', async (session: Session) => {
    if (session.content.trim() === 'help') {
      await session.send(HELP_TEXT)
    }
  })
}
