import jwt from 'jsonwebtoken'
import { getConfig } from '../config.js'

let JWT_SECRET = 'your-secret-key-here-change-in-production'

async function getJwtSecret() {
  if (JWT_SECRET === 'your-secret-key-here-change-in-production') {
    const config = await getConfig()
    JWT_SECRET = config.security.jwtSecret || 'your-secret-key-here-change-in-production'
  }
  return JWT_SECRET
}

export const verifyToken = async (req, res, next) => {
  const authHeader = req.headers.authorization
  const token = authHeader?.split(' ')[1]
  
  console.log(`[AUTH] verifyToken - Authorization header: ${authHeader ? 'Bearer ' + token?.slice(0, 20) + '...' : 'missing'}`)
  
  if (!token) {
    console.log(`[AUTH] verifyToken - Failed: No token provided`)
    return res.status(401).json({ error: 'Unauthorized' })
  }

  try {
    const secret = await getJwtSecret()
    const decoded = jwt.verify(token, secret)
    req.user = decoded
    console.log(`[AUTH] verifyToken - Success: user=${decoded.username}, usergroup=${decoded.usergroup}`)
    next()
  } catch (error) {
    console.log(`[AUTH] verifyToken - Failed: ${error.message}`)
    return res.status(401).json({ error: 'Invalid token' })
  }
}

export const requireRole = (role) => {
  return (req, res, next) => {
    console.log(`[AUTH] requireRole - Checking role: required=${role}, user=${req.user?.username}, usergroup=${req.user?.usergroup}`)
    
    if (!req.user || !req.user.usergroup) {
      console.log(`[AUTH] requireRole - Failed: No user group`)
      return res.status(403).json({ error: 'Forbidden: No user group' })
    }
    
    const userGroups = req.user.usergroup.split(',').map(g => g.trim().toLowerCase())
    console.log(`[AUTH] requireRole - User groups: ${userGroups.join(', ')}`)
    
    if (role.toLowerCase() === 'admin') {
      const adminRoles = ['owner', 'superadmin']
      const hasAdminAccess = userGroups.some(g => adminRoles.includes(g))
      console.log(`[AUTH] requireRole - Admin check: required roles=${adminRoles.join(', ')}, has access=${hasAdminAccess}`)
      if (!hasAdminAccess) {
        console.log(`[AUTH] requireRole - Failed: User is not owner/superadmin`)
        return res.status(403).json({ error: 'Forbidden: Requires owner or superadmin role' })
      }
    } else {
      if (!userGroups.includes(role.toLowerCase())) {
        console.log(`[AUTH] requireRole - Failed: User does not have ${role} role`)
        return res.status(403).json({ error: `Forbidden: Requires ${role} role` })
      }
    }
    
    console.log(`[AUTH] requireRole - Success: User has required role`)
    next()
  }
}
