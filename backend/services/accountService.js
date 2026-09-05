import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import bcrypt from 'bcrypt'
import { fileURLToPath } from 'url'
import { getAccountByUsernameCI } from './qqAccountService.js'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const ACCOUNTS_PATH = path.join(__dirname, '..', 'data', 'accounts.json')

// 角色常量
export const ROLE_ADMIN = 'admin'
export const ROLE_SUBADMIN = 'subadmin'

// 初始管理员用户名（setup 首次初始化 / setup-login JWT 使用；放开后可存在多个 admin）
export const ADMIN_USERNAME = 'admin'

let accounts = null

// ═══════════════════════════════════════════════════════════
// 基础读写
// ═══════════════════════════════════════════════════════════

async function load() {
  if (accounts) return accounts
  try {
    const content = await fs.readFile(ACCOUNTS_PATH, 'utf8')
    accounts = JSON.parse(content)
  } catch {
    accounts = {}
  }
  return accounts
}

async function persist() {
  try {
    await fs.mkdir(path.dirname(ACCOUNTS_PATH), { recursive: true })
    await fs.writeFile(ACCOUNTS_PATH, JSON.stringify(accounts, null, 2), 'utf8')
  } catch (err) {
    throw new Error('无法写入账户文件: ' + err.message)
  }
}

/** 用于首次初始化判断：是否已存在任何账户 */
export async function hasAnyAccount() {
  const acc = await load()
  return Object.keys(acc).length > 0
}

export async function getAccount(username) {
  const acc = await load()
  const key = String(username || '').toLowerCase()
  return acc[key] || null
}

/**
 * 按登录标识查找账户（双标识）：
 *  - 5-15 位纯数字 → 按 QQ 号匹配（台账关联账户）
 *  - 其余 → 按用户名大小写不敏感匹配
 */
export async function findAccountByIdent(ident) {
  const s = String(ident || '').trim()
  if (!s) return null
  const acc = await load()
  if (/^\d{5,15}$/.test(s)) {
    for (const [key, a] of Object.entries(acc)) {
      if (a.qq && String(a.qq) === s) return { key, ...a }
    }
  }
  const key = s.toLowerCase()
  if (acc[key]) return { key, ...acc[key] }
  return null
}

export async function listAccounts() {
  const acc = await load()
  return Object.values(acc)
    .map(a => ({
      username: a.username,
      role: a.role,
      qq: a.qq || '',
      linkedTo: a.linkedTo || '',
      createdAt: a.createdAt,
      updatedAt: a.updatedAt
    }))
    .sort((a, b) => a.createdAt.localeCompare(b.createdAt))
}

/**
 * 创建账户
 * 约束：
 *  - 角色仅 admin / subadmin；不限制 admin 数量（可多管理员）
 *  - linkedTo 模式：从现有 QQ 台账（qq_accounts.json）选取用户授予管理身份，
 *    登录凭证（QQ/用户名 + 密码）以台账为准（实时校验），不由 TShock 权限决定，仅 admin 手动授予
 *  - 普通模式：手动设置用户名 + 密码（bcrypt 落盘）
 */
export async function createAccount(username, password, role, { linkedTo } = null) {
  const targetRole = role === ROLE_ADMIN ? ROLE_ADMIN : ROLE_SUBADMIN
  let name = String(username || '').trim()
  let qq = ''
  let passwordHash = null
  let linked = null

  if (linkedTo) {
    // 台账关联模式：凭证取自现有 QQ 绑定数据
    linked = await getAccountByUsernameCI(linkedTo)
    if (!linked || !linked.passwordHash) {
      throw new Error('QQ 台账中不存在该用户或该用户未设置密码')
    }
    name = name || linked.username
    qq = String(linked.qq || '')
  } else {
    if (!password || String(password).length < 8) {
      throw new Error('密码长度至少 8 位')
    }
    passwordHash = await bcrypt.hash(String(password), 12)
  }

  if (!name) throw new Error('用户名不能为空')

  const acc = await load()
  const key = name.toLowerCase()
  if (acc[key]) {
    throw new Error('用户名已存在')
  }

  const now = new Date().toISOString()
  acc[key] = {
    username: name,
    qq,
    // 台账关联账户不落盘密码哈希：登录时实时读 qq_accounts.json（改密即时同步）
    ...(linked ? { linkedTo: linked.username, passwordHash: undefined } : { passwordHash }),
    role: targetRole,
    createdAt: now,
    updatedAt: now
  }
  await persist()
  return { username: name, role: targetRole, linkedTo: linked ? linked.username : '' }
}

/** 校验密码（登录 / 自助改密旧密码验证），成功返回账户信息 */
export async function verifyAccount(ident, password) {
  const account = await findAccountByIdent(ident)
  if (!account) return null

  // 台账关联账户：密码以 qq_accounts.json 为准（与游戏同源，实时校验）
  let hash = account.passwordHash
  if (account.linkedTo) {
    const linked = await getAccountByUsernameCI(account.linkedTo)
    if (!linked || !linked.passwordHash) return null
    hash = linked.passwordHash
  }
  if (!hash) return null

  const ok = await bcrypt.compare(String(password), hash)
  if (!ok) return null
  return { username: account.username, role: account.role, linkedTo: account.linkedTo }
}

/**
 * 修改密码（自助改密：必须提供旧密码校验；管理员重置走 resetPassword，不经过此方法）
 * 永不返回/记录密码原文
 */
export async function changePassword(username, oldPassword, newPassword) {
  const account = await getAccount(username)
  if (!account) throw new Error('用户不存在')
  if (account.linkedTo) {
    throw new Error('该账户密码由 QQ 绑定数据托管，请通过游戏内或 QQ 渠道修改')
  }

  if (!oldPassword) {
    throw new Error('旧密码为必填')
  }
  const ok = await bcrypt.compare(String(oldPassword), account.passwordHash)
  if (!ok) throw new Error('旧密码错误')

  if (!newPassword || String(newPassword).length < 8) {
    throw new Error('新密码长度至少 8 位')
  }
  if (String(newPassword) === String(oldPassword)) {
    throw new Error('新密码不能与旧密码相同')
  }

  account.passwordHash = await bcrypt.hash(String(newPassword), 12)
  account.updatedAt = new Date().toISOString()
  await persist()
  return { username: account.username, role: account.role }
}

/**
 * 重置密码（管理员/console 重置）：生成随机强密码
 * 返回明文一次（调用方负责显示），落盘的是 bcrypt hash
 */
export async function resetPassword(username) {
  const account = await getAccount(username)
  if (!account) throw new Error('用户不存在')
  if (account.linkedTo) {
    throw new Error('该账户密码由 QQ 绑定数据托管，无法在后端重置，请通过游戏内或 QQ 渠道修改')
  }

  const plain = generateStrongPassword(16)
  account.passwordHash = await bcrypt.hash(plain, 12)
  account.updatedAt = new Date().toISOString()
  await persist()
  return { username: account.username, plainPassword: plain }
}

/** 删除账户。铁律：不允许删除当前登录账户（controller 校验）；允许删除任意其他账户（含其他 admin） */
export async function deleteAccount(username) {
  const account = await getAccount(username)
  if (!account) throw new Error('用户不存在')
  const acc = await load()
  delete acc[String(username).toLowerCase()]
  await persist()
  return { username: account.username, role: account.role }
}

/** 更新角色（admin / subadmin 互转；不允许修改当前登录账户，由 controller 校验） */
export async function updateRole(username, role) {
  if (role !== ROLE_ADMIN && role !== ROLE_SUBADMIN) {
    throw new Error('无效的角色')
  }
  const account = await getAccount(username)
  if (!account) throw new Error('用户不存在')
  const from = account.role
  account.role = role
  account.updatedAt = new Date().toISOString()
  await persist()
  return { username: account.username, from, to: role }
}

/** 生成高强度随机密码（大小写+数字+符号，至少 16 位） */
function generateStrongPassword(length) {
  const upper = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
  const lower = 'abcdefghijklmnopqrstuvwxyz'
  const digits = '0123456789'
  const symbols = '!@#$%^&*-_=+?'
  const all = upper + lower + digits + symbols
  const chars = [
    upper[cryptoRandom(upper.length)],
    lower[cryptoRandom(lower.length)],
    digits[cryptoRandom(digits.length)],
    symbols[cryptoRandom(symbols.length)]
  ]
  while (chars.length < length) {
    chars.push(all[cryptoRandom(all.length)])
  }
  // Fisher-Yates 洗牌
  for (let i = chars.length - 1; i > 0; i--) {
    const j = cryptoRandom(i + 1)
    ;[chars[i], chars[j]] = [chars[j], chars[i]]
  }
  return chars.join('')
}

function cryptoRandom(max) {
  const buf = new Uint32Array(1)
  crypto.getRandomValues(buf)
  return buf[0] % max
}
