import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'
import tshockService, { getCurrentServer } from './tshockService.js'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const defaultsDir = path.join(__dirname, '../resources/默认配置')

const DEFAULT_FILES = {
  item: '物品违禁.json',
  proj: '弹幕违禁.json'
}

/** 读取后端内置默认配置（未启用状态，作为模板） */
export function getDefaultConfig(kind) {
  const file = DEFAULT_FILES[kind]
  if (!file) throw new Error(`未知配置类型: ${kind}`)
  const filePath = path.join(defaultsDir, file)
  if (!fs.existsSync(filePath)) {
    throw new Error(`默认配置缺失: ${filePath}`)
  }
  return JSON.parse(fs.readFileSync(filePath, 'utf8'))
}

/** 读取后端内置默认配置并将 启用 置为 true（下发时启用） */
export function getDefaultConfigEnabled(kind) {
  const config = getDefaultConfig(kind)
  config['启用'] = true
  return config
}

/** 判断插件端当前配置是否「无有效限制」（无任何进度组或所有组均为空） */
export async function hasEffectiveConfig(kind) {
  const data = kind === 'item'
    ? await tshockService.getItemConfig()
    : await tshockService.getProjConfig()

  if (!data || data.error) {
    // 插件不可达/无配置 → 视为无配置
    return false
  }
  // 兼容后端透传包装 { config: {...} }
  const config = data.config && !Array.isArray(data.config) ? data.config : data
  const list = config['限制列表'] ?? config.restrictions ?? []
  if (!Array.isArray(list) || list.length === 0) return false
  // 所有进度组的限制均为空 → 视为无配置
  const key = kind === 'item' ? '限制物品' : '限制弹幕'
  return list.some(group => {
    const items = group?.[key] ?? group?.items ?? group?.projectiles ?? []
    return Array.isArray(items) && items.length > 0
  })
}

/**
 * 将后端默认配置下发到插件端（启用=true）。
 * 仅当插件端无有效限制时执行；已有配置则不覆盖（返回 skipped）。
 */
export async function pushDefaultToPlugin(kind) {
  const server = getCurrentServer()
  if (!server || !server.id) {
    return { status: 'error', error: '当前服务器未连接或未配置（缺少 x-server-id）' }
  }

  const alreadyConfigured = await hasEffectiveConfig(kind)
  if (alreadyConfigured) {
    return { status: 'skipped', message: '插件端已有配置，未覆盖' }
  }

  const defaultConfig = getDefaultConfigEnabled(kind)
  const result = kind === 'item'
    ? await tshockService.saveItemConfig(defaultConfig)
    : await tshockService.saveProjConfig(defaultConfig)

  if (result && result.error) {
    return { status: 'error', error: result.error }
  }
  return { status: 'ok', message: '默认配置已下发', enabled: true }
}

/**
 * 打开反作弊统一入口：
 *  - 插件端无有效配置 → 下发后端默认配置（启用=true）
 *  - 插件端已有配置 → 仅翻转启用开关（调用插件 /data/anticheat/enable）
 */
export async function enableAntiCheat(kind) {
  const server = getCurrentServer()
  if (!server || !server.id) {
    return { status: 'error', error: '当前服务器未连接或未配置（缺少 x-server-id）' }
  }

  const alreadyConfigured = await hasEffectiveConfig(kind)
  if (!alreadyConfigured) {
    const pushed = await pushDefaultToPlugin(kind)
    return pushed
  }

  // 已有配置 → 仅启用开关
  const params = kind === 'item' ? 'itemEnabled=true' : 'projEnabled=true'
  const result = await tshockService.setAntiCheatEnabled(params)
  if (result && result.error) {
    return { status: 'error', error: result.error }
  }
  return { status: 'ok', message: '已启用现有配置', enabled: true }
}
