<script setup>
import { ref, onMounted, onUnmounted, nextTick } from 'vue'
import { getCurrentServerId } from '../utils/serverStore.js'

const logs = ref([])
const inputCmd = ref('')
const logContainer = ref(null)
const connected = ref(false)

let abortController = null
let reconnectAttempts = 0
let reconnectTimer = null
const MAX_RECONNECT = 20
const MAX_LOG = 200

// ── ConsoleColor → CSS 颜色映射 ──
const colorMap = {
  Black: '#1a1a1a',
  DarkBlue: '#3355cc',
  DarkGreen: '#339933',
  DarkCyan: '#339999',
  DarkRed: '#cc3333',
  DarkMagenta: '#993399',
  DarkYellow: '#b8860b',
  Gray: '#cccccc',
  DarkGray: '#666666',
  Blue: '#4488ff',
  Green: '#44cc44',
  Cyan: '#44cccc',
  Red: '#ff4444',
  Magenta: '#ff44ff',
  Yellow: '#ffcc00',
  White: '#ffffff',
}

function getToken() {
  try {
    const user = localStorage.getItem('user')
    if (user) return JSON.parse(user).token
  } catch {}
  return ''
}

onMounted(() => {
  connectSSE()
  // 多服：切换目标服务器时重建 SSE 连接并清空旧日志
  window.addEventListener('server-changed', onServerChanged)
})

onUnmounted(() => {
  window.removeEventListener('server-changed', onServerChanged)
  closeSSE()
})

function closeSSE() {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer)
    reconnectTimer = null
  }
  if (abortController) {
    try { abortController.abort() } catch {}
    abortController = null
  }
}

function onServerChanged() {
  closeSSE()
  logs.value = []
  reconnectAttempts = 0
  connectSSE()
}

/**
 * 建立日志流连接。
 *
 * 凭据只走 Authorization 头，绝不放进 URL：token 出现在 query 会留在浏览器历史、
 * 代理访问日志与后端请求日志中，属凭据泄漏。
 * 代价是浏览器原生 EventSource 无法使用（它不支持自定义请求头），故改用
 * fetch + ReadableStream 手工解析 SSE 帧；消息解析逻辑与原先完全一致。
 */
async function connectSSE() {
  const token = getToken()
  if (!token) return

  // 多服：流必须绑定当前目标服务器（serverId 非敏感，仍走 query）
  const serverId = getCurrentServerId() || ''
  if (!serverId) {
    // 未选择服务器时不发起连接，避免后端 400 触发无意义的重连循环
    connected.value = false
    return
  }

  const ctrl = new AbortController()
  abortController = ctrl

  let res
  try {
    res = await fetch(`/api/online/log/stream?serverId=${encodeURIComponent(serverId)}`, {
      headers: { 'Authorization': `Bearer ${token}` },
      signal: ctrl.signal
    })
  } catch {
    // 网络失败，或连接已被主动关闭
    if (!ctrl.signal.aborted) scheduleReconnect()
    return
  }

  // 凭据无效：重连也不会变有效，直接停止，避免无意义轮询
  if (res.status === 401 || res.status === 403) {
    connected.value = false
    return
  }
  if (!res.ok || !res.body) {
    scheduleReconnect()
    return
  }

  connected.value = true
  reconnectAttempts = 0

  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  try {
    for (;;) {
      const { done, value } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true })

      // SSE 以空行分隔帧，逐帧取出后交给 handleSseMessage
      let sep
      while ((sep = buffer.indexOf('\n\n')) >= 0) {
        const frame = buffer.slice(0, sep)
        buffer = buffer.slice(sep + 2)
        handleSseMessage({ data: parseSseData(frame) })
      }
    }
  } catch {
    // 读取中断（含 abort 导致的取消）
  }

  connected.value = false

  // 流自然结束或被中断：按退避重连
  if (!ctrl.signal.aborted) scheduleReconnect()
}

/** 取出一个 SSE 帧中的 data 字段（多行 data 按规范以换行拼接） */
function parseSseData(frame) {
  const parts = []
  for (const line of frame.split('\n')) {
    if (line.startsWith('data:')) parts.push(line.slice(5).replace(/^ /, ''))
  }
  return parts.join('\n')
}

/** 退避重连：与原先 EventSource.onerror 的行为保持一致 */
function scheduleReconnect() {
  connected.value = false
  reconnectAttempts++
  if (reconnectAttempts <= MAX_RECONNECT) {
    const delay = Math.min(2000 * Math.pow(1.5, reconnectAttempts - 1), 60000)
    reconnectTimer = setTimeout(() => connectSSE(), delay)
  }
}

/** 处理一条日志流消息（解析逻辑与原 onmessage 完全一致） */
function handleSseMessage(e) {
  try {
    const data = JSON.parse(e.data)
    if (data.connected) return
    if (Array.isArray(data)) {
      data.forEach(line => {
        let segments = []
        let time = new Date().toLocaleTimeString('zh-CN', { hour12: false })
        let lineId = Date.now() + Math.random()
        try {
          let parsed = line
          if (typeof line === 'string') parsed = JSON.parse(line)
          if (Array.isArray(parsed)) {
            // 旧格式：[{t,c}]
            segments = parsed
          } else if (parsed && Array.isArray(parsed.segments)) {
            // 新格式：{ id, time, segments }
            segments = parsed.segments
            if (parsed.time) {
              const d = new Date(parsed.time)
              if (!isNaN(d.getTime())) time = d.toLocaleTimeString('zh-CN', { hour12: false })
            }
            if (parsed.id != null) lineId = parsed.id
          } else if (parsed && typeof parsed === 'object') {
            segments = [{ t: JSON.stringify(parsed), c: null }]
          } else {
            segments = [{ t: String(line), c: null }]
          }
        } catch {
          segments = [{ t: String(line), c: null }]
        }

        logs.value.push({
          id: lineId,
          type: 'line',
          segments,
          time
        })
      })
      if (logs.value.length > MAX_LOG) {
        logs.value = logs.value.slice(-MAX_LOG)
      }
      scrollToBottom()
    }
  } catch {}
}

async function sendCommand() {
  const raw = inputCmd.value.trim()
  if (!raw) return

  const cmd = raw.startsWith('/') ? raw : '/' + raw
  inputCmd.value = ''

  if (connected.value) {
    // ── SSE 模式：手动回显命令，输出走 SSE 流 ──
    logs.value.push({
      id: Date.now(),
      type: 'line',
      segments: [{ t: `> ${cmd}`, c: 'Green' }],
      time: new Date().toLocaleTimeString('zh-CN', { hour12: false })
    })
  } else {
    // ── 非 SSE 模式：以命令块展示，直接取 API 返回结果 ──
    const blockId = Date.now()
    logs.value.push({
      id: blockId,
      type: 'cmd-block',
      cmd,
      output: '',
      time: new Date().toLocaleTimeString('zh-CN', { hour12: false })
    })
  }

  scrollToBottom()

  try {
    const token = getToken()
    let username = 'SSE-Console'
    try {
      const user = localStorage.getItem('user')
      if (user) {
        const parsed = JSON.parse(user)
        username = parsed.username || parsed.name || 'SSE-Console'
      }
    } catch {}
    const res = await fetch('/api/online/log/command', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`,
        'x-server-id': getCurrentServerId() || ''
      },
      body: JSON.stringify({ cmd, executor: username })
    })

    if (!connected.value) {
      // 非 SSE 模式：取回结果填入命令块
      const data = await res.json()
      const block = logs.value.find(l => l.type === 'cmd-block' && l.cmd === cmd)
      // 只更新最新同命令的块（防止历史块被误改）
      const lastBlock = logs.value.filter(l => l.type === 'cmd-block' && l.cmd === cmd).pop()
      if (lastBlock) {
        const output = data.response || data.error || JSON.stringify(data)
        lastBlock.output = output
        scrollToBottom()
      }
    }
  } catch (err) {
    if (!connected.value) {
      const lastBlock = logs.value.filter(l => l.type === 'cmd-block' && l.cmd === cmd).pop()
      if (lastBlock) {
        lastBlock.output = `[错误] ${err.message}`
        scrollToBottom()
      }
    }
  }
}

function scrollToBottom() {
  nextTick(() => {
    if (logContainer.value) {
      logContainer.value.scrollTop = logContainer.value.scrollHeight
    }
  })
}

function onInputKeydown(e) {
  if (e.key === 'Enter') {
    sendCommand()
  }
}
</script>

<template>
  <div class="terminal-card">
    <div class="terminal-header">
      <span class="terminal-title">
        <span class="status-dot" :class="{ connected }"></span>
        服务器控制台
      </span>
      <span class="log-count">{{ logs.length }} 条</span>
    </div>
    <div class="terminal-body" ref="logContainer">
      <div v-if="logs.length === 0" class="terminal-empty">
        {{ connected ? '[' + new Date().toLocaleTimeString('zh-CN', { hour12: false }) + '] 已连接到控制台' : '未连接' }}
      </div>
      <template v-for="log in logs" :key="log.id">
        <!-- 普通日志行 -->
        <div v-if="log.type === 'line'" class="terminal-line">
          <span class="line-time">{{ log.time }}</span>
          <span class="line-text">
            <template v-for="(seg, si) in log.segments" :key="si">
              <span v-if="seg.c" :style="{ color: colorMap[seg.c] || seg.c }">{{ seg.t }}</span>
              <template v-else>{{ seg.t }}</template>
            </template>
          </span>
        </div>
        <!-- 命令块（非 SSE 模式） -->
        <div v-else-if="log.type === 'cmd-block'" class="cmd-block">
          <div class="cmd-header">
            <span class="cmd-time">{{ log.time }}</span>
            <span class="cmd-prompt">❯</span>
            <span class="cmd-text">{{ log.cmd }}</span>
          </div>
          <div v-if="log.output" class="cmd-output">{{ log.output }}</div>
          <div v-else class="cmd-output cmd-loading">执行中...</div>
        </div>
      </template>
    </div>
    <div class="terminal-input-row">
      <span class="input-prefix">❯</span>
      <input
        v-model="inputCmd"
        class="terminal-input"
        placeholder="输入命令..."
        @keydown="onInputKeydown"
      />
      <button class="terminal-send-btn" @click="sendCommand" :disabled="!inputCmd.trim()">发送</button>
    </div>
  </div>
</template>

<style scoped>
.terminal-card {
  display: flex; flex-direction: column;
  background: var(--bg-card); border: 1px solid var(--border-light); border-radius: 14px;
  overflow: hidden;
  height: 100%;
}

.terminal-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border-light);
  flex-shrink: 0;
}

.terminal-title {
  font-size: 0.9rem; font-weight: 700; color: var(--text-primary);
  display: flex; align-items: center; gap: 7px;
}

.status-dot {
  width: 8px; height: 8px; border-radius: 50%;
  background: #ef4444;
  transition: background 0.3s;
}
.status-dot.connected { background: #22c55e; box-shadow: 0 0 6px rgba(34, 197, 94, 0.5); }

.terminal-header-actions {
  display: flex; align-items: center; gap: 10px;
}

.log-count { font-size: 0.72rem; color: var(--text-muted); }

.terminal-body {
  flex: 1; overflow-y: auto; overflow-x: hidden; padding: 6px 10px;
  font-family: 'Cascadia Code', 'Fira Code', 'JetBrains Mono', 'Consolas', monospace;
  font-size: 0.75rem;
  line-height: 1.6;
  background: var(--bg-secondary);
  min-height: 0; min-width: 0;
}

.terminal-empty {
  color: var(--text-muted); text-align: center; padding: 20px 0;
  font-family: inherit;
}

.terminal-line {
  display: flex; gap: 8px;
  padding: 1px 0;
  min-width: 0;
}

.line-time { color: var(--text-muted); flex-shrink: 0; opacity: 0.6; font-size: 0.7rem; min-width: 70px; }
.line-text { color: var(--text-primary); overflow-wrap: break-word; word-break: break-all; min-width: 0; }

/* ── 命令块样式（非 SSE 模式） ── */
.cmd-block {
  margin: 4px 0;
  border: 1px solid rgba(99, 102, 241, 0.25);
  border-radius: 8px;
  overflow: hidden;
  background: rgba(99, 102, 241, 0.04);
}

.cmd-header {
  display: flex; align-items: center; gap: 6px;
  padding: 6px 10px;
  background: rgba(99, 102, 241, 0.08);
  border-bottom: 1px solid rgba(99, 102, 241, 0.12);
  font-size: 0.78rem;
}

.cmd-time { color: var(--text-muted); font-size: 0.7rem; opacity: 0.6; min-width: 70px; }
.cmd-prompt { color: #22c55e; font-weight: 700; }
.cmd-text { color: #e2e8f0; font-weight: 600; }

.cmd-output {
  padding: 8px 10px;
  color: #cbd5e1;
  font-size: 0.72rem;
  white-space: pre-wrap;
  word-break: break-all;
  line-height: 1.5;
}

.cmd-output.cmd-loading {
  color: var(--text-muted);
  font-style: italic;
}

.terminal-input-row {
  display: flex; align-items: center; gap: 8px;
  padding: 8px 12px;
  border-top: 1px solid var(--border-light);
  background: var(--bg-tertiary);
  flex-shrink: 0;
}

.input-prefix { color: var(--accent-primary); font-weight: 700; font-size: 0.9rem; }

.terminal-input {
  flex: 1;
  padding: 6px 10px;
  border: 1px solid var(--border-light); border-radius: 6px;
  background: var(--bg-input); color: var(--text-primary);
  font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
  font-size: 0.82rem;
  outline: none;
  transition: border-color 0.2s;
}
.terminal-input:focus { border-color: var(--accent-primary); }

.terminal-send-btn {
  padding: 6px 14px;
  border: 1px solid var(--accent-primary); border-radius: 6px;
  background: var(--accent-primary); color: #fff;
  font-size: 0.78rem; font-weight: 600;
  cursor: pointer; transition: all 0.15s;
  white-space: nowrap;
}
.terminal-send-btn:hover:not(:disabled) { opacity: 0.85; }
.terminal-send-btn:disabled { opacity: 0.4; cursor: default; }
</style>
