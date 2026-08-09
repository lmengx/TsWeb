export const ADMIN_ROLES = ['admin']           // 全局唯一管理员
export const MANAGER_ROLES = ['admin', 'subadmin']  // 所有管理（admin + 子管理员）

export const getUserFromStorage = () => {
  try {
    const saved = localStorage.getItem('user')
    if (saved) {
      return JSON.parse(saved)
    }
    return null
  } catch {
    console.error('Failed to parse user from localStorage')
    return null
  }
}

const getUserGroups = (user) => {
  if (!user || !user.usergroup) return []
  return String(user.usergroup).split(',').map(g => g.trim().toLowerCase())
}

/** 仅 admin（后端级配置、审计、服务器管理） */
export const isAdmin = (user = null) => {
  const userData = user || getUserFromStorage()
  return getUserGroups(userData).some(g => ADMIN_ROLES.includes(g))
}

/** 所有管理（admin + subadmin）可用的服务器操作 */
export const isManager = (user = null) => {
  const userData = user || getUserFromStorage()
  return getUserGroups(userData).some(g => MANAGER_ROLES.includes(g))
}

/** 兼容旧名：登录即可管理（拥有任一管理角色） */
export const hasPermission = (requiredRoles = []) => {
  const user = getUserFromStorage()
  const groups = getUserGroups(user)
  return requiredRoles.some(role => groups.includes(role.toLowerCase()))
}
