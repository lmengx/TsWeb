import jwt from 'jsonwebtoken'
import forge from 'node-forge'
import { getConfig } from '../config.js'
import audit from '../services/auditLogger.js'
import {
  verifyAccount, createAccount, listAccounts, deleteAccount,
  changePassword as serviceChangePassword, resetPassword, updateRole, hasAnyAccount, hasAdmin,
  ADMIN_USERNAME
} from '../services/accountService.js'

let CHALLENGE_EXPIRE = 120000

const serverKeyPairs = new Map()

async function getJwtSecret() {
  const config = await getConfig()
  if (!config?.security?.jwtSecret) {
    // 密钥未配置时拒绝服务，绝不允许回退到弱密钥
    throw new Error('JWT secret not configured')
  }
  return config.security.jwtSecret
}

async function getChallengeExpire() {
  const config = await getConfig()
  return config?.security?.challengeExpire || 120000
}

async function getTokenExpire() {
  const config = await getConfig()
  return config.security.tokenExpire || '1h'
}

function generateServerKeyPair() {
  const keys = forge.pki.rsa.generateKeyPair(2048)
  const keyId = forge.util.bytesToHex(forge.random.getBytes(16))
  const expiresAt = Date.now() + CHALLENGE_EXPIRE

  serverKeyPairs.set(keyId, {
    privateKey: keys.privateKey,
    publicKey: keys.publicKey,
    expiresAt
  })

  setTimeout(() => {
    serverKeyPairs.delete(keyId)
  }, CHALLENGE_EXPIRE)

  return {
    keyId,
    publicKey: forge.pki.publicKeyToPem(keys.publicKey),
    expiresAt
  }
}

export const getServerKey = async (req, res) => {
  CHALLENGE_EXPIRE = await getChallengeExpire()
  const keyData = generateServerKeyPair()
  res.json(keyData)
}

/**
 * Setup Token 登录（URL ?token= 自动登录）
 * 已存在账户 → 拒绝（引导去正常登录页）；无账户（首次初始化）→ 签发 superadmin JWT
 */
export const setupLogin = async (req, res) => {
  const { validateSetupToken } = await import('../setupToken.js')
  const token = req.query.token
  if (!token || !validateSetupToken(token)) {
    return res.status(403).json({ error: '无效的 Setup Token' })
  }

  try {
    const hasAccounts = await hasAnyAccount()
    const secret = await getJwtSecret()
    const expire = await getTokenExpire()

    if (!hasAccounts) {
      // 首次初始化：签发 admin JWT，允许创建初始管理员
      const jwtToken = jwt.sign(
        { username: ADMIN_USERNAME, usergroup: 'admin' },
        secret,
        { expiresIn: expire }
      )
      audit.record('auth.setup_login', {
        username: ADMIN_USERNAME,
        via: 'setup-token',
        ip: req.ip
      })
      return res.json({ success: true, token: jwtToken, userGroup: 'admin', mustChangePassword: true, hasAccounts: false })
    }

    // 已存在账户 → 不允许 setup token 直接登录
    return res.json({ success: false, hasAccounts: true, message: '已存在账户，请使用账号密码登录' })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

/**
 * 登录：RSA-OAEP 加密密码 → 解密 → 查本地账号库 → bcrypt 比对 → 签发 JWT
 * 不依赖 TShock 服务器连接（后端账号独立托管）
 */
export const login = async (req, res) => {
  const { keyId, encryptedPassword, clientPublicKeyPem, username } = req.body

  if (!keyId || !encryptedPassword || !clientPublicKeyPem || !username) {
    return res.status(400).json({ error: 'Missing required fields' })
  }

  const serverKeyPair = serverKeyPairs.get(keyId)
  if (!serverKeyPair || Date.now() > serverKeyPair.expiresAt) {
    return res.status(400).json({ error: 'Invalid or expired server key' })
  }

  serverKeyPairs.delete(keyId)

  try {
    const encryptedBytes = forge.util.decode64(encryptedPassword)
    const decryptedPassword = serverKeyPair.privateKey.decrypt(encryptedBytes, 'RSA-OAEP')

    // 查本地账号（不再依赖 TShock）
    const account = await verifyAccount(username, decryptedPassword)

    if (!account) {
      // 统一失败语义：用户不存在/密码错误都记为登录失败（不泄露用户是否存在）
      audit.record('account.login_failed', {
        username: String(username).toLowerCase(),
        reason: 'invalid_credentials',
        ip: req.ip
      })
      await new Promise(r => setTimeout(r, 500))
      return res.status(401).json({ error: '用户名或密码错误' })
    }

    const secret = await getJwtSecret()
    const expire = await getTokenExpire()
    const token = jwt.sign(
      { username: account.username, usergroup: account.role },
      secret,
      { expiresIn: expire }
    )

    audit.record('account.login', {
      username: account.username,
      usergroup: account.role,
      via: 'password',
      ip: req.ip
    })

    try {
      const clientPublicKey = forge.pki.publicKeyFromPem(clientPublicKeyPem)
      const encryptedToken = forge.util.encode64(clientPublicKey.encrypt(token, 'RSA-OAEP'))
      res.json({
        success: true,
        encryptedToken,
        userGroup: account.role,
        mustChangePassword: account.mustChangePassword
      })
    } catch (e) {
      res.json({
        success: true,
        token,
        userGroup: account.role,
        mustChangePassword: account.mustChangePassword
      })
    }
  } catch (error) {
    console.error('Login error:', error)
    return res.status(500).json({ error: 'Server error' })
  }
}

/**
 * 自助改密（admin/subadmin 均可）：旧密码验证 + 新密码
 * 密码原文不落盘、不进日志（审计只记 username/actor）
 */
export const changePassword = async (req, res) => {
  try {
    const { oldPassword, newPassword } = req.body
    const username = req.user?.username
    if (!username || !oldPassword || !newPassword) {
      return res.status(400).json({ error: '旧密码和新密码均为必填' })
    }
    await serviceChangePassword(username, oldPassword, newPassword)
    audit.record('account.password_change', {
      username,
      actor: username
    })
    res.json({ success: true, message: '密码修改成功，请重新登录' })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

export const getUserInfo = (req, res) => {
  res.json({
    username: req.user.username,
    usergroup: req.user.usergroup
  })
}

// ═══════════════════════════════════════════════════════════
// 账户管理（仅 admin）
// ═══════════════════════════════════════════════════════════

export const getAccounts = async (req, res) => {
  try {
    const accounts = await listAccounts()
    res.json({ accounts })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const addAccount = async (req, res) => {
  try {
    const { username, password, role } = req.body
    if (!username || !password) {
      return res.status(400).json({ error: 'username 和 password 均为必填' })
    }
    const account = await createAccount(username, password, role || 'subadmin')
    audit.record('account.create', {
      username: account.username,
      role: account.role,
      actor: req.user?.username
    })
    res.json({ success: true, account })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

export const removeAccount = async (req, res) => {
  try {
    const { username } = req.params
    if (!username) return res.status(400).json({ error: 'username 为必填' })
    if (username.toLowerCase() === req.user?.username?.toLowerCase()) {
      return res.status(400).json({ error: '不能删除当前登录账户' })
    }
    const result = await deleteAccount(username)
    audit.record('account.delete', {
      username: result.username,
      actor: req.user?.username
    })
    res.json({ success: true })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

/** 管理员重置账户密码：生成随机密码，返回一次明文，强制改密 */
export const resetAccountPassword = async (req, res) => {
  try {
    const { username } = req.params
    if (!username) return res.status(400).json({ error: 'username 为必填' })
    const result = await resetPassword(username)
    audit.record('account.password_reset', {
      username: result.username,
      actor: req.user?.username,
      via: 'admin-api'
    })
    // 返回一次明文（显示在弹窗），落盘的是 bcrypt hash
    res.json({ success: true, username: result.username, plainPassword: result.plainPassword })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

export const changeAccountRole = async (req, res) => {
  try {
    const { username } = req.params
    const { role } = req.body
    if (!username || !role) return res.status(400).json({ error: 'username 和 role 均为必填' })
    const result = await updateRole(username, role)
    audit.record('account.role_change', {
      username: result.username,
      from: result.from,
      to: result.to,
      actor: req.user?.username
    })
    res.json({ success: true })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

/** 供初始化流程查询：是否需要创建初始管理员 */
export const getInitStatus = async (req, res) => {
  try {
    res.json({
      hasAccounts: await hasAnyAccount(),
      hasAdmin: await hasAdmin()
    })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}
