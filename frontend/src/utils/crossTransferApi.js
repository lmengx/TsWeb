import { apiRequest } from './api.js'

/** 读取当前服务器插件端的跨服传送配置（中文键原始对象，含明文密钥） */
export const getCrossTransferConfig = async () => {
  const res = await apiRequest('/api/crosstransfer/config')
  return res.json()
}

/** 保存配置：前端表单（英文键）→ 后端转插件格式 → 写入插件端热应用 */
export const saveCrossTransferConfig = async (config) => {
  const res = await apiRequest('/api/crosstransfer/config', {
    method: 'POST',
    body: JSON.stringify(config)
  })
  return res.json()
}

/** 探测目标服可达性（插件端实际 TCP 探测） */
export const probeCrossTransfer = async (targets) => {
  const res = await apiRequest('/api/crosstransfer/probe', {
    method: 'POST',
    body: JSON.stringify({ targets })
  })
  return res.json()
}
