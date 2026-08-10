import { apiRequest } from './api.js'
import { getCurrentServerId } from './serverStore.js'

const BASE = '/api/files'

/** 文本预览扩展名白名单 */
export const TEXT_EXTENSIONS = new Set([
  'txt', 'log', 'json', 'yml', 'yaml', 'xml', 'cfg', 'ini', 'conf',
  'md', 'markdown', 'cs', 'js', 'ts', 'tsx', 'jsx', 'vue', 'json5',
  'lua', 'sql', 'csv', 'properties', 'env', 'bat', 'sh', 'ps1',
  'html', 'css', 'gitignore', 'editorconfig', 'yml.template'
])

export function isTextFile(name) {
  const dot = String(name || '').lastIndexOf('.')
  if (dot < 0) return false
  const ext = name.slice(dot + 1).toLowerCase()
  return TEXT_EXTENSIONS.has(ext)
}

function joinPath(dir, name) {
  const d = String(dir || '').replace(/\/+$/, '')
  return d ? `${d}/${name}` : name
}

async function ensureOk(res) {
  if (!res.ok) {
    let msg = `HTTP ${res.status}`
    try {
      const j = await res.json()
      if (j?.error) msg = j.error
    } catch { /* ignore */ }
    throw new Error(msg)
  }
  return res
}

// ── 目录与文本 ──

export async function listDir(dirPath) {
  const res = await ensureOk(await apiRequest(`${BASE}/list?path=${encodeURIComponent(dirPath || '')}`, { method: 'GET' }))
  return res.json()
}

export async function readFile(filePath) {
  const res = await ensureOk(await apiRequest(`${BASE}/read?path=${encodeURIComponent(filePath)}`, { method: 'GET' }))
  return res.json()
}

export async function writeFile(filePath, content) {
  const res = await ensureOk(await apiRequest(`${BASE}/write`, {
    method: 'POST',
    body: JSON.stringify({ path: filePath, content })
  }))
  return res.json()
}

// ── 删除 ──

export async function deleteFile(filePath) {
  const res = await ensureOk(await apiRequest(`${BASE}/delete`, {
    method: 'POST',
    body: JSON.stringify({ path: filePath })
  }))
  return res.json()
}

// ── 上传（分片 base64） ──

const UPLOAD_CHUNK_BYTES = 4 * 1024 * 1024 // 4MB 二进制/片（base64 后 ~5.4MB，在 10MB body 限制内）

function blobToBase64(blob) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      const dataUrl = String(reader.result || '')
      resolve(dataUrl.includes(',') ? dataUrl.split(',')[1] : dataUrl)
    }
    reader.onerror = () => reject(reader.error)
    reader.readAsDataURL(blob)
  })
}

/**
 * 分片上传文件到目标路径
 * @param {string} dirPath 目标目录（相对 TShock 程序目录）
 * @param {File} file 文件对象
 * @param {(info: {sent:number,total:number,chunk:number,totalChunks:number}) => void} [onProgress]
 */
export async function uploadFile(dirPath, file, onProgress) {
  const targetPath = joinPath(dirPath, file.name)
  const total = Math.max(1, Math.ceil(file.size / UPLOAD_CHUNK_BYTES))
  let sent = 0
  for (let i = 0; i < total; i++) {
    const start = i * UPLOAD_CHUNK_BYTES
    const blob = file.slice(start, start + UPLOAD_CHUNK_BYTES)
    const b64 = await blobToBase64(blob)
    const res = await ensureOk(await apiRequest(`${BASE}/upload`, {
      method: 'POST',
      body: JSON.stringify({ path: targetPath, data: b64, append: i > 0 })
    }))
    const json = await res.json()
    if (json.error) throw new Error(json.error)
    sent += blob.size
    if (onProgress) onProgress({ sent, total: file.size, chunk: i + 1, totalChunks: total })
  }
  return { path: targetPath, size: file.size }
}

// ── 下载（SSE 实时回传，不经后端磁盘） ──

function b64ToUint8(b64) {
  const bin = atob(b64)
  const bytes = new Uint8Array(bin.length)
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i)
  return bytes
}

function fileNameFromPath(p) {
  const parts = String(p || '').split('/')
  return parts[parts.length - 1] || 'download'
}

/**
 * 通过 SSE 下载文件（fetch 流式读取后端 /api/files/download，实时转发插件推送）
 * @param {string} filePath 相对路径
 * @param {(info: {type:string,received:number,size:number,percent:number}) => void} [onProgress]
 * @returns {Promise<{blob:Blob, name:string, size:number}>}
 */
export async function downloadFile(filePath, onProgress) {
  const user = localStorage.getItem('user')
  let token = null
  if (user) {
    try { token = JSON.parse(user).token } catch { /* ignore */ }
  }
  const serverId = getCurrentServerId()
  const headers = {}
  if (token) headers['Authorization'] = `Bearer ${token}`
  if (serverId) headers['x-server-id'] = serverId

  const res = await fetch(`${BASE}/download?path=${encodeURIComponent(filePath)}`, { headers })
  if (!res.ok || !res.body) {
    let msg = `HTTP ${res.status}`
    try {
      const j = await res.json()
      if (j?.error) msg = j.error
    } catch { /* ignore */ }
    throw new Error(msg)
  }

  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  let meta = null
  let chunks = []
  let receivedBytes = 0
  let finished = false

  // 收到 file.end 即表示全部 chunk 已推送完毕，直接结束读取，
  // 不依赖流的关闭信号（Vite 开发代理下后端 res.end() 的 FIN 可能延迟/丢失）
  while (!finished) {
    const { done, value } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })
    let idx
    while (!finished && (idx = buffer.indexOf('\n\n')) >= 0) {
      const frame = buffer.slice(0, idx)
      buffer = buffer.slice(idx + 2)
      let event = 'message'
      let data = ''
      for (const line of frame.split('\n')) {
        if (line.startsWith('event:')) event = line.slice(6).trim()
        else if (line.startsWith('data:')) data += line.slice(5).replace(/^\s+/, '') + '\n'
      }
      data = data.replace(/\n$/, '')
      if (!data) continue
      let parsed
      try { parsed = JSON.parse(data) } catch { continue }

      if (event === 'file.begin') {
        meta = parsed
        chunks = new Array(parsed.chunks || 0).fill(null)
        receivedBytes = 0
        if (onProgress) onProgress({ type: 'begin', received: 0, size: parsed.size || 0, percent: 0 })
      } else if (event === 'file.chunk') {
        if (parsed.n != null && parsed.n < chunks.length) chunks[parsed.n] = parsed.d
        receivedBytes += Math.ceil(((parsed.d || '').length * 3) / 4)
        const size = meta?.size || 0
        if (onProgress) onProgress({
          type: 'chunk',
          received: Math.min(receivedBytes, size),
          size,
          percent: size > 0 ? Math.min(100, Math.round((receivedBytes / size) * 100)) : 0
        })
      } else if (event === 'file.error') {
        throw new Error(parsed.reason || '下载失败')
      } else if (event === 'file.end') {
        meta = { ...(meta || {}), sha256: parsed.sha256 }
        finished = true
      }
    }
  }

  if (!meta) throw new Error('下载未收到文件信息')
  const parts = chunks.map(d => (d ? b64ToUint8(d) : new Uint8Array(0)))
  const blob = new Blob(parts, { type: 'application/octet-stream' })
  const name = fileNameFromPath(meta.name || filePath)
  return { blob, name, size: blob.size }
}

/** 触发浏览器保存 Blob */
export function saveBlob(blob, name) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 4000)
}

// ── 保存到后端（data/transfer） ──

/** 经 SSE 拉取文件并保存到后端转存目录 */
export async function saveToBackend(filePath) {
  const res = await ensureOk(await apiRequest(`${BASE}/save`, {
    method: 'POST',
    body: JSON.stringify({ path: filePath })
  }))
  const json = await res.json()
  if (json.error) throw new Error(json.error)
  return json
}

/** 列出已保存到后端的文件 */
export async function listSavedFiles() {
  const res = await ensureOk(await apiRequest(`${BASE}/saved`, { method: 'GET' }))
  const json = await res.json()
  return json.files || []
}

/** 下载已保存到后端的文件（普通 HTTP，非 SSE） */
export async function downloadSavedFile(name) {
  const user = localStorage.getItem('user')
  let token = null
  if (user) {
    try { token = JSON.parse(user).token } catch { /* ignore */ }
  }
  const serverId = getCurrentServerId()
  const headers = {}
  if (token) headers['Authorization'] = `Bearer ${token}`
  if (serverId) headers['x-server-id'] = serverId

  const res = await fetch(`${BASE}/saved/download?name=${encodeURIComponent(name)}`, { headers })
  if (!res.ok) {
    let msg = `HTTP ${res.status}`
    try {
      const j = await res.json()
      if (j?.error) msg = j.error
    } catch { /* ignore */ }
    throw new Error(msg)
  }
  const blob = await res.blob()
  saveBlob(blob, name)
}

/** 删除已保存到后端的文件 */
export async function deleteSavedFile(name) {
  const res = await ensureOk(await apiRequest(`${BASE}/saved/delete`, {
    method: 'POST',
    body: JSON.stringify({ name })
  }))
  const json = await res.json()
  if (json.error) throw new Error(json.error)
  return json
}

/** 格式化文件大小 */
export function formatSize(bytes) {
  if (bytes == null) return '-'
  const n = Number(bytes)
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} MB`
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} GB`
}
