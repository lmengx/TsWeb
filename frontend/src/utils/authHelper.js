export const ADMIN_ROLES = ['owner', 'superadmin']

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

export const isAdmin = (user = null) => {
  const userData = user || getUserFromStorage()
  if (!userData || !userData.usergroup) {
    return false
  }
  
  const usergroups = userData.usergroup.split(',').map(g => g.trim().toLowerCase())
  return usergroups.some(g => ADMIN_ROLES.includes(g))
}

export const hasPermission = (requiredRoles = []) => {
  const user = getUserFromStorage()
  if (!user || !user.usergroup) {
    return false
  }
  
  const usergroups = user.usergroup.split(',').map(g => g.trim().toLowerCase())
  return requiredRoles.some(role => usergroups.includes(role.toLowerCase()))
}