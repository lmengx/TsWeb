import { getConfig } from '../config.js'

let baseUrl = null
let apiKey = ''

const ensureInit = async () => {
  if (!baseUrl) {
    const config = await getConfig()
    const host = config.tshock?.host || 'localhost'
    const tshockHost = host.startsWith('http://') || host.startsWith('https://') ? host : `http://${host}`
    baseUrl = `${tshockHost}:${config.tshock?.port || 7878}`
    apiKey = config.tshock?.apiKey || ''
  }
}

const tshockFetch = async (path, method = 'GET') => {
  await ensureInit()
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
  const { nickname, reason } = req.body
  if (!nickname) return res.status(400).json({ error: 'nickname is required' })
  let path = `/data/users/unverified/ban?nickname=${encodeURIComponent(nickname)}`
  if (reason) path += `&reason=${encodeURIComponent(reason)}`
  const result = await tshockFetch(path)
  res.json(result)
}
