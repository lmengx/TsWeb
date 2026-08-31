/**
 * 审计事件注册表（单一事实来源）
 * 每个事件声明：级别、分类、展示名、需记录的字段白名单、是否记录 IP、需脱敏字段
 * 业务代码通过 audit.record(event, ctx) 记录，未在注册表中的 event 会抛错（防手滑）
 */
export const AUDIT_EVENTS = {
  // ═══ auth 类 — 登录与会话 ═══
  'account.login': {
    level: 'info', category: 'auth', title: '账户登录',
    fields: ['username', 'usergroup', 'via'], ip: true, sensitive: []
  },
  'account.login_failed': {
    level: 'warn', category: 'auth', title: '登录失败（密码错误）',
    fields: ['username', 'reason'], ip: true, sensitive: []
  },
  'player.login': {
    level: 'info', category: 'auth', title: '玩家登录（QQ 台账）',
    fields: ['username', 'qq', 'via'], ip: true, sensitive: []
  },
  'player.login_failed': {
    level: 'warn', category: 'auth', title: '玩家登录失败',
    fields: ['username', 'reason'], ip: true, sensitive: []
  },
  'auth.setup_login': {
    level: 'info', category: 'auth', title: 'Setup Token 登录',
    fields: ['username', 'via'], ip: true, sensitive: []
  },
  'auth.token_invalid': {
    level: 'warn', category: 'auth', title: '无效 Token 访问',
    fields: ['username', 'reason'], ip: true, sensitive: []
  },

  // ═══ account 类 — 后端账户管理 ═══
  'account.create': {
    level: 'info', category: 'account', title: '创建后端账户',
    fields: ['username', 'role', 'actor'], ip: false, sensitive: []
  },
  'account.delete': {
    level: 'error', category: 'account', title: '删除后端账户',
    fields: ['username', 'actor'], ip: false, sensitive: []
  },
  'account.password_change': {
    level: 'info', category: 'account', title: '修改密码',
    fields: ['username', 'actor'], ip: false, sensitive: ['password']
  },
  'account.password_reset': {
    level: 'warn', category: 'account', title: '重置密码',
    fields: ['username', 'actor', 'via'], ip: false, sensitive: []
  },
  'account.role_change': {
    level: 'warn', category: 'account', title: '变更账户角色',
    fields: ['username', 'from', 'to', 'actor'], ip: false, sensitive: []
  },

  // ═══ server 类 — 服务器管理 ═══
  'server.add': {
    level: 'info', category: 'server', title: '添加服务器',
    fields: ['name', 'host', 'port', 'actor'], ip: false, sensitive: []
  },
  'server.update': {
    level: 'warn', category: 'server', title: '修改服务器',
    fields: ['id', 'name', 'host', 'port', 'changedKeys', 'actor'], ip: false, sensitive: []
  },
  'server.delete': {
    level: 'error', category: 'server', title: '删除服务器',
    fields: ['id', 'name', 'actor'], ip: false, sensitive: []
  },
  'server.test': {
    level: 'info', category: 'server', title: '测试服务器连接',
    fields: ['id', 'name', 'success', 'error'], ip: false, sensitive: []
  },

  // ═══ command 类 — 后端手动发命令 ═══
  'command.execute': {
    level: 'info', category: 'command', title: '执行服务器命令',
    fields: ['command', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'command.execute_failed': {
    level: 'error', category: 'command', title: '执行命令失败',
    fields: ['command', 'serverId', 'actor', 'error'], ip: false, sensitive: []
  },

  // ═══ file 类 — 文件管理 ═══
  'file.read': {
    level: 'info', category: 'file', title: '读取文件',
    fields: ['path', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'file.write': {
    level: 'warn', category: 'file', title: '写入文件',
    fields: ['path', 'size', 'serverId', 'actor'], ip: false, sensitive: []
  },

  // ═══ config 类 — 后端/插件配置 ═══
  'config.update': {
    level: 'warn', category: 'config', title: '修改后端配置',
    fields: ['changedKeys', 'actor'], ip: false, sensitive: []
  },
  'config.tsweb.set': {
    level: 'info', category: 'config', title: '修改 TSWeb 插件配置',
    fields: ['changedKeys', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'config.anticheat.save': {
    level: 'info', category: 'config', title: '保存反作弊配置',
    fields: ['serverId', 'actor'], ip: false, sensitive: []
  },
  'config.boss.set': {
    level: 'info', category: 'config', title: '修改 BOSS 配置',
    fields: ['serverId', 'actor'], ip: false, sensitive: []
  },
  'config.promotion.set': {
    level: 'info', category: 'config', title: '修改权限提升配置',
    fields: ['serverId', 'actor'], ip: false, sensitive: []
  },
  'config.backup.set': {
    level: 'info', category: 'config', title: '修改自动备份配置',
    fields: ['serverId', 'actor'], ip: false, sensitive: []
  },

  // ═══ cross_transfer 类 — 跨服传送配置（单服直连，读/写插件端）═══
  'crossTransfer.save': {
    level: 'warn', category: 'cross_transfer', title: '保存跨服传送配置到插件端',
    fields: ['serverId', 'enabled', 'targets', 'actor'], ip: false, sensitive: []
  },
  'crossTransfer.probe': {
    level: 'info', category: 'cross_transfer', title: '探测跨服目标可达性',
    fields: ['serverId', 'count', 'actor'], ip: false, sensitive: []
  },

  // ═══ backup 类 — 自动备份 ═══
  'backup.received': {
    level: 'info', category: 'backup', title: '接收自动备份',
    fields: ['serverId', 'name', 'size'], ip: false, sensitive: []
  },
  'backup.failed': {
    level: 'error', category: 'backup', title: '自动备份推送失败',
    fields: ['serverId', 'error'], ip: false, sensitive: []
  },

  // ═══ setup 类 — 初始化 ═══
  'setup.create_admin': {
    level: 'warn', category: 'setup', title: '首次创建管理员',
    fields: ['username', 'ip'], ip: true, sensitive: []
  },
  'setup.init': {
    level: 'info', category: 'setup', title: '初始化服务器',
    fields: ['serverName', 'actor'], ip: false, sensitive: []
  },
  'setup.plugin_init': {
    level: 'info', category: 'setup', title: '插件初始化',
    fields: ['mode', 'serverId'], ip: false, sensitive: []
  },

  // ═══ user 类 — 玩家管理操作 ═══
  'user.ban': {
    level: 'warn', category: 'user', title: '封禁玩家',
    fields: ['player', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'user.unban': {
    level: 'info', category: 'user', title: '解封玩家',
    fields: ['player', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'user.clearcharacter': {
    level: 'error', category: 'user', title: '清除角色数据',
    fields: ['player', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'user.invsee': {
    level: 'warn', category: 'user', title: '查看玩家背包',
    fields: ['player', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'user.password_query': {
    level: 'error', category: 'user', title: '查询玩家密码',
    fields: ['player', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'unverified.kick': {
    level: 'info', category: 'user', title: '踢出未验证玩家',
    fields: ['nickname', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'unverified.ban': {
    level: 'warn', category: 'user', title: '封禁未验证玩家',
    fields: ['nickname', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'unverified.force_login': {
    level: 'warn', category: 'user', title: '强制登录未验证玩家',
    fields: ['nickname', 'serverId', 'actor'], ip: false, sensitive: []
  },

  // ═══ qq_account 类 — QQ 账号台账同步 ═══
  'qq_account.register': {
    level: 'info', category: 'qq_account', title: 'QQ 注册角色',
    fields: ['username', 'qq'], ip: false, sensitive: []
  },
  'qq_account.bind': {
    level: 'info', category: 'qq_account', title: 'QQ 绑定角色',
    fields: ['username', 'qq', 'serverId'], ip: false, sensitive: []
  },
  'qq_account.bound': {
    level: 'info', category: 'qq_account', title: '服务器上报绑定',
    fields: ['username', 'qq', 'serverId'], ip: false, sensitive: []
  },
  'qq_account.change_password': {
    level: 'warn', category: 'qq_account', title: 'QQ 修改密码',
    fields: ['username', 'qq'], ip: false, sensitive: ['password']
  },
  'qq_account.unbind': {
    level: 'warn', category: 'qq_account', title: 'QQ 解绑角色',
    fields: ['username', 'qq'], ip: false, sensitive: []
  },
  'qq_account.rebind': {
    level: 'warn', category: 'qq_account', title: 'QQ 改绑角色',
    fields: ['username', 'qq', 'from'], ip: false, sensitive: []
  },
  'qq_playtime.refresh': {
    level: 'info', category: 'qq_account', title: '手动刷新多服时长',
    fields: ['ok', 'total', 'actor'], ip: false, sensitive: []
  },
  'config.bot.set': {
    level: 'warn', category: 'config', title: '修改 QQ 机器人设置',
    fields: ['changedKeys', 'actor'], ip: false, sensitive: []
  },

  // ═══ vote 类 — 投票轮次/投票/提案 ═══
  'vote.round.create': {
    level: 'info', category: 'vote', title: '创建投票轮次',
    fields: ['id', 'title', 'actor'], ip: false, sensitive: []
  },
  'vote.round.close': {
    level: 'warn', category: 'vote', title: '结束投票轮次',
    fields: ['id', 'title', 'actor'], ip: false, sensitive: []
  },
  'vote.round.archive': {
    level: 'warn', category: 'vote', title: '归档投票轮次',
    fields: ['id', 'title', 'actor'], ip: false, sensitive: []
  },
  'vote.round.unarchive': {
    level: 'info', category: 'vote', title: '取消归档投票轮次',
    fields: ['id', 'title', 'actor'], ip: false, sensitive: []
  },
  'vote.round.update': {
    level: 'warn', category: 'vote', title: '编辑投票轮次',
    fields: ['id', 'title', 'changedKeys', 'actor'], ip: false, sensitive: []
  },
  'vote.round.delete': {
    level: 'error', category: 'vote', title: '删除投票轮次',
    fields: ['id', 'actor'], ip: false, sensitive: []
  },
  'vote.cast': {
    level: 'info', category: 'vote', title: '玩家投票',
    fields: ['roundId', 'username', 'optionId', 'weight'], ip: false, sensitive: []
  },
  'vote.propose': {
    level: 'info', category: 'vote', title: '玩家提交自定义提案',
    fields: ['roundId', 'username', 'optionId', 'anonymous'], ip: false, sensitive: []
  },

  // ═══ permission 类 — 个人独立权限签发/回收 ═══
  'permission.grant': {
    level: 'info', category: 'permission', title: '签发个人权限',
    fields: ['player', 'permission', 'note', 'expireAt', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'permission.grant_batch': {
    level: 'info', category: 'permission', title: '批量签发个人权限',
    fields: ['players', 'permissions', 'note', 'expireAt', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'permission.revoke': {
    level: 'warn', category: 'permission', title: '回收个人权限',
    fields: ['player', 'permission', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'permission.revoke_batch': {
    level: 'warn', category: 'permission', title: '批量回收个人权限',
    fields: ['players', 'permissions', 'serverId', 'actor'], ip: false, sensitive: []
  },
  'permission.cleanup': {
    level: 'info', category: 'permission', title: '清理过期个人权限',
    fields: ['cleaned', 'serverId', 'actor'], ip: false, sensitive: []
  },

  // ═══ worldmodify 类 — 世界修改器（单服直连，读/写插件端 /data/worldmodify/*）═══
  'worldModify.apply': {
    level: 'warn', category: 'worldmodify', title: '修改世界参数（已击败标记/时间/天气等）',
    fields: ['serverId', 'fields', 'applied', 'actor'], ip: false, sensitive: []
  },

  // ═══ system 类 ═══
  'system.start': {
    level: 'info', category: 'system', title: '后端启动',
    fields: ['version', 'nodeVersion'], ip: false, sensitive: []
  },
  'system.stop': {
    level: 'info', category: 'system', title: '后端停止',
    fields: ['reason'], ip: false, sensitive: []
  }
}

/** 全局敏感字段名黑名单：出现在记录上下文中的这些 key 一律脱敏，绝不落盘 */
export const SENSITIVE_KEYS = [
  'password', 'newPassword', 'oldPassword', 'encryptedPassword', 'plainPassword',
  'apiKey', 'pushSecret', 'token', 'jwtSecret', 'authorization'
]

export function getEventMeta(event) {
  return AUDIT_EVENTS[event]
}

/** 获取全部事件（供前端筛选下拉动态渲染） */
export function listEvents() {
  return Object.entries(AUDIT_EVENTS).map(([event, meta]) => ({
    event, ...meta
  }))
}
