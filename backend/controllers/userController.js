import tshockService from '../services/tshockService.js'

/**
 * 获取当前登录用户的基本信息（不含敏感字段）
 * 
 * 返回结构:
 * {
 *   username: "lmx",
 *   group: "superadmin",
 *   qq: "123456",
 *   isOnline: true,
 *   registered: "2024-01-01 12:00:00",
 *   lastAccessed: "2024-06-15 18:30:00"
 * }
 */
export const getSelfInfo = async (req, res) => {
  const { username } = req.user

  if (!username) {
    return res.status(400).json({ error: 'username not found in token' })
  }

  try {
    const userResult = await tshockService.getUserData(username)

    if (userResult.status !== '200' || !userResult.users || userResult.users.length === 0) {
      return res.json({
        username,
        error: '用户不存在',
        group: null,
        qq: null,
        isOnline: false,
        registered: null,
        lastAccessed: null
      })
    }

    const user = userResult.users[0]

    res.json({
      username: user.Username || user.username || username,
      group: user.Usergroup || user.usergroup || '默认',
      qq: user.QQ || user.qq || '',
      isOnline: !!(user.IsOnline || user.isOnline),
      registered: user.Registered || user.registered || null,
      lastAccessed: user.LastAccessed || user.lastAccessed || null
    })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/**
 * 获取当前登录用户的背包物品（展平结构）
 * 
 * 返回结构:
 * {
 *   items: [ { netID, prefix, stack, slot, favorited }, ... ],
 *   source: "memory" | "database",
 *   online: true | false
 * }
 */
export const getSelfInventory = async (req, res) => {
  const { username } = req.user

  if (!username) {
    return res.status(400).json({ error: 'username not found in token' })
  }

  try {
    const invResult = await tshockService.getInventory(username)

    if (invResult.error) {
      return res.json({
        items: [],
        source: null,
        online: false,
        error: invResult.error
      })
    }

    // invResult 来自插件 GetInv: { inventory: [...], source: "memory", online: true }
    res.json({
      items: invResult.inventory || [],
      source: invResult.source || 'database',
      online: !!(invResult.online)
    })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}
