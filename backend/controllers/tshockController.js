import tshockService, { getCurrentServerId } from '../services/tshockService.js'
import audit from '../services/auditLogger.js'
import { parseBanDurationSeconds, formatBanDuration } from '../lib/banDuration.js'

/**
 * 审计上下文：actor = 登录的后端账户，serverId = 当前服务器实例（来自 x-server-id，
 * 前端未带则为空串），ip = 来源地址（仅当该事件在 auditEvents.js 中 ip: true 时才写入）。
 */
function auditCtx(req, extra = {}) {
  return {
    serverId: getCurrentServerId() || '',
    actor: req.user?.username || 'unknown',
    ip: req.ip,
    ...extra
  }
}

export const clearAllCharacter = async (req, res) => {
  const { username, password } = req.body

  if (!username || !password) {
    return res.status(400).json({ status: '400', error: 'username and password are required' })
  }

  const result = await tshockService.clearAllCharacter(username, password)

  // 全服清角色（DELETE FROM tsCharacter，无 WHERE 条件）不可逆：无论成败都留审计。
  // player 用 '*' 表示全量；username 是用于校验密码的账户名。
  const clearAllDetail = { scope: 'all', verifyAccount: username }
  if (result.error) {
    clearAllDetail.error = result.error
  } else {
    if (result.totalCount !== undefined) clearAllDetail.totalCount = result.totalCount
    if (result.rowsAffected !== undefined) clearAllDetail.rowsAffected = result.rowsAffected
  }
  audit.safeRecord('user.clearcharacter', auditCtx(req, {
    player: '*',
    ok: !result.error,
    detail: clearAllDetail
  }))

  if (result.error) {
    return res.json({ status: 'error', error: result.error })
  }

  res.json({ status: '200', response: result.response || '角色数据已全部清空' })
}

export const executeCommand = async (req, res) => {
  const { command } = req.body
  
  if (!command) {
    return res.status(400).json({ error: 'Command is required' })
  }

  const result = await tshockService.executeCommand(command)
  res.json(result)
}

export const getUsers = async (req, res) => {
  const { page, pageSize, keyword, hasCharacter, onlineOnly } = req.query
  const result = await tshockService.getUsers({
    page,
    pageSize,
    keyword,
    hasCharacter,
    onlineOnly
  })
  res.json(result)
}

export const getActiveUsers = async (req, res) => {
  const result = await tshockService.getActiveUsers()
  res.json(result)
}

export const getInventory = async (req, res) => {
  const { player } = req.query
  
  if (!player) {
    return res.status(400).json({ status: '400', error: 'player parameter is required' })
  }

  const result = await tshockService.getInventory(player)

  // 查看他人背包：只读但涉隐私，成功与失败都留痕
  audit.safeRecord('user.invsee', auditCtx(req, {
    player,
    ok: !result.error,
    ...(result.error ? { detail: { error: result.error } } : {})
  }))
  
  if (result.error) {
    return res.json({ status: 'error', error: result.error })
  }
  
  res.json({ status: '200', inventory: result })
}

export const getUserData = async (req, res) => {
  const { username } = req.query
  const result = await tshockService.getUserData(username)
  res.json(result)
}

export const checkDuplicateIPs = async (req, res) => {
  const { username } = req.query
  if (!username) {
    return res.status(400).json({ status: '400', error: 'username parameter is required' })
  }
  const result = await tshockService.checkDuplicateIPs(username)
  res.json(result)
}

export const getAllDuplicateIPs = async (req, res) => {
  const result = await tshockService.getAllDuplicateIPs()
  res.send(result)
}

export const editInventory = async (req, res) => {
  const { player, slotIndex, itemId, stack, prefix } = req.body

  if (!player || slotIndex === undefined || itemId === undefined) {
    return res.status(400).json({ error: 'player, slotIndex, and itemId are required' })
  }

  const result = await tshockService.editInventory(
    player,
    parseInt(slotIndex),
    parseInt(itemId),
    parseInt(stack) || 1,
    parseInt(prefix) || 0
  )
  res.json(result)
}

export const batchEdit = async (req, res) => {
  const { player, data, clearUnspecified } = req.body

  if (!player || !data) {
    return res.status(400).json({ error: 'player and data are required' })
  }

  const result = await tshockService.batchEdit(player, data, !!clearUnspecified)
  res.json(result)
}

export const getGroups = async (req, res) => {
  const result = await tshockService.getGroups()
  res.json(result)
}

export const createGroup = async (req, res) => {
  const { groupName, parent, commands, chatColor, prefix, suffix } = req.body
  if (!groupName) {
    return res.status(400).json({ error: 'groupName is required' })
  }
  const result = await tshockService.createGroup(groupName, parent, commands, chatColor, prefix, suffix)
  res.json(result)
}

export const updateGroup = async (req, res) => {
  const { groupName, parent, chatColor, prefix, suffix } = req.body
  if (!groupName) {
    return res.status(400).json({ error: 'groupName is required' })
  }
  const result = await tshockService.updateGroup(groupName, parent, chatColor, prefix, suffix)
  res.json(result)
}

export const deleteGroup = async (req, res) => {
  const { groupName } = req.body
  if (!groupName) {
    return res.status(400).json({ error: 'groupName is required' })
  }
  const result = await tshockService.deleteGroup(groupName)
  res.json(result)
}

export const addGroupPermission = async (req, res) => {
  const { groupName, permission } = req.body
  if (!groupName || !permission) {
    return res.status(400).json({ error: 'groupName and permission are required' })
  }
  const result = await tshockService.addGroupPermission(groupName, permission)
  res.json(result)
}

export const removeGroupPermission = async (req, res) => {
  const { groupName, permission } = req.body
  if (!groupName || !permission) {
    return res.status(400).json({ error: 'groupName and permission are required' })
  }
  const result = await tshockService.removeGroupPermission(groupName, permission)
  res.json(result)
}

export const banPlayer = async (req, res) => {
  const { name, id, reason, durationSeconds } = req.body
  const character = req.user?.username || '后台操作'
  
  if (!name && !id) {
    return res.status(400).json({ error: 'name or id is required' })
  }
  
  if (name && id) {
    return res.status(400).json({ error: 'specify either name or id, not both' })
  }

  // 封禁时长：不传 / 空 = 永久封禁（保持原有语义，批量封禁等其他调用方不受影响）。
  // 校验与上限由 lib/banDuration.js 统一定义（那里说明了为什么抽出去），
  // 面板也会先拦一道，但这里才是权威判定。
  const parsedDuration = parseBanDurationSeconds(durationSeconds)

  if (!parsedDuration.ok) {
    return res.status(400).json({ error: parsedDuration.error })
  }

  const banSeconds = parsedDuration.seconds

  const target = name || id
  const pluginResult = await tshockService.banPlayer(target, reason, character, banSeconds)

  // 防"新旧版本混部署"造成的静默永久封禁：旧插件不认识 durationSeconds，会直接忽略这个参数，
  // 于是"想封 1 小时"实际按永久写库、还回报成功。这里认一下插件的回执字段：
  // 要求了临时封禁却拿不到 permanent 标记 = 插件根本没吃这个参数，必须如实报错让人去更新插件。
  // 插件是逐台服部署的（后端可以连多台），混部署是很容易出现的状态，所以这个检查不能省。
  const pluginOutdated = banSeconds !== null && !pluginResult.error && pluginResult.permanent === undefined

  const result = pluginOutdated
    ? { error: '封禁已按「永久」执行：游戏服插件未识别时长参数（插件版本过旧）。请更新插件后解封重封。' }
    : pluginResult

  // 封禁：留操作者（同时作为 TShock 封禁记录的来源）、理由与时长。
  // 时长记成"30 天 / 永久"这种给人看的文案——审计页是按 JSON 原样展示的，
  // 记秒数事后还得自己换算。
  const banDetail = {
    // 插件没识别时长参数时，实际执行的是永久封禁。审计要记"实际发生了什么"，
    // 而不是"本来想封多久"，否则事后追查会被这条记录误导。
    duration: pluginOutdated
      ? '永久（插件未识别时长参数）'
      : (banSeconds ? formatBanDuration(banSeconds) : '永久')
  }
  if (reason) banDetail.reason = reason
  // 实际被踢下线的人：封禁记录写成功但踢人失败时，这里能看出少踢了一个。
  // 取自 pluginResult 而不是 result——pluginOutdated 分支会重造 result，那里没有 kicked 字段。
  if (Array.isArray(pluginResult.kicked) && pluginResult.kicked.length) banDetail.kicked = pluginResult.kicked
  // 被本次覆盖的旧封禁票据号：TShock 每个标识只允许一条生效中的封禁，改时长是改写旧记录。
  // 记下来才看得出某条封禁在什么时候被谁改过，而不是凭空变了到期时间。
  if (Array.isArray(pluginResult.bansReplaced) && pluginResult.bansReplaced.length) {
    banDetail.replacedBans = pluginResult.bansReplaced
  }
  if (pluginOutdated) banDetail.pluginOutdated = true
  if (result.error) banDetail.error = result.error

  audit.safeRecord('user.ban', auditCtx(req, {
    player: target,
    ok: !result.error,
    ...(Object.keys(banDetail).length ? { detail: banDetail } : {})
  }))

  res.json(result)
}



export const unbanPlayer = async (req, res) => {
  const { ticket, fullDelete } = req.body

  if (!ticket) {
    return res.status(400).json({ error: 'ticket is required' })
  }

  const result = await tshockService.unbanPlayer(ticket, fullDelete !== false)

  // 解封：该接口只按 ticket 定位封禁记录，拿不到被解封者名字，故只记 ticket
  const unbanDetail = { ticket, fullDelete: fullDelete !== false }
  if (result.error) unbanDetail.error = result.error
  audit.safeRecord('user.unban', auditCtx(req, {
    ok: !result.error,
    detail: unbanDetail
  }))

  res.json(result)
}

export const createUser = async (req, res) => {
  const { username, password, group } = req.body

  if (!username || !password) {
    return res.status(400).json({ error: 'username and password are required' })
  }

  const result = await tshockService.createUser(username, password, group || '')
  res.json(result)
}



export const getBossProgress = async (req, res) => {
  const result = await tshockService.getBossProgress()
  res.json(result)
}

export const getBanList = async (req, res) => {
  const result = await tshockService.getBanList()
  res.json(result)
}

export const clearCharacter = async (req, res) => {
  const { account } = req.body

  if (!account) {
    return res.status(400).json({ status: '400', error: 'account is required' })
  }

  const result = await tshockService.clearCharacter(account)

  // 单个账号清角色（不可逆）：无论成败都留审计。player 记账号 ID。
  const clearOneDetail = { scope: 'single' }
  if (result.error) clearOneDetail.error = result.error
  else if (result.rowsAffected !== undefined) clearOneDetail.rowsAffected = result.rowsAffected
  audit.safeRecord('user.clearcharacter', auditCtx(req, {
    player: String(account),
    ok: !result.error,
    detail: clearOneDetail
  }))

  if (result.error) {
    return res.json({ status: 'error', error: result.error })
  }

  res.json({ status: '200', response: result.response || '角色数据已清空' })
}

export const scanItems = async (req, res) => {
  const result = await tshockService.scanItems()
  
  if (result.error) {
    return res.json({ status: 'error', error: result.error })
  }
  
  res.json(result)
}

export const scanItemById = async (req, res) => {
  const { itemId } = req.body
  
  if (!itemId || isNaN(itemId)) {
    return res.json({ status: 'error', error: 'itemId is required' })
  }
  
  const result = await tshockService.scanItemById(itemId)
  
  if (result.error) {
    return res.json({ status: 'error', error: result.error })
  }
  
  res.json(result)
}

export const getPlayerStats = async (req, res) => {
  const { player } = req.query
  
  if (!player) {
    return res.status(400).json({ status: '400', error: 'player parameter is required' })
  }

  const result = await tshockService.getPlayerStats(player)
  
  if (result.error) {
    return res.json({ status: 'error', error: result.error })
  }
  
  res.json({ status: '200', ...result })
}

export const setPlayerStats = async (req, res) => {
  const { player } = req.body
  
  if (!player) {
    return res.status(400).json({ status: '400', error: 'player parameter is required' })
  }

  const stats = { ...req.body }
  delete stats.player

  const result = await tshockService.setPlayerStats(player, stats)
  
  if (result.error) {
    return res.json({ status: 'error', error: result.error })
  }
  
  res.json({ status: '200', ...result })
}
