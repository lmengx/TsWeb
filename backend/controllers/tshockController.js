import tshockService from '../services/tshockService.js'

export const clearAllCharacter = async (req, res) => {
  const { username, password } = req.body

  if (!username || !password) {
    return res.status(400).json({ status: '400', error: 'username and password are required' })
  }

  const result = await tshockService.clearAllCharacter(username, password)

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

export const testCommand = async (req, res) => {
  const { cmd } = req.query
  
  if (!cmd) {
    return res.status(400).json({ error: 'cmd parameter is required' })
  }

  const result = await tshockService.executeCommand(cmd)
  res.json(result)
}

export const getUsers = async (req, res) => {
  const result = await tshockService.getUsers()
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
  
  if (result.error) {
    return res.json({ status: 'error', error: result.error })
  }
  
  res.json({ status: '200', inventory: result })
}

export const getUserList = async (req, res) => {
  const { username } = req.query
  const result = await tshockService.getUserList(username)
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

export const getGroups = async (req, res) => {
  const result = await tshockService.getGroups()
  res.json(result)
}

export const getGroup = async (req, res) => {
  const { groupName } = req.query
  if (!groupName) {
    return res.status(400).json({ error: 'groupName parameter is required' })
  }
  const result = await tshockService.getGroup(groupName)
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
  const { name, id, reason } = req.body
  
  if (!name && !id) {
    return res.status(400).json({ error: 'name or id is required' })
  }
  
  if (name && id) {
    return res.status(400).json({ error: 'specify either name or id, not both' })
  }

  const target = name || id
  const result = await tshockService.banPlayer(target, reason)
  res.json(result)
}



export const unbanPlayer = async (req, res) => {
  const { ticket, fullDelete } = req.body

  if (!ticket) {
    return res.status(400).json({ error: 'ticket is required' })
  }

  const result = await tshockService.unbanPlayer(ticket, fullDelete !== false)
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

export const getSelfInfo = async (req, res) => {
  const { username } = req.user
  
  if (!username) {
    return res.status(400).json({ error: 'username not found in token' })
  }
  
  const [userResult, invResult, onlineResult] = await Promise.all([
    tshockService.getUserList(username),
    tshockService.getInventory(username),
    tshockService.getActiveUsers()
  ])
  
  const userInfo = userResult.status === '200' && userResult.users && userResult.users.length > 0 
    ? userResult.users[0] 
    : null
    
  const inventory = invResult.error ? null : invResult
  
  const isOnline = onlineResult && onlineResult.activeusers 
    ? onlineResult.activeusers.split('\t').some(name => name.trim().toLowerCase() === username.toLowerCase())
    : false
  
  res.json({
    username,
    userInfo,
    inventory: inventory ? { status: '200', inventory } : { status: 'error', error: invResult.error },
    isOnline,
    success: !!userInfo
  })
}

export const clearCharacter = async (req, res) => {
  const { account } = req.body

  if (!account) {
    return res.status(400).json({ status: '400', error: 'account is required' })
  }

  const result = await tshockService.clearCharacter(account)

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
