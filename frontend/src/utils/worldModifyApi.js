import { apiRequest } from './api.js'

/** 读取当前服务器插件端世界字段状态（fields + meta + groups，数据驱动） */
export const getWorldModifyStatus = async () => {
  const res = await apiRequest('/api/worldmodify/status')
  return res.json()
}

/** 应用字段修改（fields 为 { 字段名: 值 }，插件端广播 WorldInfo 即时生效） */
export const applyWorldModify = async (fields) => {
  const res = await apiRequest('/api/worldmodify/apply', {
    method: 'POST',
    body: JSON.stringify({ fields })
  })
  return res.json()
}
