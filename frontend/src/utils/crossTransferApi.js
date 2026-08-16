import { apiRequest } from './api.js'

/** 读取跨服传送配置：{ configured: 脱敏后端配置, pluginCurrent: 插件端现有配置|null } */
export const getCrossTransferConfig = async () => {
  const res = await apiRequest('/api/crosstransfer/config')
  return res.json()
}

/** 保存草稿到后端（不下发插件端） */
export const saveCrossTransferConfig = async (config) => {
  const res = await apiRequest('/api/crosstransfer/config', {
    method: 'POST',
    body: JSON.stringify(config)
  })
  return res.json()
}

/** 获取本服密钥明文（审计记录） */
export const revealSelfSecret = async () => {
  const res = await apiRequest('/api/crosstransfer/reveal', {
    method: 'POST',
    body: '{}'
  })
  return res.json()
}

/** 探测目标服可达性（插件端实际 TCP 探测，专线优先） */
export const probeCrossTransfer = async (targets) => {
  const res = await apiRequest('/api/crosstransfer/probe', {
    method: 'POST',
    body: JSON.stringify({ targets })
  })
  return res.json()
}

/** 确认并下发：后端构建插件配置（自动配对密钥）→ 写插件端 → 存后端 */
export const applyCrossTransfer = async (config) => {
  const res = await apiRequest('/api/crosstransfer/apply', {
    method: 'POST',
    body: JSON.stringify(config)
  })
  return res.json()
}
