<script setup>
import { ref, computed, nextTick, onMounted } from 'vue'
import { get, post } from '../../../utils/api.js'
import GradientText from '../tools/GradientText.vue'
import { loadItemData } from '../../../api/itemDataApi.js'

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

// 面板级行尾空格覆盖：{ 面板名: 空格数 }；缺省 = 使用全局 spacerWidth
const panelSpacers = ref({})

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

// 当前面板行尾空格（双向绑定；空 = 回退全局）
const panelSpacerVal = computed({
  get: () => panelSpacers.value[activePanel.value] ?? '',
  set: (v) => {
    if (v === '' || v === null || v === undefined || !Number.isFinite(Number(v)) || Number(v) < 0) {
      delete panelSpacers.value[activePanel.value]
    } else {
      panelSpacers.value[activePanel.value] = Math.min(500, Math.round(Number(v)))
    }
    autoSave()
  }
})

// 预览用：当前面板实际生效的空格数
const activeSpacer = computed(() => panelSpacers.value[activePanel.value] ?? spacerWidth.value)

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
        panels: payloadPanels,
        panelSpacers: { ...panelSpacers.value }
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
  delete panelSpacers.value[name]
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

// ═══════════ 行拖拽排序（原生 HTML5 DnD，仅 ⠿ 把手可拖起） ═══════════
const dragIndex = ref(-1)
const dragOverIndex = ref(-1)
const dragOverPos = ref('') // 'before' | 'after'

/**
 * 判定事件目标是否位于交互元素上（文本框/输入框/按钮/下拉框）。
 * 命中则绝不判定拖拽（不启动、不显示指示线、不执行移动），
 * 且不 preventDefault，保持文本框默认行为（文本选择/输入/拖放）。
 */
const isInteractiveTarget = (e) => {
  const el = e.target
  // 文本节点等非元素目标一律视为交互区，避免误判
  if (!el || el.nodeType !== 1) return true
  return !!el.closest('textarea, input, button, select')
}

const onDragStart = (i, e) => {
  // 仅 ⠿ 把手可启动（draggable 只在把手上）；防御性排除交互元素
  if (isInteractiveTarget(e)) { e.preventDefault(); return }
  dragIndex.value = i
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', String(i))
  }
}

const onDragOver = (i, e) => {
  if (dragIndex.value < 0 || dragIndex.value === i) return
  // 输入框/控件上不判定（先于 preventDefault，保持文本框默认行为）
  if (isInteractiveTarget(e)) return
  e.preventDefault()
  // 鼠标落在目标行上半 → 插到其前；下半 → 插到其后
  const rect = e.currentTarget.getBoundingClientRect()
  dragOverPos.value = e.clientY < rect.top + rect.height / 2 ? 'before' : 'after'
  dragOverIndex.value = i
}

const onDrop = (i, e) => {
  const from = dragIndex.value
  const pos = dragOverPos.value
  dragIndex.value = -1
  dragOverIndex.value = -1
  dragOverPos.value = ''
  if (from < 0 || from === i) return
  // 在输入框/控件元素上释放时不执行移动（不 preventDefault）
  if (isInteractiveTarget(e)) return
  e.preventDefault()
  const rows = panels.value[activePanel.value]
  const [item] = rows.splice(from, 1)
  let target = i
  if (from < i) target = i - 1 // 移除前方元素后目标索引前移
  if (pos === 'after') target += 1
  rows.splice(target, 0, item)
  activeLineIndex.value = target
  autoSave()
}

const onDragEnd = () => {
  dragIndex.value = -1
  dragOverIndex.value = -1
  dragOverPos.value = ''
}

// ═══════════ 彩虹字插入（右侧 GradientText 组件生成代码 → 插入当前行光标位置） ═══════════
const insertGradientCode = (code) => {
  if (activeLineIndex.value < 0) {
    error.value = '请先点击左侧列表中的一行，再插入彩虹字'
    return
  }
  const token = code || ''
  if (!token) return
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
    panelSpacers.value = (data.panelSpacers && typeof data.panelSpacers === 'object') ? { ...data.panelSpacers } : {}
    // 强制 default 面板存在
    if (!panels.value.default) panels.value.default = DEFAULT_ROWS().map(r => ({ ...r }))
    activePanel.value = 'default'
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  ready = true
  loading.value = false
}

onMounted(async () => {
  fetchConfig()
  // 异步加载物品数据（ID.json），用于预览图标/名称；失败不影响其余功能
  try { itemData.value = await loadItemData() } catch { itemData.value = null }
})

// ═══════════ 物品数据（预览图标/名称，ID.json 异步加载） ═══════════
const itemData = ref(null)

const itemNameOf = (id) => {
  const it = itemData.value?.list?.find(i => i.id === id)
  return it ? (it.cn || it.english || `物品(${id})`) : `物品(${id})`
}

/** 图标加载失败回退：本地 → wiki（按英文名） → 隐藏 */
const onItemIconError = (seg) => {
  if (!seg.fallback) {
    seg.fallback = true
    const it = itemData.value?.list?.find(i => i.id === seg.itemId)
    seg.icon = (it && it.english) ? `https://terraria.wiki.gg/images/${it.english.replace(/\s+/g, '_')}.png` : ''
  } else {
    seg.icon = ''
  }
}

// ═══════════ 实时预览（当前面板，转义 [c/色值:文字] 颜色与 [i:ID] 物品） ═══════════
const TAG_RE = /\[([^\]]*)\]/g

/** 把 Terraria 富文本解析为片段：[c/色:字] → color，[i:ID]/[i/sX:ID] → item，其余原样 */
const parseRichText = (text) => {
  const segs = []
  TAG_RE.lastIndex = 0
  let last = 0, m
  while ((m = TAG_RE.exec(text))) {
    if (m.index > last) segs.push({ type: 'text', text: text.slice(last, m.index) })
    const inner = m[1]
    if (inner.startsWith('c/')) {
      const rest = inner.slice(2)
      const ci = rest.indexOf(':')
      if (ci > 0) {
        const hex = rest.slice(0, ci).trim()
        if (/^[0-9a-fA-F]{3}([0-9a-fA-F]{3})?$/.test(hex)) {
          const color = '#' + (hex.length === 3 ? [...hex].map(ch => ch + ch).join('') : hex).toLowerCase()
          segs.push({ type: 'color', color, text: rest.slice(ci + 1) })
          last = TAG_RE.lastIndex
          continue
        }
      }
    } else if (inner.startsWith('i:') || inner.startsWith('i/s') || inner.startsWith('i/p')) {
      const rest = inner.startsWith('i:') ? inner.slice(2).trim() : inner.slice(2).trim()
      // 兼容 [i:ID]、[i/sX:ID|name]、[i/pX:ID|name]（i/s5:757 → s5:757 → 757）
      const mm = rest.match(/^(?:s|p)\d*:(.*)$/)
      const raw = (mm ? mm[1] : rest).trim()
      const id = parseInt(raw, 10)
      if (!isNaN(id) && id > 0) {
        segs.push({ type: 'item', itemId: id, icon: `/assets/img/img/Item_${id}.png`, fallback: false })
        last = TAG_RE.lastIndex
        continue
      }
    }
    // 其他标签（[n/] [g/] 等）或无法解析的内容：原样文本
    segs.push({ type: 'text', text: m[0] })
    last = TAG_RE.lastIndex
  }
  if (last < text.length) segs.push({ type: 'text', text: text.slice(last) })
  return segs
}

const previewSegments = computed(() => {
  return currentRows.value.map((row, rowIdx) => {
    let t = row.text || ''
    for (const it of INTERPOLATIONS) {
      t = t.split(it.token).join(it.preview)
    }
    return {
      rowIdx,
      spacer: '·'.repeat(Math.max(0, Math.min(12, Number(activeSpacer.value) / 5 || 0))),
      segments: parseRichText(t)
    }
  })
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
      <!-- 顶部总览（100% 宽）：状态面板总览 + 启用按钮 + 次要设置 -->
      <div class="section-card overview-card">
        <div class="overview-row">
          <div class="overview-info">
            <h3>状态面板</h3>
            <p class="section-desc">
              在客户端固定屏幕位置显示持久文本框，纯服务端实现，所有原版客户端可见。
              玩家用 <code>/st</code> 查看面板列表、<code>/st 面板名</code> 切换查看（内存态）、<code>/st on|off</code> 开关。
              <code>default</code> 面板强制存在且不可删除。
            </p>
          </div>
          <div class="overview-toggle">
            <span class="toggle-label">启用面板</span>
            <label class="switch">
              <input type="checkbox" v-model="enabled" @change="autoSave" />
              <span class="slider"></span>
            </label>
          </div>
        </div>

        <div class="overview-sub">
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
      </div>

      <!-- 当前面板行列表（左）+ 彩虹字设计器（右） -->
      <div class="section-card">
        <div class="card-head">
          <h3>面板内容：{{ activePanel }}</h3>
          <div class="head-actions">
            <button class="ghost-btn" @click="resetTemplate">恢复默认模板</button>
            <button class="ghost-btn accent" @click="addLine">+ 添加行</button>
          </div>
        </div>
        <p class="section-desc">
          每行支持插值并按其更新间隔（帧数，60=1秒）刷新；每行渲染后自动追加行尾空格。
          <strong>按住 ⠿ 把手拖动即可调整顺序</strong>（文本框内拖动不受影响）。
        </p>

        <div class="panel-content-layout">
          <!-- 左栏：面板选择 + 行编辑 -->
          <div class="lines-column">
            <div class="panel-tabs-wrap">
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
              <div class="panel-add">
                <input v-model="newPanelName" class="new-panel-input" placeholder="新面板名称" @keyup.enter="addPanel" />
                <button class="ghost-btn accent" @click="addPanel">+ 添加面板</button>
              </div>
            </div>

            <!-- 面板级行尾空格（留空 = 使用全局）：创建面板区域下方 -->
            <div class="interval-row panel-spacer-row">
              <span class="toggle-label">本面板行尾空格</span>
              <div class="number-control">
                <button class="num-btn" @click="panelSpacerVal = Math.max(0, ((panelSpacers[activePanel] ?? spacerWidth) - 5))">−</button>
                <input
                  type="number"
                  v-model.number="panelSpacerVal"
                  min="0"
                  max="500"
                  step="5"
                  class="num-input"
                  :placeholder="`全局 ${spacerWidth}`"
                  title="该面板每行行尾补空格数；留空 = 使用全局设置"
                />
                <button class="num-btn" @click="panelSpacerVal = Math.min(500, ((panelSpacers[activePanel] ?? spacerWidth) + 5))">+</button>
              </div>
              <span class="interval-desc">留空/清除 = 使用全局（{{ spacerWidth }}）；每个面板可独立调整</span>
            </div>

            <div v-if="currentRows.length === 0" class="empty-hint">该面板暂无内容行，点击「+ 添加行」开始配置</div>

            <div
              v-for="(line, i) in currentRows"
              :key="activePanel + '-' + i"
              class="line-card"
              :class="{
                active: activeLineIndex === i,
                dragging: dragIndex === i,
                'drag-over-before': dragOverIndex === i && dragOverPos === 'before',
                'drag-over-after': dragOverIndex === i && dragOverPos === 'after'
              }"
              @dragover="onDragOver(i, $event)"
              @drop="onDrop(i, $event)"
              @dragend="onDragEnd"
              @click="focusLine(i)"
            >
              <div class="line-head">
                <span
                  class="drag-handle"
                  title="按住拖动排序"
                  draggable="true"
                  @dragstart="onDragStart(i, $event)"
                >⠿</span>
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

          <!-- 右栏：预览 + 彩虹字编辑器 -->
          <div class="rainbow-column">
            <div class="mini-card preview-card">
              <div class="mini-card-head">
                <span class="mini-title">预览（{{ activePanel }}）</span>
              </div>
              <p class="mini-desc">
                插值以示例值展示（真实游戏中动态替换）；「·」示意行尾空格数量；<code>[c/]</code> 颜色与 <code>[i:]</code> 物品已转义。
              </p>
              <div v-if="previewSegments.length" class="preview-box">
                <div v-for="row in previewSegments" :key="row.rowIdx" class="preview-line">
                  <template v-for="(seg, si) in row.segments" :key="si">
                    <span v-if="seg.type === 'color'" class="preview-color" :style="{ color: seg.color }">{{ seg.text }}</span>
                    <span v-else-if="seg.type === 'item'" class="preview-item" :title="itemNameOf(seg.itemId)">
                      <img v-if="seg.icon" :src="seg.icon" :alt="itemNameOf(seg.itemId)" class="preview-item-icon" @error="onItemIconError(seg)" />
                    </span>
                    <span v-else>{{ seg.text }}</span>
                  </template>
                  <span v-if="row.spacer" class="preview-spacer">{{ row.spacer }}</span>
                </div>
              </div>
              <div v-else class="preview-box preview-empty">（空）</div>
            </div>

            <!-- 彩虹字编辑器（复用 GradientText，点击「插入当前行」把代码插入左侧选中行） -->
            <GradientText embedded insertable @insert="insertGradientCode" />
          </div>
        </div>
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
.settings-content { max-width: 1100px; }
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

/* ── 顶部总览（100% 宽） ── */
.overview-row { display: flex; align-items: flex-start; justify-content: space-between; gap: 24px; }
.overview-info { flex: 1; min-width: 0; }
.overview-info h3 { margin: 0 0 4px 0; color: var(--text-primary); font-size: 1.1rem; font-weight: 600; }
.overview-info .section-desc { margin: 0; }
.overview-toggle { display: flex; align-items: center; gap: 10px; flex-shrink: 0; padding-top: 4px; }
.overview-sub { margin-top: 16px; border-top: 1px solid var(--border-light); padding-top: 4px; }
.overview-sub .interval-row,
.overview-sub .toggle-row { border-bottom: none; }

/* ── 双栏布局（左编辑 + 右预览/彩虹字） ── */
.panel-content-layout { display: flex; gap: 20px; align-items: flex-start; }
.lines-column { flex: 1; min-width: 0; }
.rainbow-column {
  width: 360px; flex-shrink: 0; position: sticky; top: 20px;
  display: flex; flex-direction: column; gap: 16px;
}
@media (max-width: 1180px) {
  .panel-content-layout { flex-direction: column; }
  .rainbow-column { width: 100%; position: static; }
  .overview-row { flex-direction: column; }
}

/* ── 左栏面板选择条 ── */
.panel-tabs-wrap {
  display: flex; align-items: center; justify-content: space-between;
  gap: 10px; flex-wrap: wrap; margin-bottom: 10px;
}
.panel-add { display: flex; gap: 8px; align-items: center; }
.panel-tip { margin-bottom: 12px; }

/* ── 右栏迷你卡片（预览） ── */
.mini-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-lg);
  padding: 14px;
}
.mini-card-head { display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px; }
.mini-title { font-size: 0.95rem; font-weight: 700; color: var(--text-primary); }
.mini-desc { margin: 0 0 10px 0; color: var(--text-muted); font-size: 0.78rem; line-height: 1.6; }
.mini-desc code {
  color: var(--accent-primary); background: var(--bg-tertiary);
  border-radius: 4px; padding: 1px 5px; font-size: 0.75rem;
}

/* ── 拖拽排序（仅 ⠿ 把手可拖起） ── */
.drag-handle {
  color: var(--text-muted); font-size: 0.95rem; cursor: grab;
  user-select: none; padding: 0 3px;
}
.drag-handle:active { cursor: grabbing; }
.line-card.dragging { opacity: 0.45; border-style: dashed; }
.line-card.drag-over-before { box-shadow: 0 -3px 0 0 var(--accent-primary); }
.line-card.drag-over-after { box-shadow: 0 3px 0 0 var(--accent-primary); }
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
.preview-line { min-height: 1.4em; }
.preview-line + .preview-line { margin-top: 4px; }
.preview-color { font-weight: 700; }
.preview-item {
  display: inline-flex; align-items: center; gap: 4px;
  vertical-align: middle; margin: 0 2px;
}
.preview-item-icon { width: 18px; height: 18px; image-rendering: pixelated; }
.preview-spacer { color: var(--text-muted); opacity: 0.55; user-select: none; }
.preview-empty { color: var(--text-muted); }

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
