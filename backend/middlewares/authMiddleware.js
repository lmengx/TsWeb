import jwt from 'jsonwebtoken'
import { getConfig } from '../config.js'
import audit from '../services/auditLogger.js'

async function getJwtSecret() {
  const config = await getConfig()
  if (!config?.security?.jwtSecret) {
    // 密钥未配置时拒绝服务，绝不允许回退到弱密钥
    throw new Error('JWT secret not configured')
  }
  return config.security.jwtSecret
}

// 角色常量（与 accountService 保持一致）
const ROLE_ADMIN = 'admin'          // 全局唯一管理员：全部权限
const ROLE_SUBADMIN = 'subadmin'    // 子管理员：服务器操作（除文件/后端配置）

/** 从 JWT usergroup 字段解析角色列表（兼容逗号分隔） */
function getRoles(req) {
  if (!req.user?.usergroup) return []
  return String(req.user.usergroup).split(',').map(g => g.trim().toLowerCase())
}

export const verifyToken = async (req, res, next) => {
  const authHeader = req.headers.authorization
  const token = authHeader?.split(' ')[1]

  if (!token) {
    return res.status(401).json({ error: 'Unauthorized' })
  }

  try {
    const secret = await getJwtSecret()
    const decoded = jwt.verify(token, secret)
    req.user = decoded
    next()
  } catch (error) {
    // 无效/过期 token 落审计（warn），辅助追踪异常访问
    audit.record('auth.token_invalid', {
      actor: 'unknown',
      reason: error.message,
      ip: req.ip
    })
    return res.status(401).json({ error: 'Invalid token' })
  }
}

/**
 * 仅 admin：后端级配置（账户管理、服务器管理、后端配置、webhook 回传、审计日志）
 */
export const requireAdmin = (req, res, next) => {
  const roles = getRoles(req)
  if (!roles.includes(ROLE_ADMIN)) {
    return res.status(403).json({ error: 'Forbidden: Requires admin role' })
  }
  next()
}

/**
 * 所有管理（admin + subadmin）：服务器内操作（发命令、查信息、权限组、封禁等）
 */
export const requireManager = (req, res, next) => {
  const roles = getRoles(req)
  if (!roles.includes(ROLE_ADMIN) && !roles.includes(ROLE_SUBADMIN)) {
    return res.status(403).json({ error: 'Forbidden: Requires admin or subadmin role' })
  }
  next()
}

/**
 * 仅玩家（QQ 台账登录）：投票等玩家端接口
 * 实时校验台账存在性：解绑后旧 JWT 立即失效（投票资格语义）——
 * 即使不引入 tokenVersion，资格层面也必须实时查账（改密不失效，解绑必须失效）
 */
export const requirePlayer = async (req, res, next) => {
  const roles = getRoles(req)
  if (!roles.includes('player')) {
    return res.status(403).json({ error: 'Forbidden: Requires player role' })
  }
  try {
    const { getAccountByUsernameCI } = await import('../services/qqAccountService.js')
    const account = await getAccountByUsernameCI(req.user?.username)
    if (!account) {
      return res.status(401).json({ error: '账号不存在或已解绑' })
    }
    next()
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

/**
 * 兼容旧签名：requireRole('admin') → 视为管理员
 * 新代码请使用 requireAdmin / requireManager 明确语义
 */
export const requireRole = (role) => {
  return (req, res, next) => {
    if (String(role).toLowerCase() === 'admin') {
      return requireAdmin(req, res, next)
    }
    const roles = getRoles(req)
    if (!roles.includes(String(role).toLowerCase())) {
      return res.status(403).json({ error: `Forbidden: Requires ${role} role` })
    }
    next()
  }
}
