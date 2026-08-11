import fs from 'fs/promises'
import path from 'path'
import crypto from 'crypto'
import bcrypt from 'bcrypt'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const ACCOUNTS_PATH = path.join(__dirname, '..', 'data', 'accounts.json')

// 角色常量
export const ROLE_ADMIN = 'admin'
export const ROLE_SUBADMIN = 'subadmin'

// 全局唯一 admin 用户名
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

/** 判断全局唯一 admin 是否已存在 */
export async function hasAdmin() {
  const acc = await load()
  return Object.values(acc).some(a => a.role === ROLE_ADMIN)
}

export async function getAccount(username) {
  const acc = await load()
  const key = String(username || '').toLowerCase()
  return acc[key] || null
}

export async function listAccounts() {
  const acc = await load()
  return Object.values(acc)
    .map(a => ({
      username: a.username,
      role: a.role,
      createdAt: a.createdAt,
      updatedAt: a.updatedAt
    }))
    .sort((a, b) => a.createdAt.localeCompare(b.createdAt))
}

/**
 * 创建账户
 * 约束：
 *  - 全局唯一 admin：role=admin 只能创建一次，且用户名强制为 "admin"
 *  - 普通账户 role=subadmin
 */
export async function createAccount(username, password, role) {
  const targetRole = role === ROLE_ADMIN ? ROLE_ADMIN : ROLE_SUBADMIN
  const name = String(username || '').trim()
  const key = name.toLowerCase()

  if (!name) throw new Error('用户名不能为空')
  if (!password || String(password).length < 8) {
    throw new Error('密码长度至少 8 位')
  }

  // 唯一 admin 约束
  if (targetRole === ROLE_ADMIN) {
    if (name !== ADMIN_USERNAME) {
      throw new Error(`唯一管理员用户名必须为 "${ADMIN_USERNAME}"`)
    }
    if (await hasAdmin()) {
      throw new Error('已存在全局唯一管理员，无法创建第二个 admin')
    }
  }

  const acc = await load()
  if (acc[key]) {
    throw new Error('用户名已存在')
  }

  const hash = await bcrypt.hash(String(password), 12)
  const now = new Date().toISOString()
  acc[key] = {
    username: name,
    passwordHash: hash,
    role: targetRole,
    createdAt: now,
    updatedAt: now
  }
  await persist()
  return { username: name, role: targetRole }
}

/** 校验密码（登录 / 自助改密旧密码验证），成功返回账户信息 */
export async function verifyAccount(username, password) {
  const account = await getAccount(username)
  if (!account || !account.passwordHash) return null
  const ok = await bcrypt.compare(String(password), account.passwordHash)
  if (!ok) return null
  return {
    username: account.username,
    role: account.role
  }
}

/**
 * 修改密码（自助改密：必须提供旧密码校验；管理员重置走 resetPassword，不经过此方法）
 * 永不返回/记录密码原文
 */
export async function changePassword(username, oldPassword, newPassword) {
  const account = await getAccount(username)
  if (!account) throw new Error('用户不存在')

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

  const plain = generateStrongPassword(16)
  account.passwordHash = await bcrypt.hash(plain, 12)
  account.updatedAt = new Date().toISOString()
  await persist()
  return { username: account.username, plainPassword: plain }
}

/** 删除账户。铁律：不允许删除全局唯一 admin */
export async function deleteAccount(username) {
  const account = await getAccount(username)
  if (!account) throw new Error('用户不存在')
  if (account.role === ROLE_ADMIN) {
    throw new Error('不允许删除全局唯一管理员')
  }
  const acc = await load()
  delete acc[String(username).toLowerCase()]
  await persist()
  return { username: account.username, role: account.role }
}

/** 更新角色（仅 subadmin 之间/降级可用，不允许提升为 admin 或操作 admin） */
export async function updateRole(username, role) {
  if (role !== ROLE_ADMIN && role !== ROLE_SUBADMIN) {
    throw new Error('无效的角色')
  }
  const account = await getAccount(username)
  if (!account) throw new Error('用户不存在')
  if (account.role === ROLE_ADMIN) {
    throw new Error('不允许修改唯一管理员角色')
  }
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
