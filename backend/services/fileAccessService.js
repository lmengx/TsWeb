import fsSync from 'fs'
import path from 'path'
import crypto from 'crypto'

const FILE_ACCESS_PATH = path.resolve('config/fileAccess.json')

/** @type {{ read: string[], write: string[] } | null} */
let rules = null
let currentHash = null

/** @type {Set<string> | null} 可列出的目录集合 */
let listableDirs = null

function readFileWithHash() {
  const content = fsSync.readFileSync(FILE_ACCESS_PATH, 'utf8')
  const hash = crypto.createHash('sha256').update(content).digest('hex')
  return { content, hash }
}

function reloadIfChanged() {
  try {
    if (!fsSync.existsSync(FILE_ACCESS_PATH)) return
    const { content, hash } = readFileWithHash()
    if (hash !== currentHash) {
      rules = JSON.parse(content)
      currentHash = hash
      rebuildListableDirs()
      console.log(`[FileAccess] 白名单已变更 (${hash.slice(0, 8)}...)，已重新加载`)
    }
  } catch (e) {
    console.error('[FileAccess] 热重载失败:', e.message)
  }
}

export async function loadRules() {
  if (!rules) {
    const { content, hash } = readFileWithHash()
    rules = JSON.parse(content)
    currentHash = hash
    rebuildListableDirs()
  }
  return rules
}

/**
 * 从 read/write 中的明确路径自动计算出哪些目录是可浏览的
 */
function rebuildListableDirs() {
  listableDirs = new Set()

  const allPaths = [...(rules.read || []), ...(rules.write || [])]
  for (const filePath of allPaths) {
    // 标准化路径：统一正斜杠，去除头尾空格
    let normalized = filePath.trim().replace(/\\/g, '/')
    // 跳过空路径或已经是目录的路径
    if (!normalized || normalized.endsWith('/')) continue

    // 逐级提取父目录
    const parts = normalized.split('/')
    for (let i = 1; i < parts.length; i++) {
      listableDirs.add(parts.slice(0, i).join('/') + '/')
    }
  }
}

function isListable(dirPath) {
  if (!listableDirs) return false
  const normalized = dirPath.replace(/\\/g, '/').replace(/\/?$/, '/')
  return listableDirs.has(normalized)
}

/**
 * 精确匹配：路径必须与规则完全一致（不允许通配符）
 */
function matchExact(rulePath, targetPath) {
  return rulePath === targetPath
}

/**
 * 校验相对路径是否有指定操作权限
 */
export function checkPermission(relativePath, operation) {
  reloadIfChanged()
  if (!rules) throw new Error('fileAccess rules not loaded')

  // 1. 拒绝空/绝对/含 .. 或 . 的路径
  if (!relativePath || relativePath === '/' || relativePath === '.') return false
  if (relativePath.startsWith('/') || /^[A-Za-z]:[/\\]/.test(relativePath)) return false

  let normalized = relativePath.replace(/\\/g, '/')
  const parts = normalized.split('/')
  for (const part of parts) {
    if (part === '..' || part === '.') return false
  }
  normalized = parts.join('/')
  if (!normalized) return false

  // 2. 禁止读取 fileAccess.json 自身
  if (normalized === 'config/fileAccess.json' || normalized.endsWith('/config/fileAccess.json')) return false

  // 3. 先检查 deny 黑名单
  for (const rule of (rules.deny || [])) {
    if (matchExact(rule, normalized)) return false
  }

  // 4. list 权限自动计算
  if (operation === 'list') return isListable(normalized)

  // 5. write 操作：只匹配 write 规则
  if (operation === 'write') {
    for (const rule of (rules.write || [])) {
      if (matchExact(rule, normalized)) return true
    }
    return false
  }

  // 6. read 操作：匹配 read 规则 + write 规则（write 隐式包含 read）
  for (const rule of (rules.read || [])) {
    if (matchExact(rule, normalized)) return true
  }
  for (const rule of (rules.write || [])) {
    if (matchExact(rule, normalized)) return true
  }

  return false
}

/**
 * 获取前端可浏览的基础目录结构
 */
export function getAllowedTree() {
  reloadIfChanged()
  if (!listableDirs) return []

  const dirSet = new Set()
  for (const dirPath of listableDirs) {
    const first = dirPath.split('/')[0]
    if (first) dirSet.add(first)
  }

  const result = []
  for (const name of dirSet) {
    result.push({ name, type: 'dir', children: [] })
  }
  result.sort((a, b) => a.name.localeCompare(b.name))
  return result
}

/**
 * 过滤目录列表结果
 */
export function filterListResult(entries, dirPath) {
  reloadIfChanged()
  if (!rules) return []

  const normalizedDir = dirPath.replace(/\\/g, '/').replace(/\/?$/, '/')

  return entries.filter(entry => {
    const fullPath = normalizedDir + entry.name
    if (entry.type === 'dir') {
      return isListable(fullPath + '/')
    }
    // 文件：read 或 write 中有精确匹配
    return (rules.read || []).includes(fullPath) || (rules.write || []).includes(fullPath)
  })
}
