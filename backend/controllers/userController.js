/**
 * 当前登录用户（后端账号系统）信息
 *
 * 后端账号系统（accounts.json + JWT）已独立于 TShock 游戏账号：
 *  - 不再查询 TShock 游戏账号（游戏在线状态 / QQ 绑定 / 注册时间等均为游戏侧属性，与后端账号无关）
 *  - 仅返回后端账号自身信息（用户名 + 角色）
 */
export const getSelfInfo = async (req, res) => {
  const { username, usergroup } = req.user || {}

  if (!username) {
    return res.status(400).json({ error: 'username not found in token' })
  }

  res.json({
    username,
    group: usergroup || ''
  })
}
