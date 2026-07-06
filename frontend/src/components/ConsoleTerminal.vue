<script setup>
import { ref, watch, nextTick, onMounted, computed } from 'vue'
import { post, get } from '../utils/api.js'

const user = ref(null)

const isAdmin = computed(() => {
  if (!user.value?.usergroup) return false
  const usergroup = user.value.usergroup.toLowerCase()
  return usergroup.includes('admin') || usergroup.includes('owner') || usergroup.includes('superadmin')
})

const loading = ref(false)
const commandInput = ref('')
const commandHistory = ref([])
const executedCommands = ref([])
const maxHistory = 50
const historyContainer = ref(null)
const commandInputRef = ref(null)
const historyIndex = ref(-1)

// ── 服务器统计数据 ──
const stats = ref({ online: '0', total: '0', qqBound: '0', totalBans: '0' })

const fetchServerStats = async () => {
  try {
    const [activeRes, usersRes, bansRes] = await Promise.all([
      get('/api/tshock/activeusers'),
      get('/api/tshock/users'),
      get('/api/tshock/banlist')
    ])
    
    const activeData = await activeRes.json()
    const usersData = await usersRes.json()
    const bansData = await bansRes.json()
    
    let online = '0'
    if (activeData.activeusers) {
      const names = activeData.activeusers.split('\t').filter(n => n.trim())
      online = String(names.length)
    } else if (activeData.users) {
      online = String(activeData.users.length)
    }
    
    let total = '0'
    let qqBound = '0'
    if (usersData.status === '200' && usersData.users) {
      total = String(usersData.users.length)
      const bound = usersData.users.filter(u => u.QQ && u.QQ.trim() !== '')
      qqBound = String(bound.length)
    } else if (usersData.users) {
      total = String(usersData.users.length)
    }
    
    let totalBans = '0'
    if (bansData.status === '200' && bansData.bans) {
      totalBans = String(bansData.bans.length)
    } else if (Array.isArray(bansData)) {
      totalBans = String(bansData.length)
    }
    
    stats.value = { online, total, qqBound, totalBans }
  } catch {
    // 静默失败，保留 '--'
  }
}

const allCommands = [
  '/user', '/login', '/logout', '/password', '/register', '/accountinfo', '/ban', '/broadcast',
  '/displaylogs', '/group', '/itemban', '/projban', '/tileban', '/region', '/kick', '/mute',
  '/overridessc', '/savessc', '/uploadssc', '/tempgroup', '/su', '/sudo', '/userinfo', '/annoy',
  '/rocket', '/firework', '/checkupdates', '/off', '/off-nosave', '/reload',
  '/serverpassword', '/version', '/whitelist', '/give', '/item', '/butcher', '/renamenpc',
  '/maxspawns', '/spawnboss', '/spawnmob', '/spawnrate', '/clearangler', '/home', '/spawn',
  '/tp', '/tphere', '/tpnpc', '/tppos', '/pos', '/tpallow', '/worldmode', '/antibuild', '/grow',
  '/forcehalloween', '/forcexmas', '/worldevent', '/hardmode', '/evil', '/protectspawn',
  '/save', '/setspawn', '/setdungeon', '/settle', '/time', '/wind', '/worldinfo', '/buff',
  '/clear', '/gbuff', '/godmode', '/heal', '/kill', '/me', '/party', '/reply', '/rest', '/slap',
  '/serverinfo', '/warp', '/whisper', '/wallow', '/dump-reference-data', '/sync', '/respawn',
  '/aliases', '/help', '/motd', '/playing', '/rules', '/death', '/pvpdeath', '/alldeath'
]

const suggestions = computed(() => {
  const input = commandInput.value.trim().toLowerCase()
  
  if (!input) {
    return ['/help']
  }
  
  const startsWithMatches = allCommands.filter(cmd => 
    cmd.toLowerCase().startsWith(input)
  )
  
  const containsMatches = allCommands.filter(cmd => 
    !cmd.toLowerCase().startsWith(input) && cmd.toLowerCase().includes(input)
  )
  
  const allMatches = [...startsWithMatches, ...containsMatches]
  return allMatches.slice(0, 10)
})

const scrollToBottom = () => {
  nextTick(() => {
    if (historyContainer.value) {
      historyContainer.value.scrollTop = historyContainer.value.scrollHeight
    }
  })
}

watch(commandHistory, () => {
  scrollToBottom()
}, { deep: true })

const addToHistory = (command, result, type) => {
  commandHistory.value.push({
    id: Date.now(),
    command,
    result,
    type,
    timestamp: new Date().toLocaleTimeString()
  })

  if (commandHistory.value.length > maxHistory) {
    commandHistory.value.shift()
  }
}

const addToExecutedCommands = (cmd) => {
  if (!executedCommands.value.includes(cmd)) {
    executedCommands.value.push(cmd)
  }
  historyIndex.value = executedCommands.value.length
}

const handleKeyDown = (e) => {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    executeCommand()
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    navigateHistoryUp()
  } else if (e.key === 'ArrowDown') {
    e.preventDefault()
    navigateHistoryDown()
  }
}

const navigateHistoryUp = () => {
  if (executedCommands.value.length === 0) return
  if (historyIndex.value > 0) {
    historyIndex.value--
    commandInput.value = executedCommands.value[historyIndex.value]
  }
}

const navigateHistoryDown = () => {
  if (historyIndex.value < executedCommands.value.length - 1) {
    historyIndex.value++
    commandInput.value = executedCommands.value[historyIndex.value]
  } else {
    historyIndex.value = executedCommands.value.length
    commandInput.value = ''
  }
}

const selectSuggestion = (cmd) => {
  commandInput.value = cmd
  nextTick(() => {
    if (commandInputRef.value) {
      commandInputRef.value.focus()
    }
  })
}

const formatResult = (result) => {
  if (!result) return '无返回内容'

  let content = ''

  if (result.status === '200' && result.response) {
    if (Array.isArray(result.response)) {
      content = result.response.join('\n')
    } else {
      content = result.response
    }
  } else if (result.error) {
    content = `错误: ${result.error}`
  } else if (result.status && result.response) {
    content = result.response
  } else {
    const keys = Object.keys(result)
    const filteredKeys = keys.filter(k => k !== 'status' && k !== 'error')

    if (filteredKeys.length === 0) {
      content = JSON.stringify(result, null, 2)
    } else {
      const filteredResult = {}
      filteredKeys.forEach(k => {
        filteredResult[k] = result[k]
      })
      content = JSON.stringify(filteredResult, null, 2)
    }
  }

  return content.replace(/\[c\/([A-Fa-f0-9]+):([^\]]+)\]/g, '<span style="color: #$1; font-weight: bold;">$2</span>')
}

const loadUser = () => {
  const saved = localStorage.getItem('user')
  if (saved) {
    try {
      user.value = JSON.parse(saved)
    } catch (e) {
      console.error('Failed to load user')
    }
  }
}

const executeCommand = async () => {
  if (!commandInput.value.trim()) return
  if (!isAdmin.value) {
    addToHistory(commandInput.value, { error: '您没有管理员权限' }, 'error')
    commandInput.value = ''
    return
  }

  loading.value = true
  let cmd = commandInput.value.trim()

  if (!cmd.startsWith('/')) {
    cmd = '/' + cmd
  }

  addToExecutedCommands(cmd)

  try {
    const response = await post('/api/tshock/command', { command: cmd })
    const result = await response.json()

    if (result.error) {
      addToHistory(cmd, result, 'error')
    } else {
      addToHistory(cmd, result, 'success')
    }
  } catch (error) {
    if (error.message !== 'Unauthorized') {
      addToHistory(cmd, { error: error.message }, 'error')
    }
  }

  commandInput.value = ''
  historyIndex.value = executedCommands.value.length
  loading.value = false

  nextTick(() => {
    if (commandInputRef.value) {
      commandInputRef.value.focus()
    }
  })
}

onMounted(() => {
  loadUser()
  fetchServerStats()
  if (commandInputRef.value) {
    commandInputRef.value.focus()
  }
})
</script>

<template>
  <div class="console-content">
    <div class="history-container" ref="historyContainer">
      <div v-if="commandHistory.length === 0" class="empty-state">
        <div class="stats-grid">
          <div class="stat-item">
            <span class="stat-value">{{ stats.online }}<span class="stat-sep">/</span>{{ stats.total }}</span>
            <span class="stat-label">当前在线 / 总注册</span>
          </div>
          <div class="stat-item">
            <span class="stat-value">{{ stats.qqBound }}<small> 人</small></span>
            <span class="stat-label">QQ 绑定</span>
          </div>
          <div class="stat-item">
            <span class="stat-value">{{ stats.totalBans }}<small> 条</small></span>
            <span class="stat-label">累计封禁</span>
          </div>
        </div>
        <p class="welcome-text">欢迎使用 TShock 控制台</p>
        <p class="hint-text" v-if="isAdmin">输入命令并按 Enter 执行</p>
        <p class="hint-text" v-else>您没有管理员权限，无法执行命令</p>
      </div>

      <div 
        v-for="item in commandHistory" 
        :key="item.id" 
        class="history-item"
        :class="item.type"
      >
        <div class="item-header">
          <span class="timestamp">{{ item.timestamp }}</span>
          <span class="type-badge" :class="item.type">
            {{ item.type === 'success' ? '成功' : '错误' }}
          </span>
        </div>
        <div class="command">
          <span class="prompt">></span>
          <span>{{ item.command }}</span>
        </div>
        <pre class="result" v-html="formatResult(item.result)"></pre>
      </div>
    </div>

    <div class="input-area">
      <div v-if="suggestions.length > 0" class="suggestions-container">
        <div class="suggestions-list">
          <div 
            v-for="cmd in suggestions.slice(0, 5)" 
            :key="cmd"
            @click="selectSuggestion(cmd)"
            class="suggestion-item"
          >
            {{ cmd }}
          </div>
          <div v-if="suggestions.length > 5" class="suggestions-more">
            <span>还有 {{ suggestions.length - 5 }} 个...</span>
          </div>
        </div>
      </div>

      <div class="input-container">
        <div class="input-wrapper">
          <span class="prompt">></span>
          <input
            v-model="commandInput"
            type="text"
            :placeholder="isAdmin ? '输入命令...' : '您没有管理员权限'"
            :disabled="loading || !isAdmin"
            @keydown="handleKeyDown"
            class="command-input"
            autocomplete="off"
            ref="commandInputRef"
          />
        </div>
        <button 
          @click="executeCommand" 
          :disabled="loading || !isAdmin || !commandInput.trim()"
          class="execute-btn"
        >
          {{ loading ? '执行中...' : '执行' }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.console-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
}

.history-container {
  flex: 1;
  overflow-y: auto;
  padding: 20px;
  display: flex;
  flex-direction: column;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-muted);
  gap: 8px;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
  margin-bottom: 16px;
  width: 100%;
  max-width: 480px;
}

.stat-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 20px 16px;
  background: var(--bg-tertiary);
  border-radius: 12px;
  border: 1px solid var(--border-color);
  transition: all 0.25s ease;
}

.stat-item:hover {
  border-color: var(--accent-primary);
  transform: translateY(-2px);
}

.stat-value {
  font-size: 1.8rem;
  font-weight: 800;
  background: linear-gradient(135deg, var(--accent-primary), #8b5cf6);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  font-variant-numeric: tabular-nums;
  line-height: 1;
}

.stat-sep {
  font-size: 1.2rem;
  font-weight: 400;
  -webkit-text-fill-color: var(--text-muted);
  color: var(--text-muted);
  margin: 0 2px;
}

.stat-value small {
  font-size: 0.7rem;
  font-weight: 600;
  -webkit-text-fill-color: var(--text-muted);
  color: var(--text-muted);
}

.stat-label {
  font-size: 0.8rem;
  color: var(--text-muted);
  font-weight: 500;
}

.welcome-text {
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-secondary);
  margin: 8px 0 0;
}

.hint-text {
  font-size: 0.85rem;
  color: var(--text-muted);
  margin: 0;
}

.empty-state p {
  margin: 8px 0;
  font-size: 0.95rem;
}

.history-item {
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
  padding: 16px;
  margin-bottom: 16px;
  border-left: 4px solid var(--accent-secondary);
  box-shadow: var(--shadow-sm);
  transition: transform 0.2s ease;
}

.history-item:hover {
  transform: translateX(4px);
}

.history-item.error {
  border-left-color: var(--accent-error);
}

.item-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.timestamp {
  font-size: 0.75rem;
  color: var(--text-muted);
}

.type-badge {
  font-size: 0.75rem;
  padding: 4px 10px;
  border-radius: 20px;
  background: linear-gradient(135deg, var(--accent-secondary), #16a34a);
  color: white;
  font-weight: 500;
  box-shadow: 0 2px 8px rgba(55, 154, 107, 0.3);
}

.type-badge.error {
  background: linear-gradient(135deg, var(--accent-error), #dc2626);
  box-shadow: 0 2px 8px rgba(239, 68, 68, 0.3);
}

.command {
  display: flex;
  gap: 10px;
  margin-bottom: 12px;
  font-family: 'JetBrains Mono', 'Consolas', 'Monaco', monospace;
  font-size: 0.9rem;
}

.command .prompt {
  color: var(--accent-secondary);
  font-weight: 600;
}

.result {
  background: var(--bg-primary);
  padding: 14px 16px;
  border-radius: var(--radius-md);
  margin: 0;
  overflow-x: auto;
  font-family: 'JetBrains Mono', 'Consolas', 'Monaco', monospace;
  font-size: 0.85rem;
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 200px;
  overflow-y: auto;
  border: 1px solid var(--border-light);
}

.history-item.error .result {
  color: var(--accent-error);
}

.input-area {
  flex-shrink: 0;
  background: var(--bg-tertiary);
  border-top: 1px solid var(--border-light);
}

.suggestions-container {
  padding: 12px 20px;
  max-height: 80px;
  overflow-y: auto;
}

.suggestions-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.suggestion-item {
  padding: 6px 14px;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 20px;
  color: var(--text-primary);
  font-size: 0.85rem;
  font-family: 'JetBrains Mono', 'Consolas', 'Monaco', monospace;
  cursor: pointer;
  transition: all 0.2s ease;
  white-space: nowrap;
}

.suggestion-item:hover {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
  color: white;
  transform: translateY(-1px);
}

.suggestions-more {
  padding: 6px 14px;
  color: var(--text-muted);
  font-size: 0.8rem;
}

.input-container {
  display: flex;
  gap: 12px;
  padding: 16px 20px;
}

.input-wrapper {
  flex: 1;
  display: flex;
  align-items: center;
  background: var(--bg-primary);
  border-radius: var(--radius-lg);
  padding: 0 16px;
  border: 2px solid var(--border-color);
  transition: all 0.25s ease;
}

.input-wrapper:focus-within {
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.input-wrapper .prompt {
  color: var(--accent-secondary);
  font-weight: 600;
  margin-right: 10px;
  font-family: 'JetBrains Mono', 'Consolas', 'Monaco', monospace;
  font-size: 1rem;
}

.command-input {
  flex: 1;
  background: transparent;
  border: none;
  outline: none;
  color: var(--text-primary);
  padding: 14px 0;
  font-size: 1rem;
  font-family: 'JetBrains Mono', 'Consolas', 'Monaco', monospace;
}

.command-input::placeholder {
  color: var(--text-muted);
}

.command-input:disabled {
  opacity: 0.5;
}

.execute-btn {
  padding: 14px 28px;
  background: linear-gradient(135deg, var(--accent-secondary), #16a34a);
  color: white;
  border: none;
  border-radius: var(--radius-lg);
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-md);
}

.execute-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: var(--shadow-lg);
}

.execute-btn:active:not(:disabled) {
  transform: translateY(0);
}

.execute-btn:disabled {
  background: var(--bg-hover);
  color: var(--text-muted);
  cursor: not-allowed;
  box-shadow: none;
}
</style>