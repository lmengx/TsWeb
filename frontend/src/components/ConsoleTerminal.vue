<script setup>
import { ref, onMounted, onUnmounted, nextTick } from 'vue'

const logs = ref([])
const inputCmd = ref('')
const logContainer = ref(null)
const connected = ref(false)

let eventSource = null
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
          // line 是 LogSegment[] 序列化 JSON 字符串
          // '[{"t":"text","c":"Red"},{"t":"normal","c":null}]'
          let segments = []
          try {
            if (typeof line === 'string') {
              // 尝试解析为 LogSegment[] JSON
              const parsed = JSON.parse(line)
              if (Array.isArray(parsed)) {
                segments = parsed
              } else {
                segments = [{ t: line, c: null }]
              }
            } else if (Array.isArray(line)) {
              segments = line
            }
          } catch {
            segments = [{ t: String(line), c: null }]
          }

          logs.value.push({
            id: Date.now() + Math.random(),
            segments,
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
  const raw = inputCmd.value.trim()
  if (!raw) return

  const cmd = raw.startsWith('/') ? raw : '/' + raw

  // 在日志中回显输入
  logs.value.push({
    id: Date.now(),
    segments: [{ t: `> ${cmd}`, c: null }],
    time: new Date().toLocaleTimeString('zh-CN', { hour12: false })
  })

  inputCmd.value = ''

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
      headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${token}` },
      body: JSON.stringify({ cmd, executor: username })
    })
    // 命令输出走 Console → LogInterceptor → SSE 流，无需再重复显示
  } catch (err) {
    logs.value.push({
      id: Date.now() + 1,
      segments: [{ t: `[错误] ${err.message}`, c: null }],
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
        <div v-for="log in logs" :key="log.id" class="terminal-line">
          <span class="line-time">{{ log.time }}</span>
          <span class="line-text">
            <template v-for="(seg, si) in log.segments" :key="si">
              <span v-if="seg.c" :style="{ color: colorMap[seg.c] || seg.c }">{{ seg.t }}</span>
              <template v-else>{{ seg.t }}</template>
            </template>
          </span>
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
