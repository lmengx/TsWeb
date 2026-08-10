import { get, post } from '../utils/api.js'

// House 房屋系统 API（经后端代理转发到 TShock /data/house/* 与 /data/buildings/*）
// 建筑存档：插件本地 TSWeb/Buildings/ 经 /api/tshock/data/buildings/*；
//          后端 data/transfer/building/ 经 /api/buildings/*

export async function listHouses(page = 1, pageSize = 20) {
  const res = await get(`/api/tshock/data/house/list?page=${page}&pageSize=${pageSize}`)
  return res.json()
}

export async function listBuildings(page = 1, pageSize = 20) {
  const res = await get(`/api/tshock/data/buildings/list?page=${page}&pageSize=${pageSize}`)
  return res.json()
}

export async function getBuildingInfo(file) {
  const res = await get(`/api/tshock/data/buildings/info?file=${encodeURIComponent(file)}`)
  return res.json()
}

// ═══════════════ 建筑导入导出 ═══════════════

// 房屋导出到插件本地 TSWeb/Buildings/
export async function exportBuildingToLocal(house) {
  const res = await post('/api/tshock/data/buildings/export', { house })
  return res.json()
}

// 房屋直接导出到后端 data/transfer/building/（不保留插件本地副本）
export async function exportBuildingToBackend(house) {
  const res = await post('/api/buildings/export-to-backend', { house })
  return res.json()
}

// 在线玩家坐标列表（锚点选择）
export async function getOnlinePlayers() {
  const res = await get('/api/tshock/data/buildings/online-players')
  return res.json()
}

// 删除插件本地 .tsb
export async function deleteLocalBuilding(file) {
  const res = await post('/api/tshock/data/buildings/delete-local', { file })
  return res.json()
}

// ═══════════════ 后端建筑存档 data/transfer/building/ ═══════════════

export async function listBackendBuildings() {
  const res = await get('/api/buildings/list')
  return res.json()
}

// 插件本地 .tsb 发送到后端（不保留本地副本）
export async function sendBuildingToBackend(file) {
  const res = await post('/api/buildings/send', { file })
  return res.json()
}

// 后端 .tsb 上传到插件 TSWeb/Buildings/（导入前置）
export async function uploadBuildingToPlugin(file) {
  const res = await post('/api/buildings/upload', { file })
  return res.json()
}

// 导入到世界（插件端执行）
// payload: { file, anchor: 'player'|'coords'|'house', anchorPlayer?, anchorHouse?, coords?, align }
export async function importBuildingToWorld(payload) {
  const res = await post('/api/buildings/import', payload)
  return res.json()
}

// 删除后端 .tsb
export async function deleteBackendBuilding(file) {
  const res = await post('/api/buildings/delete', { file })
  return res.json()
}

// 下载后端 .tsb 到浏览器
export async function downloadBackendBuilding(file) {
  const res = await get(`/api/buildings/download?file=${encodeURIComponent(file)}`)
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err.error || '下载失败')
  }
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = file
  a.click()
  URL.revokeObjectURL(url)
}
