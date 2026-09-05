import jwt from 'jsonwebtoken'
import forge from 'node-forge'
import bcrypt from 'bcrypt'
import { getConfig } from '../config.js'
import audit from '../services/auditLogger.js'
import { getAccountByQq, getAccountByUsernameCI } from '../services/qqAccountService.js'
import { getPlaytimeRecords } from '../services/qqPlaytimeService.js'
import { listRounds, calcUserWeight } from '../services/voteService.js'
import {
  verifyAccount, createAccount, listAccounts, deleteAccount,
  changePassword as serviceChangePassword, resetPassword, updateRole, hasAnyAccount,
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
      return res.json({ success: true, token: jwtToken, userGroup: 'admin', hasAccounts: false })
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
        userGroup: account.role
      })
    } catch (e) {
      res.json({
        success: true,
        token,
        userGroup: account.role
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
    const { username, password, role, linkedTo } = req.body
    if (!username && !linkedTo) {
      return res.status(400).json({ error: '请提供用户名，或选择要关联的 QQ 台账用户' })
    }
    // linkedTo：从现有 QQ 台账选取用户授予管理身份（登录凭证以台账为准，可设多 admin）
    const account = await createAccount(username, password, role || 'subadmin', { linkedTo })
    audit.record('account.create', {
      username: account.username,
      role: account.role,
      linkedTo: account.linkedTo || '',
      actor: req.user?.username
    })
    res.json({ success: true, account })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

/**
 * 可选关联的 QQ 台账用户列表（尚未成为管理端账户）：供 admin 添加管理员时选择
 * 返回：{ accounts: [{ username, qq }] }
 */
export const getLinkableAccounts = async (req, res) => {
  try {
    const { getAccounts } = await import('../services/qqAccountService.js')
    const records = await getAccounts()
    const existing = await listAccounts()
    const used = new Set(existing.map(a => String(a.username).toLowerCase()))
    const linkable = Object.entries(records)
      .filter(([name]) => !used.has(String(name).toLowerCase()))
      .map(([name, rec]) => ({ username: name, qq: String(rec.qq || '') }))
      .sort((a, b) => a.username.localeCompare(b.username))
    res.json({ accounts: linkable })
  } catch (err) {
    res.status(500).json({ error: err.message })
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
    if (String(username).toLowerCase() === String(req.user?.username || '').toLowerCase()) {
      return res.status(400).json({ error: '不能修改当前登录账户的角色' })
    }
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

// ═══════════════════════════════════════════════════════════
// 玩家登录（QQ 台账体系，独立于管理端 accounts.json）
//
// JWT payload: { username, qq, usergroup: 'player' }
//   - usergroup='player' 复用现有角色体系：requireAdmin/requireManager 自动拒绝，
//     前端 authHelper isAdmin/isManager 自动拒绝管理页面（零改动隔离）
//   - 有效期 security.playerTokenExpire（默认 7d），独立于管理端 tokenExpire
//   - 密码哈希 = qq_accounts.json.passwordHash（与游戏内 TShock 账号同一哈希，后端权威）
//   - 只查后端本地台账，不经过插件端
// ═══════════════════════════════════════════════════════════

async function getPlayerTokenExpire() {
  const config = await getConfig()
  return config?.security?.playerTokenExpire || '7d'
}

/**
 * 实时计算玩家投票权重（不进 JWT：时长每 10 分钟聚合刷新，必须实时查）
 * 以当前进行中轮次的加权规则为准（与投票接口同源权威）；无进行中轮次 → 基础权重 1
 */
async function calcPlayerWeight(username) {
  const records = await getPlaytimeRecords()
  let total = 0
  for (const [name, rec] of Object.entries(records)) {
    if (String(name).toLowerCase() === String(username).toLowerCase()) {
      total = Number(rec?.total || 0)
      break
    }
  }
  const hours = total / 60
  let weight = 1
  let weightRules = []
  let baseWeight = 1
  try {
    const openRounds = await listRounds({ includeClosed: false })
    if (openRounds.length > 0) {
      weight = await calcUserWeight(openRounds[0], username)
      weightRules = openRounds[0].weightRules || []
      baseWeight = Number(openRounds[0].baseWeight ?? 1)
    }
  } catch { /* 投票服务异常不影响登录 */ }
  return {
    playtimeMinutes: total,
    playtimeHours: Math.round(hours * 10) / 10,
    weight,
    weightRules,
    baseWeight
  }
}

/**
 * 玩家登录：POST /api/auth/player-login
 * body: { keyId, encryptedPassword, clientPublicKeyPem, account }
 *   account = QQ 号（5-15 位数字）或角色名；只查后端本地 qq_accounts.json
 * 与 admin login 同构：RSA-OAEP 挑战 → 本地 bcrypt 比对 → 签发 JWT（前端公钥加密回传）
 */
export const playerLogin = async (req, res) => {
  const { keyId, encryptedPassword, clientPublicKeyPem, account } = req.body

  if (!keyId || !encryptedPassword || !clientPublicKeyPem || !account) {
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

    const ident = String(account || '').trim()
    // QQ 号（纯数字 5-15 位）优先按 QQ 精确匹配；否则按角色名大小写不敏感匹配
    let rec = /^\d{5,15}$/.test(ident) ? await getAccountByQq(ident) : null
    if (!rec) rec = await getAccountByUsernameCI(ident)

    if (!rec || !rec.passwordHash) {
      // 统一失败语义：账号不存在/密码错误同为 401，不泄露账号存在性
      audit.record('player.login_failed', { username: ident, reason: 'invalid_credentials', ip: req.ip })
      await new Promise(r => setTimeout(r, 500))
      return res.status(401).json({ error: '账号或密码错误' })
    }

    const ok = await bcrypt.compare(String(decryptedPassword), rec.passwordHash)
    if (!ok) {
      audit.record('player.login_failed', { username: rec.username, reason: 'invalid_credentials', ip: req.ip })
      await new Promise(r => setTimeout(r, 500))
      return res.status(401).json({ error: '账号或密码错误' })
    }

    const secret = await getJwtSecret()
    const expire = await getPlayerTokenExpire()
    const token = jwt.sign(
      { username: rec.username, qq: String(rec.qq || ''), usergroup: 'player' },
      secret,
      { expiresIn: expire }
    )

    audit.record('player.login', { username: rec.username, qq: rec.qq, via: 'password', ip: req.ip })

    const player = await calcPlayerWeight(rec.username)
    const payload = {
      username: rec.username,
      qq: String(rec.qq || ''),
      ...player
    }

    try {
      const clientPublicKey = forge.pki.publicKeyFromPem(clientPublicKeyPem)
      const encryptedToken = forge.util.encode64(clientPublicKey.encrypt(token, 'RSA-OAEP'))
      res.json({ success: true, encryptedToken, userGroup: 'player', player: payload })
    } catch (e) {
      res.json({ success: true, token, userGroup: 'player', player: payload })
    }
  } catch (error) {
    console.error('Player login error:', error)
    return res.status(500).json({ error: 'Server error' })
  }
}

/**
 * 玩家自身信息：GET /api/auth/player/me（requirePlayer 已实时校验台账存在性）
 * 返回：username / qq / 累计时长 / 实时权重 / 阈值
 */
export const playerMe = async (req, res) => {
  try {
    const rec = await getAccountByUsernameCI(req.user?.username)
    if (!rec) {
      return res.status(401).json({ error: '账号不存在或已解绑' })
    }
    const player = await calcPlayerWeight(rec.username)
    res.json({ username: rec.username, qq: String(rec.qq || ''), ...player })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

