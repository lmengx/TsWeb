import { get, post } from '../utils/api.js'

// 自动任务 API（通过后端代理转发到 TShock /data/tasks/*）

export async function listTasks() {
  const res = await get('/api/tshock/data/tasks/list')
  return res.json()
}

export async function getTask(id) {
  const res = await get(`/api/tshock/data/tasks/get?id=${encodeURIComponent(id)}`)
  return res.json()
}

export async function saveTask(task) {
  const res = await post('/api/tshock/data/tasks/save', { task: JSON.stringify(task) })
  return res.json()
}

export async function deleteTask(id) {
  const res = await post(`/api/tshock/data/tasks/delete?id=${encodeURIComponent(id)}`)
  return res.json()
}

export async function runTask(id, force = false) {
  const res = await post(`/api/tshock/data/tasks/run?id=${encodeURIComponent(id)}&force=${force ? 1 : 0}`)
  return res.json()
}

export async function listTaskLogs(taskId = '', page = 1, pageSize = 20) {
  let url = '/api/tshock/data/tasks/log?page=' + page + '&pageSize=' + pageSize
  if (taskId) url += '&taskId=' + encodeURIComponent(taskId)
  const res = await get(url)
  return res.json()
}

export async function getTaskLogDetail(id) {
  const res = await get(`/api/tshock/data/tasks/log/detail?id=${encodeURIComponent(id)}`)
  return res.json()
}

// BOSS 名称列表（用于条件选择器）
export const BOSS_NAMES = [
  '史莱姆王', '克苏鲁之眼', '世界吞噬者', '克苏鲁之脑', '蜂后', '巨鹿',
  '骷髅王', '血肉墙', '史莱姆皇后', '毁灭者', '机械骷髅王', '双子魔眼',
  '世纪之花', '石巨人', '猪龙鱼公爵', '光之女皇', '拜月教教徒', '月亮领主'
]
