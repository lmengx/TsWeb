<script setup>
import { ref, computed, nextTick, onMounted } from 'vue'
import { get, post } from '../../../utils/api.js'

// ═══════════ 插值字典（前端展示 + 点击插入，无需手打） ═══════════
const INTERPOLATIONS = [
  { group: '玩家', token: '{PlayerName}', label: '玩家名称', preview: 'Steve' },
  { group: '玩家', token: '{PlayerGroupName}', label: '所在组', preview: 'guest' },
  { group: '玩家', token: '{PlayerLife}', label: '生命值', preview: '500' },
  { group: '玩家', token: '{PlayerMana}', label: '魔力值', preview: '200' },
  { group: '玩家', token: '{PlayerLifeMax}', label: '最大生命', preview: '500' },
  { group: '玩家', token: '{PlayerManaMax}', label: '最大魔力', preview: '200' },
  { group: '玩家', token: '{PlayerLuck}', label: '幸运值', preview: '0.1' },
  { group: '玩家', token: '{PlayerCoordinateX}', label: 'X 坐标', preview: '200' },
  { group: '玩家', token: '{PlayerCoordinateY}', label: 'Y 坐标', preview: '300' },
  { group: '玩家', token: '{PlayerCurrentRegion}', label: '所在区域', preview: '主城' },
  { group: '玩家', token: '{IsPlayerAlive}', label: '存活状态', preview: '存活' },
  { group: '玩家', token: '{RespawnTimer}', label: '重生倒计时', preview: '5' },
  { group: '玩家', token: '{CurrentBiomes}', label: '当前群系', preview: '[c/008000:森林]' },
  { group: '服务器', token: '{OnlinePlayersCount}', label: '本服在线数', preview: '12' },
  { group: '服务器', token: '{OnlinePlayersList}', label: '本服在线列表', preview: 'A,B,C' },
  { group: '服务器', token: '{WorldName}', label: '世界名称', preview: '开荒服世界' },
  { group: '服务器', token: '{CurrentTime}', label: '游戏内时间', preview: '12:00' },
  { group: '渔夫任务', token: '{AnglerQuestFishName}', label: '任务鱼名称', preview: '金鲤' },
  { group: '渔夫任务', token: '{AnglerQuestFishID}', label: '任务鱼 ID', preview: '2611' },
  { group: '渔夫任务', token: '{AnglerQuestFishingBiome}', label: '钓鱼点', preview: '丛林' },
  { group: '渔夫任务', token: '{AnglerQuestCompleted}', label: '任务完成状态', preview: '未完成' },
  { group: '全服', token: '{AllOnlineCount}', label: '全服在线数', preview: '36' },
  { group: '时间', token: '{SystemTime}', label: '系统时间（服务器）', preview: '14:30' }
  // 注：{RealWorldTime} 与 {SystemTime} 相同（均为服务器本地时间 HH:mm），仅保留代码兼容，不再展示
]

const INTERPOLATION_GROUPS = ['玩家', '服务器', '渔夫任务', '全服', '时间']

const DEFAULT_ROWS = () => [
  { text: '[i:757][c/f15642:开荒服]', updateInterval: 600 },
  { text: '在线人数：{OnlinePlayersCount}人', updateInterval: 60 },
  { text: '全服在线：{AllOnlineCount}人', updateInterval: 60 },
  { text: '系统时间：{SystemTime}', updateInterval: 60 }
]

// 命令保留字：不允许用作面板名（与 /st on/off 冲突）
const RESERVED_NAMES = ['on', 'off', 'show', 'hide']

// ═══════════ 表单状态 ═══════════
const loading = ref(true)
const error = ref('')
const success = ref('')
let saveTimer = null
let ready = false

const enabled = ref(true)
const spacerWidth = ref(60)
const logLevel = ref('INFO')
const LOG_LEVELS = ['NONE', 'ERROR', 'WARNING', 'INFO', 'DEBUG']

// 多面板：{ 面板名: [{ text, updateInterval }] }，default 强制存在
const panels = ref({})
const activePanel = ref('default')
const newPanelName = ref('')

// 当前编辑行（用于插值插入定位）
const activeLineIndex = ref(-1)
const lineTextareas = ref([])
const activeGroup = ref('玩家')

const panelNames = computed(() => {
  const names = Object.keys(panels.value)
  // default 永远排最前
  return ['default', ...names.filter(n => n !== 'default')]
})

const currentRows = computed(() => panels.value[activePanel.value] || [])

// ═══════════ 自动保存 ═══════════
const autoSave = () => {
  if (!ready) return
  clearTimeout(saveTimer)
  saveTimer = setTimeout(async () => {
    error.value = ''
    success.value = ''
    try {
      const payloadPanels = {}
      for (const [name, rows] of Object.entries(panels.value)) {
        payloadPanels[name] = (rows || []).map(r => ({
          typeName: 'DynamicText',
          text: r.text || '',
          updateInterval: Number(r.updateInterval) >= 1 ? Number(r.updateInterval) : 60
        }))
      }
      const res = await post('/api/config/statuspanel', {
        enabled: enabled.value,
        spacerWidth: Number(spacerWidth.value) || 0,
        logLevel: logLevel.value,
        panels: payloadPanels
      })
      const data = await res.json()
      if (data.status === '200' || data.response) {
        success.value = '已保存'
        setTimeout(() => { success.value = '' }, 1500)
      } else {
        error.value = data.error || '保存失败'
      }
    } catch (err) {
      error.value = '保存失败: ' + err.message
    }
  }, 500)
}

// ═══════════ 面板操作 ═══════════
const selectPanel = (name) => {
  activePanel.value = name
  activeLineIndex.value = -1
}

const addPanel = () => {
  const name = newPanelName.value.trim()
  if (!name) { error.value = '请输入面板名称'; return }
  if (RESERVED_NAMES.includes(name.toLowerCase())) { error.value = `「${name}」为命令保留字，不能用作面板名`; return }
  if (panels.value[name]) { error.value = `面板「${name}」已存在`; return }
  panels.value[name] = []
  activePanel.value = name
  activeLineIndex.value = -1
  newPanelName.value = ''
  autoSave()
}

const removePanel = (name) => {
  if (name === 'default') return
  if (!window.confirm(`确定删除面板「${name}」？`)) return
  const next = Object.fromEntries(Object.entries(panels.value).filter(([k]) => k !== name))
  panels.value = next
  if (activePanel.value === name) selectPanel('default')
  autoSave()
}

// ═══════════ 行操作（作用于当前面板） ═══════════
const addLine = () => {
  panels.value[activePanel.value].push({ text: '', updateInterval: 60 })
  autoSave()
}

const removeLine = (i) => {
  panels.value[activePanel.value].splice(i, 1)
  if (activeLineIndex.value >= panels.value[activePanel.value].length) activeLineIndex.value = -1
  autoSave()
}

const moveLine = (i, dir) => {
  const rows = panels.value[activePanel.value]
  const j = i + dir
  if (j < 0 || j >= rows.length) return
  const t = rows[i]
  rows[i] = rows[j]
  rows[j] = t
  activeLineIndex.value = j
  autoSave()
}

const resetTemplate = () => {
  panels.value[activePanel.value] = DEFAULT_ROWS().map(r => ({ ...r }))
  autoSave()
}

// ═══════════ 插值插入（光标位置） ═══════════
const focusLine = (i) => {
  activeLineIndex.value = i
}

const insertToken = (token) => {
  if (activeLineIndex.value < 0) return
  const line = panels.value[activePanel.value][activeLineIndex.value]
  if (!line) return
  const el = lineTextareas.value[activeLineIndex.value]
  const start = (el && typeof el.selectionStart === 'number') ? el.selectionStart : line.text.length
  const end = (el && typeof el.selectionEnd === 'number') ? el.selectionEnd : line.text.length
  const before = line.text.slice(0, start)
  const after = line.text.slice(end)
  line.text = before + token + after
  nextTick(() => {
    if (el) {
      el.focus()
      const pos = start + token.length
      el.setSelectionRange(pos, pos)
    }
  })
  autoSave()
}

// ═══════════ 加载配置 ═══════════
const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/statuspanel')
    const data = await res.json()
    if (data.enabled !== undefined) enabled.value = !!data.enabled
    if (data.spacerWidth !== undefined) spacerWidth.value = data.spacerWidth
    if (data.logLevel) logLevel.value = data.logLevel
    if (data.panels && typeof data.panels === 'object') {
      const loaded = {}
      for (const [name, rows] of Object.entries(data.panels)) {
        loaded[name] = (Array.isArray(rows) ? rows : []).map(s => ({
          text: s.text || '',
          updateInterval: s.updateInterval ?? 60
        }))
      }
      panels.value = loaded
    } else {
      panels.value = {}
    }
    // 强制 default 面板存在
    if (!panels.value.default) panels.value.default = DEFAULT_ROWS().map(r => ({ ...r }))
    activePanel.value = 'default'
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  ready = true
  loading.value = false
}

onMounted(fetchConfig)

// ═══════════ 实时预览（当前面板） ═══════════
const previewText = computed(() => {
  return currentRows.value.map(row => {
    let t = row.text || ''
    for (const it of INTERPOLATIONS) {
      t = t.split(it.token).join(it.preview)
    }
    return t + ('·'.repeat(Math.max(0, Math.min(12, Number(spacerWidth.value) / 5 || 0))))
  }).join('\n')
})

const activeGroupList = computed(() => {
  if (activeGroup.value === '全部') return INTERPOLATIONS
  return INTERPOLATIONS.filter(i => i.group === activeGroup.value)
})
</script>

<template>
  <div class="settings-page">
    <div v-if="loading" class="loading-state">
      <p>加载中...</p>
    </div>

    <div v-else class="settings-content">
      <!-- 全局设置 -->
      <div class="section-card">
        <h3>状态面板</h3>
        <p class="section-desc">
          在客户端固定屏幕位置显示持久文本框，纯服务端实现，所有原版客户端可见。
          可配置多个面板，玩家用 <code>/st</code> 查看面板列表、<code>/st 面板名</code> 切换查看（内存态）、<code>/st on|off</code> 开关。
          <code>default</code> 面板强制存在且不可删除。
        </p>

        <div class="toggle-row">
          <span class="toggle-label">启用面板</span>
          <span class="toggle-hint">关闭后停止向所有玩家发送面板文本</span>
          <label class="switch">
            <input type="checkbox" v-model="enabled" @change="autoSave" />
            <span class="slider"></span>
          </label>
        </div>

        <div class="interval-row">
          <span class="toggle-label">行尾空格宽度</span>
          <div class="number-control">
            <button class="num-btn" @click="spacerWidth = Math.max(0, (Number(spacerWidth) || 0) - 5); autoSave()">−</button>
            <input type="number" v-model.number="spacerWidth" min="0" max="500" step="5" class="num-input" title="每行行尾补空格数：越大文本块越宽，可视文字越靠近屏幕中部；屏幕越宽需越大" />
            <button class="num-btn" @click="spacerWidth = Math.min(500, (Number(spacerWidth) || 0) + 5); autoSave()">+</button>
          </div>
          <span class="interval-desc">文本块撑宽 → 客户端固定锚点居中（屏幕越宽需越大，过大偏出左屏）</span>
        </div>

        <div class="toggle-row">
          <span class="toggle-label">日志等级</span>
          <select v-model="logLevel" class="log-select" @change="autoSave">
            <option v-for="lv in LOG_LEVELS" :key="lv" :value="lv">{{ lv }}</option>
          </select>
          <span class="toggle-hint">兼容参考插件 LogLevel</span>
        </div>
      </div>

      <!-- 面板选择器 -->
      <div class="section-card">
        <div class="card-head">
          <h3>面板</h3>
          <div class="head-actions">
            <input v-model="newPanelName" class="new-panel-input" placeholder="新面板名称" @keyup.enter="addPanel" />
            <button class="ghost-btn accent" @click="addPanel">+ 添加面板</button>
          </div>
        </div>
        <p class="section-desc">
          <code>default</code> 不可删除；面板名不能是 <code>on/off/show/hide</code>（与 /st 命令冲突）。
        </p>

        <div class="panel-tabs">
          <span
            v-for="name in panelNames"
            :key="name"
            class="panel-tab"
            :class="{ active: activePanel === name, locked: name === 'default' }"
            @click="selectPanel(name)"
          >
            {{ name }}
            <button v-if="name !== 'default'" class="panel-del" @click.stop="removePanel(name)" title="删除面板">×</button>
          </span>
        </div>
      </div>

      <!-- 当前面板行列表 -->
      <div class="section-card">
        <div class="card-head">
          <h3>面板内容：{{ activePanel }}</h3>
          <div class="head-actions">
            <button class="ghost-btn" @click="resetTemplate">恢复默认模板</button>
            <button class="ghost-btn accent" @click="addLine">+ 添加行</button>
          </div>
        </div>
        <p class="section-desc">
          每行支持插值并按其更新间隔（帧数，60=1秒）刷新；不含插值的行内容保持不变。每行渲染后自动追加行尾空格。
        </p>

        <div v-if="currentRows.length === 0" class="empty-hint">该面板暂无内容行，点击「+ 添加行」开始配置</div>

        <div
          v-for="(line, i) in currentRows"
          :key="activePanel + '-' + i"
          class="line-card"
          :class="{ active: activeLineIndex === i }"
          @click="focusLine(i)"
        >
          <div class="line-head">
            <span class="line-index">{{ i + 1 }}</span>
            <span class="type-badge">动态文本</span>
            <span class="interval-label">更新间隔</span>
            <input type="number" v-model.number="line.updateInterval" min="1" max="6000" class="interval-input" @change="autoSave" title="帧数，60=1秒" />
            <span class="interval-unit">帧</span>
            <span class="flex-spacer"></span>
            <button class="mini-btn" :disabled="i === 0" @click.stop="moveLine(i, -1)" title="上移">↑</button>
            <button class="mini-btn" :disabled="i === currentRows.length - 1" @click.stop="moveLine(i, 1)" title="下移">↓</button>
            <button class="mini-btn danger" @click.stop="removeLine(i)" title="删除">×</button>
          </div>
          <textarea
            :ref="el => { lineTextareas[i] = el }"
            v-model="line.text"
            class="line-text"
            rows="2"
            placeholder="例如：在线人数：{OnlinePlayersCount}人"
            @input="autoSave"
            @focus="focusLine(i)"
          ></textarea>

          <!-- 插值填入面板 -->
          <div class="interpolate-panel" @click.stop>
            <div class="interp-head">点击插入插值（光标位置）</div>
            <div class="interp-groups">
              <button
                v-for="g in ['全部', ...INTERPOLATION_GROUPS]"
                :key="g"
                class="group-chip"
                :class="{ active: activeGroup === g }"
                @click="activeGroup = g"
              >{{ g }}</button>
            </div>
            <div class="interp-chips">
              <button
                v-for="it in activeGroupList"
                :key="it.token"
                class="interp-chip"
                :title="`${it.label}（示例：${it.preview}）`"
                @click="insertToken(it.token)"
              >
                <code>{{ it.token }}</code>
                <span class="interp-label">{{ it.label }}</span>
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- 实时预览 -->
      <div class="section-card">
        <h3>预览（{{ activePanel }}）</h3>
        <p class="section-desc">
          插值以示例值展示（真实游戏中动态替换）；「·」示意行尾空格数量。服务器名行可用
          <code>[i:物品ID]</code> 图标与 <code>[c/色值:文字]</code> 颜色。
        </p>
        <pre class="preview-box">{{ previewText || '（空）' }}</pre>
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
.settings-page { padding: 20px; width: 100%; }
.settings-content { max-width: 760px; }
.loading-state { text-align: center; padding: 60px; color: var(--text-muted); }

.section-card {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  margin-bottom: 20px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}
.section-card h3 { margin: 0 0 4px 0; color: var(--text-primary); font-size: 1.1rem; font-weight: 600; }
.section-desc { margin: 0 0 16px 0; color: var(--text-muted); font-size: 0.85rem; line-height: 1.6; }
.section-desc code {
  background: var(--bg-tertiary);
  border: 1px solid var(--border-light);
  border-radius: 4px;
  padding: 1px 6px;
  font-size: 0.8rem;
  color: var(--accent-primary);
}

.card-head { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 8px; }
.head-actions { display: flex; gap: 8px; align-items: center; }
.ghost-btn {
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  padding: 6px 12px;
  font-size: 0.85rem;
  cursor: pointer;
  transition: all 0.2s ease;
}
.ghost-btn:hover { border-color: var(--accent-primary); }
.ghost-btn.accent { color: var(--accent-primary); border-color: var(--accent-primary); }

/* ── 面板选择器 ── */
.new-panel-input {
  width: 120px; height: 30px; padding: 0 8px;
  background: var(--bg-tertiary); color: var(--text-primary);
  border: 1px solid var(--border-color); border-radius: var(--radius-sm);
  font-size: 0.85rem; outline: none; font-family: inherit;
}
.new-panel-input:focus { border-color: var(--accent-primary); }
.panel-tabs { display: flex; gap: 8px; flex-wrap: wrap; }
.panel-tab {
  display: inline-flex; align-items: center; gap: 6px;
  padding: 7px 14px; border-radius: 999px;
  background: var(--bg-tertiary); border: 1px solid var(--border-color);
  color: var(--text-secondary); font-size: 0.88rem; font-weight: 500;
  cursor: pointer; transition: all 0.15s ease; user-select: none;
}
.panel-tab:hover { border-color: var(--accent-primary); }
.panel-tab.active { color: var(--accent-primary); border-color: var(--accent-primary); background: var(--bg-hover); font-weight: 600; }
.panel-tab.locked { cursor: default; }
.panel-del {
  background: none; border: none; color: var(--accent-error);
  font-size: 0.95rem; line-height: 1; cursor: pointer; padding: 0 2px;
}

.toggle-row {
  display: flex; align-items: center; gap: 12px;
  padding: 12px 0; border-bottom: 1px solid var(--border-light);
}
.toggle-row:last-child { border-bottom: none; }
.toggle-label { color: var(--text-primary); font-weight: 500; font-size: 0.95rem; min-width: 88px; }
.toggle-hint { flex: 1; color: var(--text-muted); font-size: 0.8rem; }

.switch { position: relative; display: inline-block; width: 48px; height: 26px; flex-shrink: 0; }
.switch input { opacity: 0; width: 0; height: 0; }
.slider {
  position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0;
  background: var(--bg-hover); border: 2px solid var(--border-color); border-radius: 26px;
  transition: all 0.3s ease;
}
.slider::before {
  content: ''; position: absolute; height: 18px; width: 18px; left: 2px; bottom: 2px;
  background: var(--text-muted); border-radius: 50%; transition: all 0.3s ease;
}
.switch input:checked + .slider { background: var(--accent-primary); border-color: var(--accent-primary); }
.switch input:checked + .slider::before { transform: translateX(22px); background: white; }

.interval-row {
  display: flex; align-items: center; gap: 16px; padding: 14px 0;
  border-bottom: 1px solid var(--border-light);
}
.number-control { display: flex; align-items: center; gap: 8px; }
.num-btn {
  width: 30px; height: 30px; background: var(--bg-tertiary);
  border: 2px solid var(--border-color); border-radius: var(--radius-sm);
  color: var(--text-primary); font-size: 1rem; cursor: pointer; transition: all 0.2s ease;
}
.num-btn:hover { border-color: var(--accent-primary); }
.num-input {
  width: 72px; height: 30px; text-align: center; font-size: 0.95rem; font-weight: 700;
  border: 2px solid var(--border-color); border-radius: var(--radius-sm); outline: none;
  color: var(--text-primary); background: var(--bg-tertiary); transition: border-color 0.15s ease;
  -moz-appearance: textfield; font-family: inherit;
}
.num-input::-webkit-outer-spin-button, .num-input::-webkit-inner-spin-button { -webkit-appearance: none; margin: 0; }
.num-input:focus { border-color: var(--accent-primary); }
.interval-desc { color: var(--text-muted); font-size: 0.8rem; flex: 1; }

.log-select {
  background: var(--bg-tertiary); color: var(--text-primary);
  border: 2px solid var(--border-color); border-radius: var(--radius-sm);
  padding: 5px 8px; font-size: 0.85rem; outline: none;
}

/* ── 行卡片 ── */
.empty-hint { color: var(--text-muted); font-size: 0.85rem; padding: 12px 0; }
.line-card {
  border: 1px solid var(--border-light);
  border-radius: var(--radius-lg);
  padding: 14px; margin-bottom: 12px;
  background: var(--bg-secondary);
  transition: border-color 0.2s ease;
}
.line-card.active { border-color: var(--accent-primary); }
.line-head { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
.line-index {
  width: 22px; height: 22px; border-radius: 50%; display: flex; align-items: center; justify-content: center;
  background: var(--bg-tertiary); color: var(--text-muted); font-size: 0.75rem; font-weight: 600;
}
.type-badge {
  background: var(--bg-tertiary);
  color: var(--accent-primary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 3px 8px;
  font-size: 0.78rem;
  font-weight: 600;
}
.interval-label { color: var(--text-muted); font-size: 0.78rem; margin-left: 8px; }
.interval-input {
  width: 64px; height: 26px; text-align: center; font-size: 0.85rem;
  border: 1px solid var(--border-color); border-radius: var(--radius-sm); outline: none;
  color: var(--text-primary); background: var(--bg-tertiary); font-family: inherit;
}
.interval-unit { color: var(--text-muted); font-size: 0.78rem; }
.flex-spacer { flex: 1; }
.mini-btn {
  width: 26px; height: 26px; background: var(--bg-tertiary);
  border: 1px solid var(--border-color); border-radius: var(--radius-sm);
  color: var(--text-primary); font-size: 0.9rem; cursor: pointer; transition: all 0.15s ease;
}
.mini-btn:hover:not(:disabled) { border-color: var(--accent-primary); }
.mini-btn:disabled { opacity: 0.35; cursor: not-allowed; }
.mini-btn.danger:hover { border-color: var(--accent-error); color: var(--accent-error); }
.line-text {
  width: 100%; box-sizing: border-box; resize: vertical;
  background: var(--bg-tertiary); color: var(--text-primary);
  border: 1px solid var(--border-color); border-radius: var(--radius-sm);
  padding: 8px 10px; font-size: 0.9rem; font-family: inherit; outline: none;
  transition: border-color 0.15s ease;
}
.line-text:focus { border-color: var(--accent-primary); }

/* ── 插值填入面板 ── */
.interpolate-panel {
  margin-top: 10px; padding: 10px; border-radius: var(--radius-sm);
  background: var(--bg-card); border: 1px dashed var(--border-color);
}
.interp-head { color: var(--text-muted); font-size: 0.75rem; margin-bottom: 8px; }
.interp-groups { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 8px; }
.group-chip {
  background: var(--bg-tertiary); border: 1px solid var(--border-color);
  border-radius: 999px; color: var(--text-muted); font-size: 0.75rem;
  padding: 3px 10px; cursor: pointer; transition: all 0.15s ease;
}
.group-chip.active { color: var(--accent-primary); border-color: var(--accent-primary); font-weight: 600; }
.interp-chips { display: flex; gap: 6px; flex-wrap: wrap; }
.interp-chip {
  display: inline-flex; align-items: center; gap: 6px;
  background: var(--bg-tertiary); border: 1px solid var(--border-color);
  border-radius: var(--radius-sm); padding: 3px 8px; cursor: pointer;
  transition: all 0.15s ease; font-size: 0.78rem;
}
.interp-chip:hover { border-color: var(--accent-primary); background: var(--bg-hover); }
.interp-chip code { color: var(--accent-primary); }
.interp-label { color: var(--text-muted); }

/* ── 预览 ── */
.preview-box {
  background: var(--bg-tertiary); border: 1px solid var(--border-light);
  border-radius: var(--radius-sm); padding: 14px; margin: 0;
  color: var(--text-primary); font-size: 0.9rem; line-height: 1.7;
  white-space: pre-wrap; font-family: inherit;
}

/* ── Toast ── */
.toast {
  position: fixed; top: 20px; right: 20px;
  padding: 12px 18px; border-radius: var(--radius-md); font-size: 0.9rem;
  z-index: 2000; box-shadow: var(--shadow-lg);
}
.toast-success { background: rgba(34, 197, 94, 0.15); color: var(--accent-secondary); border: 1px solid rgba(34, 197, 94, 0.3); }
.toast-error { background: rgba(239, 68, 68, 0.15); color: var(--accent-error); border: 1px solid rgba(239, 68, 68, 0.3); }
.toast-enter-active, .toast-leave-active { transition: all 0.3s ease; }
.toast-enter-from, .toast-leave-to { opacity: 0; transform: translateY(-10px); }
</style>
