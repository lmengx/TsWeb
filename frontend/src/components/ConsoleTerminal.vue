<script setup>
import { ref, onMounted, onUnmounted, nextTick } from 'vue'

const logs = ref([])
const inputCmd = ref('')
const logContainer = ref(null)
const connected = ref(false)

let eventSource = null
const MAX_LOG = 200

function getToken() {
  try {
    const user = localStorage.getItem('user')
    if (user) return JSON.parse(user).token
  } catch {}
  return ''
}

onMounted(() => {
  connectSSE()
})

onUnmounted(() => {
  if (eventSource) {
    eventSource.close()
    eventSource = null
  }
})

function connectSSE() {
  const token = getToken()
  if (!token) return

  eventSource = new EventSource(`/api/online/log/stream?token=${encodeURIComponent(token)}`)

  eventSource.onopen = () => {
    connected.value = true
  }

  eventSource.onmessage = (e) => {
    try {
      const data = JSON.parse(e.data)
      if (data.connected) return
      if (Array.isArray(data)) {
        data.forEach(line => {
          logs.value.push({
            id: Date.now() + Math.random(),
            text: line,
            time: new Date().toLocaleTimeString('zh-CN', { hour12: false })
          })
        })
        if (logs.value.length > MAX_LOG) {
          logs.value = logs.value.slice(-MAX_LOG)
        }
        scrollToBottom()
      }
    } catch { }
  }

  eventSource.onerror = () => {
    connected.value = false
    // 自动重连（浏览器 EventSource 自带）
  }
}

async function sendCommand() {
  const cmd = inputCmd.value.trim()
  if (!cmd) return

  // 在日志中回显输入
  logs.value.push({
    id: Date.now(),
    text: `> ${cmd}`,
    time: new Date().toLocaleTimeString('zh-CN', { hour12: false })
  })

  inputCmd.value = ''

  try {
    const token = getToken()
    const res = await fetch('/api/online/log/command', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${token}` },
      body: JSON.stringify({ cmd })
    })
    const json = await res.json()
    if (json.response) {
      logs.value.push({
        id: Date.now() + 1,
        text: json.response,
        time: new Date().toLocaleTimeString('zh-CN', { hour12: false })
      })
      if (logs.value.length > MAX_LOG) {
        logs.value = logs.value.slice(-MAX_LOG)
      }
      scrollToBottom()
    }
  } catch (err) {
    logs.value.push({
      id: Date.now() + 1,
      text: `[错误] ${err.message}`,
      time: ''
    })
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

function clearLogs() {
  logs.value = []
}
</script>

<template>
  <div class="terminal-card">
    <div class="terminal-header">
      <span class="terminal-title">
        <span class="status-dot" :class="{ connected }"></span>
        服务器控制台
      </span>
      <div class="terminal-header-actions">
        <span class="log-count">{{ logs.length }} 条</span>
        <button class="terminal-clear-btn" @click="clearLogs" title="清屏">✕</button>
      </div>
    </div>
    <div class="terminal-body" ref="logContainer">
      <div v-if="logs.length === 0" class="terminal-empty">
        {{ connected ? '等待日志...' : '未连接' }}
      </div>
      <div v-for="log in logs" :key="log.id" class="terminal-line">
        <span class="line-time">{{ log.time }}</span>
        <span class="line-text" v-text="log.text"></span>
      </div>
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

.terminal-clear-btn {
  width: 22px; height: 22px; border-radius: 5px;
  border: 1px solid var(--border-light); background: var(--bg-tertiary);
  color: var(--text-muted); font-size: 0.65rem; cursor: pointer;
  display: flex; align-items: center; justify-content: center;
}
.terminal-clear-btn:hover { background: rgba(239, 68, 68, 0.1); border-color: #ef4444; color: #ef4444; }

.terminal-body {
  flex: 1; overflow-y: auto; padding: 8px 12px;
  font-family: 'Cascadia Code', 'Fira Code', 'JetBrains Mono', 'Consolas', monospace;
  font-size: 0.75rem;
  line-height: 1.5;
  background: var(--bg-secondary);
  min-height: 0;
}

.terminal-empty {
  color: var(--text-muted); text-align: center; padding: 20px 0;
  font-family: inherit;
}

.terminal-line {
  display: flex; gap: 8px;
  padding: 1px 0;
  word-break: break-all;
}

.line-time { color: var(--text-muted); flex-shrink: 0; opacity: 0.6; font-size: 0.7rem; min-width: 70px; }
.line-text { color: var(--text-primary); }

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
