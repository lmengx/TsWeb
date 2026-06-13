import tshockService from '../services/tshockService.js'

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
  res.send(result)
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
