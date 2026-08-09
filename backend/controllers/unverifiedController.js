import { getCurrentServer } from '../services/tshockService.js'

// 从请求级上下文获取当前目标服务器（由 x-server-id header 决定）
const getEndpoint = () => {
  const server = getCurrentServer()
  if (!server) return { baseUrl: null, apiKey: '' }
  return { baseUrl: server.baseUrl, apiKey: server.apiKey }
}

const tshockFetch = async (path, method = 'GET') => {
  const { baseUrl, apiKey } = getEndpoint()
  if (!baseUrl) return { error: '当前服务器未配置或未选择' }
  const sep = path.includes('?') ? '&' : '?'
  const url = `${baseUrl}${path}${sep}token=${encodeURIComponent(apiKey)}`
  const response = await fetch(url, { method })
  const text = await response.text()
  try { return JSON.parse(text) } catch { return { error: 'Invalid JSON', raw: text } }
}

export const list = async (req, res) => {
  const result = await tshockFetch('/data/users/unverified/list')
  res.json(result)
}

export const detail = async (req, res) => {
  const { nickname } = req.query
  if (!nickname) return res.status(400).json({ error: 'nickname is required' })
  const result = await tshockFetch(`/data/users/unverified/detail?nickname=${encodeURIComponent(nickname)}`)
  res.json(result)
}

export const register = async (req, res) => {
  const { nickname, password, group } = req.body
  if (!nickname || !password) return res.status(400).json({ error: 'nickname and password are required' })
  let path = `/data/users/unverified/register?nickname=${encodeURIComponent(nickname)}&password=${encodeURIComponent(password)}`
  if (group) path += `&group=${encodeURIComponent(group)}`
  const result = await tshockFetch(path)
  res.json(result)
}

export const forceLogin = async (req, res) => {
  const { nickname } = req.body
  if (!nickname) return res.status(400).json({ error: 'nickname is required' })
  const result = await tshockFetch(`/data/users/unverified/force-login?nickname=${encodeURIComponent(nickname)}`)
  res.json(result)
}

export const kick = async (req, res) => {
  const { nickname, reason } = req.body
  if (!nickname) return res.status(400).json({ error: 'nickname is required' })
  let path = `/data/users/unverified/kick?nickname=${encodeURIComponent(nickname)}`
  if (reason) path += `&reason=${encodeURIComponent(reason)}`
  const result = await tshockFetch(path)
  res.json(result)
}

export const ban = async (req, res) => {
  const { nickname, reason, character } = req.body
  if (!nickname) return res.status(400).json({ error: 'nickname is required' })
  let path = `/data/users/unverified/ban?nickname=${encodeURIComponent(nickname)}`
  if (reason) path += `&reason=${encodeURIComponent(reason)}`
  if (character) path += `&character=${encodeURIComponent(character)}`
  const result = await tshockFetch(path)
  res.json(result)
}
