import { get } from '../utils/api.js'

// House 房屋系统 API（经后端代理转发到 TShock /data/house/* 与 /data/buildings/*）

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
