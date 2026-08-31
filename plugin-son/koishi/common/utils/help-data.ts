// ══════════════════════════════════════════════════════════
//  help 指令元数据（唯一数据源）
//  修改本文件即可同时更新：help 图片卡片（render.ts helpCard）
//  与文本兜底（misc/index.ts 程序化生成），无需两处同步。
//
//  channel: '群聊' | '私聊' | '@'
//  private: true 的分类 = 仅私聊可用（如改密码），
//  图片卡片中整区高亮、标题紫色标注；文本兜底渠道后缀为（私聊）。
// ══════════════════════════════════════════════════════════

export interface HelpItem {
  cmd: string
  desc: string
  channel: '群聊' | '私聊' | '@'
}

export interface HelpSection {
  title: string
  /** 私聊专属分类：整区高亮标注 */
  private?: boolean
  items: HelpItem[]
}

export const HELP_SECTIONS: HelpSection[] = [
  {
    title: '账号管理',
    items: [
      { cmd: '注册 角色名', desc: '创建新角色', channel: '群聊' },
      { cmd: '绑定 角色名', desc: '绑定已有角色', channel: '群聊' },
    ],
  },
  {
    title: '私聊指令',
    private: true,
    items: [
      { cmd: '改密码 新密码', desc: '修改登录密码', channel: '私聊' },
    ],
  },
  {
    title: '服务器查询',
    items: [
      { cmd: '我的信息', desc: '玩家信息卡片', channel: '群聊' },
      { cmd: '在线', desc: '当前在线玩家', channel: '群聊' },
      { cmd: '进度', desc: 'Boss 击杀进度', channel: '群聊' },
      { cmd: '服务器列表', desc: '已配置服务器', channel: '群聊' },
    ],
  },
  {
    title: '投票',
    items: [
      { cmd: '投票', desc: '查看投票状态', channel: '群聊' },
      { cmd: '参与投票 选项', desc: '选项名或编号', channel: '群聊' },
      { cmd: '投票提案 内容', desc: '提交自定义提案', channel: '群聊' },
    ],
  },

]
