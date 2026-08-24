<script setup>
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { get, post } from '../../../utils/api.js'

const loading = ref(true)
const error = ref('')
const success = ref('')
let saveTimer = null
let pollTimer = null

// ═══ 目标群体元数据 ═══
const TARGETS = [
  { key: 'all', label: '所有玩家', hint: '全部玩家（含未登录）' },
  { key: 'proxy', label: '海外/代理玩家', hint: 'IP 归属非四大运营商或 ip.sb 无法判定' },
  { key: 'register-under-1h', label: '注册不足1小时', hint: 'TShock 账号注册时间 < 1 小时' },
  { key: 'playtime-under-1h', label: '游玩不足1小时', hint: '累计游玩时间 < 1 小时' },
  { key: 'not-logged-in', label: '未登录玩家', hint: '尚未登录账号' },
]

// ═══ 配置（v2）═══
const config = ref({
  blockEnter: { enabled: false, targets: [] },
  blockChat: { enabled: false, targets: [] },
  qqBindExempt: true,
  exemptGroups: ['owner', 'superadmin'],
  proxy: {
    enabled: true,
    cacheTtlHours: 24,
    allowIsps: ['中国电信', '中国联通', '中国移动', '中国广电', 'China Telecom', 'China Unicom', 'China Mobile', 'CBN', 'Chinanet', 'CHINANET', 'CMNET'],
    proxyKeywords: ['relay', 'vpn', 'proxy', 'hosting', 'datacenter', 'cloud', 'akamai', 'cloudflare', 'ovh', 'hetzner'],
  },
})

// 高级设置文本输入（逗号分隔）
const exemptGroupsText = ref('')
const allowIspsText = ref('')
const proxyKeywordsText = ref('')

// ═══ 在线玩家特征 + 选区 ═══
const players = ref([])
const apiHealth = ref('ok')
const selectedTargets = ref([])
const expandedTarget = ref('')
const kicking = ref(false)
const refreshing = ref(false)

// ═══ 群体命中计算 ═══
const hitCounts = computed(() => {
  const counts = { all: 0, proxy: 0, 'register-under-1h': 0, 'playtime-under-1h': 0, 'not-logged-in': 0 }
  for (const p of players.value) {
    counts.all++
    if (p.ip && (p.proxyStatus === 'proxy' || p.proxyStatus === 'unknown')) counts.proxy++
    if (p.loggedIn && p.registerMinutesAgo !== undefined && p.registerMinutesAgo >= 0 && p.registerMinutesAgo < 60) counts['register-under-1h']++
    if (p.loggedIn && p.playtimeMinutes !== undefined && p.playtimeMinutes < 60) counts['playtime-under-1h']++
    if (!p.loggedIn) counts['not-logged-in']++
  }
  return counts
})

const hitPlayers = (key) => players.value.filter(p => {
  switch (key) {
    case 'all': return true
    case 'proxy': return p.ip && (p.proxyStatus === 'proxy' || p.proxyStatus === 'unknown')
    case 'register-under-1h': return p.loggedIn && p.registerMinutesAgo !== undefined && p.registerMinutesAgo >= 0 && p.registerMinutesAgo < 60
    case 'playtime-under-1h': return p.loggedIn && p.playtimeMinutes !== undefined && p.playtimeMinutes < 60
    case 'not-logged-in': return !p.loggedIn
    default: return false
  }
})

// 踢出预估人数（选区并集去重）
const kickEstimate = computed(() => {
  const set = new Set()
  for (const t of selectedTargets.value) {
    for (const p of hitPlayers(t)) set.add(p)
  }
  return set.size
})

// ═══ 状态/工具 ═══
const statusLabel = (s) => ({
  normal: { text: '正常', cls: 'status-normal' },
  proxy: { text: '海外/代理', cls: 'status-proxy' },
  unknown: { text: '未知(拦截)', cls: 'status-unknown' },
  unavailable: { text: '检测不可用', cls: 'status-unavailable' },
  pending: { text: '检测中…', cls: 'status-pending' },
  disabled: { text: '检测关闭', cls: 'status-disabled' },
}[s] || { text: s || '-', cls: 'status-pending' })

const geoText = (p) => {
  const g = p.geo
  if (!g) return ''
  return [g.country, g.isp].filter(Boolean).join(' · ')
}

const targetLabels = (keys) => {
  if (!keys || !keys.length) return '（无）'
  return keys.map(k => TARGETS.find(t => t.key === k)?.label || k).join('、')
}

const sameTargets = (a, b) => {
  const sa = [...(a || [])].sort().join(',')
  const sb = [...(b || [])].sort().join(',')
  return sa === sb
}

// ═══ 配置读写 ═══
const doSave = async () => {
  error.value = ''
  success.value = ''
  try {
    const res = await post('/api/config/risk-control', {
      blockEnter: config.value.blockEnter,
      blockChat: config.value.blockChat,
      qqBindExempt: config.value.qqBindExempt,
      exemptGroups: config.value.exemptGroups,
      proxy: config.value.proxy,
    })
    const data = await res.json()
    if (data.status === '200') {
      success.value = '已保存'
      setTimeout(() => { success.value = '' }, 1500)
    } else {
      error.value = data.error || '保存失败'
    }
  } catch (err) {
    error.value = '保存失败: ' + err.message
  }
}

const saveConfig = (immediate = false) => {
  clearTimeout(saveTimer)
  if (immediate) return doSave()
  saveTimer = setTimeout(doSave, 500)
}

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/risk-control')
    const data = await res.json()
    if (data.blockEnter) config.value.blockEnter = { enabled: !!data.blockEnter.enabled, targets: data.blockEnter.targets || [] }
    if (data.blockChat) config.value.blockChat = { enabled: !!data.blockChat.enabled, targets: data.blockChat.targets || [] }
    if (data.qqBindExempt !== undefined) config.value.qqBindExempt = !!data.qqBindExempt
    if (data.exemptGroups) config.value.exemptGroups = data.exemptGroups
    if (data.proxy) {
      config.value.proxy = {
        enabled: data.proxy.enabled !== false,
        cacheTtlHours: data.proxy.cacheTtlHours || 24,
        allowIsps: data.proxy.allowIsps || [],
        proxyKeywords: data.proxy.proxyKeywords || [],
      }
    }
    exemptGroupsText.value = (config.value.exemptGroups || []).join(', ')
    allowIspsText.value = (config.value.proxy.allowIsps || []).join(', ')
    proxyKeywordsText.value = (config.value.proxy.proxyKeywords || []).join(', ')
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  loading.value = false
}

// ═══ 玩家特征拉取（pending 时 2s 轮询）═══
const fetchPlayers = async () => {
  try {
    const res = await get('/api/config/risk-control/players')
    const data = await res.json()
    if (data.players) {
      players.value = data.players
      apiHealth.value = data.apiHealth || 'ok'
    }
  } catch (err) {
    /* 静默，下次轮询重试 */
  }
  clearTimeout(pollTimer)
  if (players.value.some(p => p.proxyStatus === 'pending')) {
    pollTimer = setTimeout(fetchPlayers, 2000)
  }
}

// ═══ 操作执行 ═══
const executeKick = async () => {
  if (!selectedTargets.value.length) {
    error.value = '请先选择玩家群体'
    return
  }
  if (!confirm(`确定要踢出选中的 ${kickEstimate.value} 名在线玩家吗？此操作不可撤销。`)) return
  kicking.value = true
  error.value = ''
  success.value = ''
  try {
    const res = await post('/api/config/risk-control/action', { action: 'kick', targets: selectedTargets.value })
    const data = await res.json()
    if (data.status === '200') {
      success.value = `已踢出 ${data.kicked ?? '未知'} 人`
      setTimeout(() => { success.value = '' }, 3000)
      fetchPlayers()
    } else {
      error.value = data.error || '踢出失败'
    }
  } catch (err) {
    error.value = '踢出失败: ' + err.message
  } finally {
    kicking.value = false
  }
}

// 持续性操作：开启时把当前选区固化为生效目标
const applyTargetsTo = async (which) => {
  if (!selectedTargets.value.length) {
    error.value = '请先选择玩家群体，再开启该操作'
    return
  }
  config.value[which].targets = [...selectedTargets.value]
  config.value[which].enabled = true
  await doSave()
  success.value = which === 'blockEnter' ? '✅ 禁止进入已开启并生效' : '✅ 禁言已开启并生效'
  setTimeout(() => { success.value = '' }, 2000)
}

const toggleBlockEnter = () => {
  if (config.value.blockEnter.enabled) {
    config.value.blockEnter.enabled = false
    saveConfig()
  } else {
    applyTargetsTo('blockEnter')
  }
}

const toggleBlockChat = () => {
  if (config.value.blockChat.enabled) {
    config.value.blockChat.enabled = false
    saveConfig()
  } else {
    applyTargetsTo('blockChat')
  }
}

const refreshProxy = async () => {
  refreshing.value = true
  error.value = ''
  try {
    await post('/api/config/risk-control/proxy/refresh', {})
    await fetchPlayers()
  } catch (err) {
    error.value = '刷新检测失败: ' + err.message
  } finally {
    refreshing.value = false
  }
}

// ═══ 高级设置应用 ═══
const applyExemptGroups = () => {
  config.value.exemptGroups = exemptGroupsText.value.split(',').map(s => s.trim()).filter(Boolean)
  saveConfig()
}
const applyAllowIsps = () => {
  config.value.proxy.allowIsps = allowIspsText.value.split(',').map(s => s.trim()).filter(Boolean)
  saveConfig()
}
const applyProxyKeywords = () => {
  config.value.proxy.proxyKeywords = proxyKeywordsText.value.split(',').map(s => s.trim()).filter(Boolean)
  saveConfig()
}

// 自动保存（这些字段不涉及"先选群体"校验）
watch(() => config.value.qqBindExempt, saveConfig)
watch(() => config.value.proxy.enabled, saveConfig)
watch(() => config.value.proxy.cacheTtlHours, saveConfig)

onMounted(async () => {
  await fetchConfig()
  await fetchPlayers()
})

onUnmounted(() => {
  clearTimeout(saveTimer)
  clearTimeout(pollTimer)
})
</script>

<template>
  <div class="settings-page">
    <div v-if="loading" class="loading-state"><p>加载中...</p></div>

    <div v-else class="settings-content">

      <!-- ip.sb 服务异常横幅 -->
      <div v-if="apiHealth === 'degraded'" class="banner banner-warn">
        ⚠️ ip.sb 服务异常，海外/代理拦截已临时放行（仅影响代理项，不影响注册/游玩时长等目标）
      </div>

      <!-- ═══ ① 选择玩家群体 ═══ -->
      <div class="section-card">
        <h3>🎯 选择玩家群体</h3>
        <p class="section-desc">多选要处理的群体（命中数实时计算，可展开查看名单）。开启"海外/代理玩家"拦截后：海外及非四大运营商直接拦截，ip.sb 无法判定也拦截，ip.sb 服务异常则此项临时放行。</p>

        <div v-for="t in TARGETS" :key="t.key" class="target-row">
          <div class="target-main">
            <label class="target-check">
              <input type="checkbox" :value="t.key" v-model="selectedTargets" />
              <span class="target-label">{{ t.label }}</span>
            </label>
            <span class="target-hint">{{ t.hint }}</span>
          </div>
          <div class="target-meta">
            <span class="target-count">{{ hitCounts[t.key] }}</span>
            <button class="link-btn" @click="expandedTarget = expandedTarget === t.key ? '' : t.key">
              {{ expandedTarget === t.key ? '收起' : '名单' }}
            </button>
          </div>
          <div v-if="expandedTarget === t.key" class="target-list">
            <div v-if="!hitPlayers(t.key).length" class="empty">无命中玩家</div>
            <div v-for="p in hitPlayers(t.key)" :key="p.name + '|' + p.ip" class="player-row">
              <span class="p-name">{{ p.name || '(未登录)' }}</span>
              <span class="p-ip">{{ p.ip || '-' }}</span>
              <span class="p-geo">{{ geoText(p) }}</span>
              <span :class="['badge', statusLabel(p.proxyStatus).cls]">{{ statusLabel(p.proxyStatus).text }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- ═══ ② 执行操作 ═══ -->
      <div class="section-card">
        <h3>⚡ 执行操作</h3>
        <p class="section-desc">一次性操作点击执行；持续性操作切换开关，开启时固化当前选区为生效目标</p>

        <!-- 踢出（一次性） -->
        <div class="action-row">
          <div class="action-info">
            <span class="action-label">踢出选中群体</span>
            <span class="action-hint">只扫描在线玩家 · 预估 {{ kickEstimate }} 人</span>
          </div>
          <button class="action-btn btn-danger" :disabled="kicking || !selectedTargets.length" @click="executeKick">
            {{ kicking ? '执行中...' : '执行踢出' }}
          </button>
        </div>

        <!-- 禁止进入（持续性） -->
        <div class="action-row">
          <div class="action-info">
            <span class="action-label">禁止进入</span>
            <span class="action-hint">
              <template v-if="config.blockEnter.enabled">
                🔴 拦截中：{{ targetLabels(config.blockEnter.targets) }}
                <span v-if="!sameTargets(config.blockEnter.targets, selectedTargets)" class="hint-warn">
                  （当前选区已变化，
                  <a class="link-inline" @click="applyTargetsTo('blockEnter')">更新为当前选区</a>）
                </span>
              </template>
              <template v-else>⚪ 未开启</template>
            </span>
          </div>
          <label class="switch">
            <input type="checkbox" :checked="config.blockEnter.enabled" @change="toggleBlockEnter" />
            <span class="slider"></span>
          </label>
        </div>

        <!-- 禁言（持续性） -->
        <div class="action-row">
          <div class="action-info">
            <span class="action-label">禁言</span>
            <span class="action-hint">
              <template v-if="config.blockChat.enabled">
                🔴 拦截中：{{ targetLabels(config.blockChat.targets) }}
                <span v-if="!sameTargets(config.blockChat.targets, selectedTargets)" class="hint-warn">
                  （当前选区已变化，
                  <a class="link-inline" @click="applyTargetsTo('blockChat')">更新为当前选区</a>）
                </span>
              </template>
              <template v-else>⚪ 未开启</template>
            </span>
          </div>
          <label class="switch">
            <input type="checkbox" :checked="config.blockChat.enabled" @change="toggleBlockChat" />
            <span class="slider"></span>
          </label>
        </div>
      </div>

      <!-- ═══ ③ 代理检测状态 ═══ -->
      <div class="section-card">
        <h3>🕵️ 代理检测状态</h3>
        <p class="section-desc">当前在线玩家判定：🟢正常 / 🔴海外代理(拦截) / 🟠未知(拦截) / ⚪检测不可用(放行)</p>
        <div class="status-summary">
          <span class="summary-item" :class="{ 'summary-ok': players.every(p => p.proxyStatus !== 'proxy' && p.proxyStatus !== 'unknown') }">
            海外/代理：{{ players.filter(p => p.proxyStatus === 'proxy').length }} 人
          </span>
          <span class="summary-item">未知：{{ players.filter(p => p.proxyStatus === 'unknown').length }} 人</span>
          <span class="summary-item">检测中：{{ players.filter(p => p.proxyStatus === 'pending').length }} 人</span>
          <button class="action-btn btn-warning" :disabled="refreshing" @click="refreshProxy">
            {{ refreshing ? '刷新中...' : '🔄 重新检测' }}
          </button>
        </div>
      </div>

      <!-- ═══ ④ 高级设置 ═══ -->
      <div class="section-card">
        <h3>⚙️ 高级设置</h3>
        <details>
          <summary class="details-summary">展开设置</summary>

          <div class="toggle-row">
            <div class="toggle-label-wrap">
              <span class="toggle-label">绑定 QQ 玩家直接放行</span>
              <span class="toggle-hint">进服与发言均豁免（保险项，建议保持开启）</span>
            </div>
            <label class="switch">
              <input type="checkbox" v-model="config.qqBindExempt" />
              <span class="slider"></span>
            </label>
          </div>

          <div class="field-row">
            <label class="field-label">豁免组（逗号分隔）</label>
            <input class="field-input" type="text" v-model="exemptGroupsText" @change="applyExemptGroups" />
            <span class="field-hint">这些用户组不受禁言/进服目标拦截影响（紧急全禁除外）</span>
          </div>

          <div class="toggle-row">
            <div class="toggle-label-wrap">
              <span class="toggle-label">代理检测（ip.sb）</span>
              <span class="toggle-hint">总开关，关闭后不发起任何检测请求</span>
            </div>
            <label class="switch">
              <input type="checkbox" v-model="config.proxy.enabled" />
              <span class="slider"></span>
            </label>
          </div>

          <div class="field-row">
            <label class="field-label">检测缓存时长（小时）</label>
            <input class="field-input field-input-sm" type="number" v-model.number="config.proxy.cacheTtlHours" min="1" />
            <span class="field-hint">同 IP 结果缓存时长，避免重复请求触发 ip.sb 限速</span>
          </div>

          <div class="field-row">
            <label class="field-label">允许 ISP 白名单（四大运营商，逗号分隔）</label>
            <input class="field-input" type="text" v-model="allowIspsText" @change="applyAllowIsps" />
            <span class="field-hint">命中即为正常，不拦截</span>
          </div>

          <div class="field-row">
            <label class="field-label">代理特征关键字（逗号分隔）</label>
            <input class="field-input" type="text" v-model="proxyKeywordsText" @change="applyProxyKeywords" />
            <span class="field-hint">明确代理特征（relay/vpn/hosting 等），命中直接判恶意</span>
          </div>
        </details>
      </div>

      <!-- Toast -->
      <Transition name="toast">
        <div v-if="success" class="toast toast-success">
          <span>{{ success }}</span>
        </div>
      </Transition>
      <Transition name="toast">
        <div v-if="error" class="toast toast-error">
          <span>{{ error }}</span>
        </div>
      </Transition>
    </div>
  </div>
</template>

<style scoped>
.settings-page {
  padding: 20px;
  width: 100%;
}

.settings-content {
  max-width: 760px;
}

.loading-state {
  text-align: center;
  padding: 60px;
  color: var(--text-muted);
}

.section-card {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  margin-bottom: 20px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.section-card h3 {
  margin: 0 0 4px 0;
  color: var(--text-primary);
  font-size: 1.1rem;
  font-weight: 600;
}

.section-desc {
  margin: 0 0 16px 0;
  color: var(--text-muted);
  font-size: 0.85rem;
  line-height: 1.5;
}

/* ── 横幅 ── */
.banner {
  border-radius: var(--radius-md);
  padding: 12px 16px;
  font-size: 0.9rem;
  margin-bottom: 20px;
}

.banner-warn {
  background: rgba(245, 158, 11, 0.15);
  color: #f59e0b;
  border: 1px solid rgba(245, 158, 11, 0.3);
}

/* ── 目标群体选择 ── */
.target-row {
  padding: 12px 0;
  border-bottom: 1px solid var(--border-light);
}

.target-row:last-child {
  border-bottom: none;
}

.target-main {
  display: flex;
  align-items: baseline;
  gap: 12px;
}

.target-check {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
}

.target-check input {
  accent-color: var(--accent-primary);
  width: 16px;
  height: 16px;
}

.target-label {
  color: var(--text-primary);
  font-weight: 600;
  font-size: 0.95rem;
}

.target-hint {
  color: var(--text-muted);
  font-size: 0.78rem;
}

.target-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 4px;
}

.target-count {
  color: var(--text-muted);
  font-size: 0.85rem;
  background: var(--bg-hover);
  padding: 2px 10px;
  border-radius: 999px;
}

.link-btn {
  background: none;
  border: none;
  color: var(--accent-primary);
  font-size: 0.8rem;
  cursor: pointer;
  padding: 2px 6px;
}

.link-btn:hover {
  text-decoration: underline;
}

.target-list {
  margin-top: 8px;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  padding: 8px;
  max-height: 220px;
  overflow-y: auto;
  background: var(--bg-hover);
}

.empty {
  color: var(--text-muted);
  font-size: 0.85rem;
  padding: 8px;
}

.player-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 8px;
  font-size: 0.85rem;
  border-radius: var(--radius-sm);
}

.player-row:hover {
  background: var(--bg-card);
}

.p-name {
  font-weight: 600;
  color: var(--text-primary);
  min-width: 90px;
}

.p-ip {
  color: var(--text-muted);
  font-family: monospace;
  min-width: 110px;
}

.p-geo {
  color: var(--text-muted);
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── 判定标签 ── */
.badge {
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
  white-space: nowrap;
}

.status-normal { background: rgba(34, 197, 94, 0.15); color: #22c55e; }
.status-proxy { background: rgba(239, 68, 68, 0.15); color: #ef4444; }
.status-unknown { background: rgba(245, 158, 11, 0.15); color: #f59e0b; }
.status-unavailable { background: rgba(107, 114, 128, 0.15); color: #9ca3af; }
.status-pending { background: rgba(59, 130, 246, 0.15); color: #3b82f6; }
.status-disabled { background: rgba(107, 114, 128, 0.15); color: #9ca3af; }

/* ── 操作区 ── */
.action-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 14px 0;
  border-bottom: 1px solid var(--border-light);
}

.action-row:last-child {
  border-bottom: none;
}

.action-info {
  display: flex;
  flex-direction: column;
  gap: 3px;
  flex: 1;
  min-width: 0;
}

.action-label {
  color: var(--text-primary);
  font-weight: 600;
  font-size: 0.95rem;
}

.action-hint {
  color: var(--text-muted);
  font-size: 0.8rem;
  line-height: 1.4;
}

.hint-warn {
  color: #f59e0b;
}

.link-inline {
  color: var(--accent-primary);
  cursor: pointer;
  text-decoration: underline;
}

.action-btn {
  flex-shrink: 0;
  padding: 8px 20px;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  border: none;
  transition: all 0.2s ease;
}

.action-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-danger {
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.btn-danger:hover:not(:disabled) {
  background: rgba(239, 68, 68, 0.25);
  border-color: rgba(239, 68, 68, 0.5);
}

.btn-warning {
  background: rgba(245, 158, 11, 0.15);
  color: #f59e0b;
  border: 1px solid rgba(245, 158, 11, 0.3);
}

.btn-warning:hover:not(:disabled) {
  background: rgba(245, 158, 11, 0.25);
  border-color: rgba(245, 158, 11, 0.5);
}

/* ── 开关 ── */
.switch {
  position: relative;
  display: inline-block;
  width: 48px;
  height: 26px;
  flex-shrink: 0;
}

.switch input {
  opacity: 0;
  width: 0;
  height: 0;
}

.slider {
  position: absolute;
  cursor: pointer;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: var(--bg-hover);
  border: 2px solid var(--border-color);
  border-radius: 26px;
  transition: all 0.3s ease;
}

.slider::before {
  content: '';
  position: absolute;
  height: 18px;
  width: 18px;
  left: 2px;
  bottom: 2px;
  background: var(--text-muted);
  border-radius: 50%;
  transition: all 0.3s ease;
}

.switch input:checked + .slider {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
}

.switch input:checked + .slider::before {
  transform: translateX(22px);
  background: white;
}

/* ── 状态汇总 ── */
.status-summary {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}

.summary-item {
  font-size: 0.85rem;
  color: var(--text-muted);
  background: var(--bg-hover);
  padding: 4px 12px;
  border-radius: 999px;
}

.summary-ok {
  color: #22c55e;
}

/* ── 高级设置 ── */
.details-summary {
  cursor: pointer;
  color: var(--accent-primary);
  font-size: 0.9rem;
  margin-bottom: 8px;
}

.toggle-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px 0;
  border-bottom: 1px solid var(--border-light);
}

.toggle-row:last-of-type {
  border-bottom: none;
}

.toggle-label-wrap {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
  min-width: 0;
}

.toggle-label {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.95rem;
}

.toggle-hint {
  color: var(--text-muted);
  font-size: 0.8rem;
  line-height: 1.4;
}

.field-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 0;
}

.field-label {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.9rem;
}

.field-input {
  background: var(--bg-hover);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  padding: 8px 12px;
  font-size: 0.9rem;
  width: 100%;
  box-sizing: border-box;
}

.field-input-sm {
  width: 140px;
}

.field-hint {
  color: var(--text-muted);
  font-size: 0.78rem;
}

/* ── Toast ── */
.toast {
  position: fixed;
  top: 20px;
  right: 20px;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 18px;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  z-index: 2000;
  box-shadow: var(--shadow-lg);
}

.toast-success {
  background: rgba(34, 197, 94, 0.15);
  color: var(--accent-secondary);
  border: 1px solid rgba(34, 197, 94, 0.3);
}

.toast-error {
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.toast-enter-active,
.toast-leave-active {
  transition: all 0.3s ease;
}

.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(-10px);
}
</style>
