<script setup>
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { getEmoteConfig, saveEmoteConfig, EMOTE_PRESETS } from '../../../api/emojiApi.js'
import Loading from '../../../components/Loading.vue'

const loading = ref(true)
const saving = ref(false)
const error = ref('')
const success = ref('')

const rules = ref([])

const loadConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const data = await getEmoteConfig()
    const emotes = data.emotes || []
    rules.value = emotes.map(r => {
      const preset = EMOTE_PRESETS.some(p => p.id === r.emojiId)
      return {
        emojiId: typeof r.emojiId === 'number' ? r.emojiId : (parseInt(r.emojiId) || 0),
        enabled: r.enabled !== false,
        commands: Array.isArray(r.commands) ? r.commands.filter(c => typeof c === 'string') : [],
        remark: r.remark || '',
        ignorePermission: !!r.ignorePermission,
        _customMode: !preset
      }
    })
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  loading.value = false
}

const save = async () => {
  if (saving.value) return
  saving.value = true
  error.value = ''
  success.value = ''
  try {
    const payload = {
      emotes: rules.value.map(r => ({
        emojiId: parseInt(r.emojiId) || 0,
        enabled: !!r.enabled,
        commands: (r.commands || []).map(c => c.trim()).filter(c => c !== ''),
        remark: (r.remark || '').trim(),
        ignorePermission: !!r.ignorePermission
      }))
    }
    const data = await saveEmoteConfig(payload)
    if (data.response === '配置已保存' || data.status === '200') {
      success.value = '配置已保存'
      setTimeout(() => { success.value = '' }, 2000)
    } else {
      error.value = data.error || '保存失败'
    }
  } catch (err) {
    error.value = '保存失败: ' + err.message
  }
  saving.value = false
}

// ===== 表情规则操作 =====
const addRule = () => {
  rules.value.push({
    emojiId: 0,
    enabled: true,
    commands: ['/heal'],
    remark: '',
    ignorePermission: false,
    _customMode: false
  })
}

const removeRule = (index) => {
  rules.value.splice(index, 1)
}

const addCommand = (rule) => {
  rule.commands.push('')
}

const removeCommand = (rule, index) => {
  rule.commands.splice(index, 1)
}

// ===== 表情搜索选择器 =====
// 编辑距离（Levenshtein），用于子串匹配结果过少时模糊补充
const levenshtein = (a, b) => {
  const m = a.length, n = b.length
  if (m === 0) return n
  if (n === 0) return m
  const dp = Array.from({ length: m + 1 }, () => new Array(n + 1).fill(0))
  for (let i = 0; i <= m; i++) dp[i][0] = i
  for (let j = 0; j <= n; j++) dp[0][j] = j
  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      dp[i][j] = Math.min(
        dp[i - 1][j] + 1,
        dp[i][j - 1] + 1,
        dp[i - 1][j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1)
      )
    }
  }
  return dp[m][n]
}

const activePicker = ref(-1)     // 当前展开面板的规则索引，-1 = 关闭
const searchKeyword = ref('')    // 搜索词（面板内共用）
const customIdInput = ref(null)  // 面板底部自定义 ID 输入（数字）
const pickerSearchRef = ref(null)

const openPicker = (index, rule) => {
  if (!rule.enabled) return
  activePicker.value = index
  searchKeyword.value = ''
  customIdInput.value = ''
  nextTick(() => {
    if (pickerSearchRef.value) pickerSearchRef.value.focus()
  })
}

const closePicker = () => { activePicker.value = -1 }

const selectPreset = (rule, preset) => {
  rule.emojiId = preset.id
  rule._customMode = false
  closePicker()
}

const confirmCustomId = (rule) => {
  const id = parseInt(customIdInput.value)
  if (isNaN(id) || id < 0) { customIdInput.value = ''; return }
  rule.emojiId = id
  rule._customMode = true
  closePicker()
}

const currentLabel = (rule) => {
  const p = EMOTE_PRESETS.find(x => x.id === rule.emojiId)
  return p ? `${p.id} · ${p.name}` : `自定义 ${rule.emojiId}`
}

// 搜索过滤：优先子串匹配（中文名/ID）；结果 < 5 时用编辑距离补充（距离 ≤ 3，最多 5 条）
const filteredPresets = computed(() => {
  const kw = searchKeyword.value.trim().toLowerCase()
  if (!kw) return EMOTE_PRESETS

  const substring = EMOTE_PRESETS.filter(p =>
    p.name.toLowerCase().includes(kw) || String(p.id).includes(kw)
  )

  if (substring.length >= 5) return substring

  const fuzzy = EMOTE_PRESETS
    .map(p => ({
      p,
      d: Math.min(levenshtein(kw, p.name.toLowerCase()), levenshtein(kw, String(p.id)))
    }))
    .filter(x => x.d <= 3 && !substring.some(s => s.id === x.p.id))
    .sort((a, b) => a.d - b.d || a.p.id - b.p.id)
    .slice(0, 5)
    .map(x => x.p)

  return [...substring, ...fuzzy]
})

// 点击面板外部关闭
const onDocClick = (e) => {
  if (!e.target.closest('.emoji-picker')) closePicker()
}

onMounted(() => {
  loadConfig()
  document.addEventListener('click', onDocClick)
})

onUnmounted(() => {
  document.removeEventListener('click', onDocClick)
})
</script>

<template>
  <div class="emoji-page">
    <div class="page-header">
      <h2>表情指令</h2>
      <p class="page-desc">玩家发送指定表情时，自动按顺序执行配置的指令</p>
    </div>

    <!-- 使用说明 -->
    <div class="info-card">
      <div class="info-title">💡 使用说明</div>
      <ul class="info-list">
        <li>玩家在聊天框输入表情名（如 <code>/爱心</code>、<code>/heart</code>）或按 <code>T</code> 打开表情菜单点击，都会触发对应规则。</li>
        <li>一个表情可以配置<strong>多组指令</strong>；触发时<strong>按顺序逐条执行</strong>（执行完一条再执行下一条）。</li>
        <li>每条指令以玩家身份执行（受权限检查），可填写任意存在的指令，例如 <code>/heal</code>、<code>/give {player} 3500 1</code>。</li>
        <li><code>{player}</code> 会自动替换为触发者名字；指令开头未写 <code>/</code> 或 <code>.</code> 会自动补 <code>/</code>。</li>
        <li>需要玩家拥有 <code>tshock.sendemoji</code> 权限才能发送表情（默认组自带）。</li>
        <li><strong>忽略权限</strong>：勾选后以玩家身份直接执行指令、不做权限检查，可用于让玩家通过表情使用原本无权使用的指令（请谨慎配置）。</li>
      </ul>
    </div>

    <Loading v-if="loading" text="加载中..." />

    <div v-else class="emoji-content">
      <!-- ===== 规则列表 ===== -->
      <div class="rule-list">
        <div
          v-for="(rule, index) in rules"
          :key="index"
          class="rule-card"
          :class="{ disabled: !rule.enabled }"
        >
          <div class="rule-header">
            <span class="rule-index">#{{ index + 1 }}</span>
            <label class="switch" title="启用/停用此规则">
              <input type="checkbox" v-model="rule.enabled" />
              <span class="slider"></span>
            </label>
            <button
              class="btn-remove"
              @click="removeRule(index)"
              title="删除此规则"
            >✕</button>
          </div>

          <div class="rule-body">
            <div class="form-row">
              <div class="form-group">
                <label class="form-label">触发表情</label>
                <div class="emoji-picker">
                  <!-- 当前选中显示 + 点击展开 -->
                  <div
                    class="picker-current"
                    :class="{ open: activePicker === index }"
                    :disabled="!rule.enabled"
                    @click="openPicker(index, rule)"
                  >
                    <span class="picker-current-label">{{ currentLabel(rule) }}</span>
                    <span class="picker-arrow">▾</span>
                  </div>

                  <!-- 搜索下拉面板 -->
                  <div v-if="activePicker === index" class="picker-panel" @click.stop>
                    <input
                      ref="pickerSearchRef"
                      v-model="searchKeyword"
                      class="form-input picker-search"
                      placeholder="搜索：中文名 / ID"
                    />
                    <div class="picker-list">
                      <div
                        v-for="p in filteredPresets"
                        :key="p.id"
                        class="picker-item"
                        :class="{ active: p.id === rule.emojiId }"
                        @click="selectPreset(rule, p)"
                      >
                        <span class="picker-item-id">{{ p.id }}</span>
                        <span class="picker-item-name">{{ p.name }}</span>
                        <span v-if="p.id === rule.emojiId" class="picker-item-check">✓</span>
                      </div>
                      <div v-if="filteredPresets.length === 0" class="picker-empty">
                        无匹配结果，可在下方输入自定义 ID
                      </div>
                    </div>
                    <div class="picker-custom">
                      <input
                        v-model.number="customIdInput"
                        type="number"
                        min="0"
                        max="150"
                        class="form-input picker-custom-input"
                        placeholder="自定义 ID (0~150)"
                        @keydown.enter="confirmCustomId(rule)"
                      />
                      <button class="btn-custom-ok" @click="confirmCustomId(rule)">确定</button>
                    </div>
                  </div>
                </div>
                <span v-if="rule._customMode" class="field-hint">自定义表情 ID（不在预设列表中）</span>
              </div>

              <div class="form-group">
                <label class="form-label">备注（可选）</label>
                <input
                  v-model="rule.remark"
                  class="form-input"
                  placeholder="如：爱心=回血"
                  :disabled="!rule.enabled"
                />
              </div>
            </div>

            <div class="perm-row">
              <label class="switch" title="跳过权限检查，以玩家身份直接执行">
                <input type="checkbox" v-model="rule.ignorePermission" :disabled="!rule.enabled" />
                <span class="slider"></span>
              </label>
              <div class="perm-text">
                <span class="perm-title">忽略权限</span>
                <span class="perm-hint">以玩家身份直接执行，不做权限检查（谨慎使用）</span>
              </div>
            </div>

            <div class="cmd-block">
              <label class="form-label">触发指令（按顺序执行）</label>
              <div
                v-for="(cmd, cmdIndex) in rule.commands"
                :key="cmdIndex"
                class="cmd-row"
              >
                <span class="cmd-order">{{ cmdIndex + 1 }}</span>
                <input
                  v-model="rule.commands[cmdIndex]"
                  class="form-input cmd-input"
                  placeholder="如 /heal 或 /give {player} 3500 1"
                  :disabled="!rule.enabled"
                />
                <button
                  class="btn-remove cmd-remove"
                  @click="removeCommand(rule, cmdIndex)"
                  title="删除此指令"
                >✕</button>
              </div>
              <button
                class="btn-add-small"
                @click="addCommand(rule)"
                :disabled="!rule.enabled"
              >＋ 添加指令</button>
            </div>
          </div>
        </div>
      </div>

      <button class="btn-add" @click="addRule">＋ 添加表情规则</button>

      <!-- 保存 -->
      <div class="save-bar">
        <button class="btn-save" :disabled="saving" @click="save">
          {{ saving ? '保存中...' : '保存配置' }}
        </button>
      </div>
    </div>

    <!-- Toast -->
    <Transition name="toast">
      <div v-if="success" class="toast toast-success">
        <svg class="toast-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18">
          <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/>
        </svg>
        <span>{{ success }}</span>
      </div>
    </Transition>
    <Transition name="toast">
      <div v-if="error" class="toast toast-error">
        <svg class="toast-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18">
          <path fill-rule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clip-rule="evenodd"/>
        </svg>
        <span>{{ error }}</span>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.emoji-page {
  padding: 20px;
  width: 100%;
}

.page-header {
  margin-bottom: 16px;
}

.page-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
}

.page-desc {
  margin: 4px 0 0 0;
  color: var(--text-muted);
  font-size: 0.88rem;
}

/* ===== 使用说明 ===== */
.info-card {
  background: rgba(99, 102, 241, 0.06);
  border: 1px solid rgba(99, 102, 241, 0.2);
  border-radius: var(--radius-lg);
  padding: 14px 18px;
  margin-bottom: 20px;
}

.info-title {
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--accent-primary);
  margin-bottom: 6px;
}

.info-list {
  margin: 0;
  padding-left: 18px;
  color: var(--text-secondary);
  font-size: 0.85rem;
  line-height: 1.7;
}

.info-list code {
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  padding: 1px 5px;
  font-size: 0.82rem;
  color: var(--accent-primary);
}

.info-list strong {
  color: var(--text-primary);
}

/* ===== 内容区 ===== */
.emoji-content {
  max-width: 860px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.rule-list {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.rule-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-md);
  padding: 18px;
  transition: opacity 0.2s;
}

.rule-card.disabled {
  opacity: 0.55;
}

.rule-header {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 14px;
}

.rule-index {
  font-weight: 700;
  font-size: 0.85rem;
  color: var(--accent-primary);
  flex: 1;
}

/* ===== 表单 ===== */
.form-row {
  display: flex;
  gap: 16px;
}

.form-group {
  flex: 1;
  margin-bottom: 12px;
}

.form-label {
  display: block;
  margin-bottom: 6px;
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-weight: 500;
}

.form-select,
.form-input {
  width: 100%;
  padding: 9px 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.9rem;
  outline: none;
  transition: border-color 0.2s;
  box-sizing: border-box;
}

.form-select:focus,
.form-input:focus {
  border-color: var(--accent-primary);
}

.form-select:disabled,
.form-input:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.form-select option {
  background: var(--bg-card);
  color: var(--text-primary);
}

.field-hint {
  display: block;
  font-size: 0.76rem;
  color: var(--text-muted);
  margin-top: 4px;
}

/* ===== 表情搜索选择器 ===== */
.emoji-picker {
  position: relative;
}

.picker-current {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 9px 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.9rem;
  cursor: pointer;
  transition: border-color 0.2s;
  user-select: none;
}

.picker-current:hover,
.picker-current.open {
  border-color: var(--accent-primary);
}

.picker-current-label {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.picker-arrow {
  color: var(--text-muted);
  font-size: 0.75rem;
  transition: transform 0.2s;
}

.picker-current.open .picker-arrow {
  transform: rotate(180deg);
}

.picker-panel {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  z-index: 100;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-lg);
  padding: 8px;
}

.picker-search {
  margin-bottom: 6px;
}

.picker-list {
  max-height: 220px;
  overflow-y: auto;
}

.picker-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 10px;
  border-radius: var(--radius-md);
  cursor: pointer;
  transition: background 0.15s;
}

.picker-item:hover {
  background: var(--bg-hover);
}

.picker-item.active {
  background: rgba(99, 102, 241, 0.12);
}

.picker-item-id {
  width: 34px;
  flex-shrink: 0;
  font-size: 0.78rem;
  font-weight: 700;
  color: var(--accent-primary);
}

.picker-item-name {
  flex: 1;
  font-size: 0.85rem;
  color: var(--text-primary);
}

.picker-item-check {
  color: var(--accent-primary);
  font-weight: 700;
}

.picker-empty {
  padding: 14px;
  text-align: center;
  color: var(--text-muted);
  font-size: 0.82rem;
}

.picker-custom {
  display: flex;
  gap: 6px;
  margin-top: 6px;
  padding-top: 8px;
  border-top: 1px solid var(--border-light);
}

.picker-custom-input {
  flex: 1;
}

.btn-custom-ok {
  flex-shrink: 0;
  padding: 0 16px;
  border: none;
  border-radius: var(--radius-md);
  background: var(--accent-primary);
  color: white;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.2s;
}

.btn-custom-ok:hover {
  opacity: 0.88;
}

/* ===== 忽略权限 ===== */
.perm-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 2px 0 14px;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
}

.perm-text {
  display: flex;
  flex-direction: column;
  gap: 1px;
}

.perm-title {
  font-size: 0.86rem;
  font-weight: 600;
  color: var(--text-primary);
}

.perm-hint {
  font-size: 0.75rem;
  color: var(--text-muted);
}

/* ===== 指令列表 ===== */
.cmd-block {
  margin-top: 4px;
}

.cmd-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.cmd-order {
  width: 22px;
  height: 22px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background: rgba(99, 102, 241, 0.12);
  color: var(--accent-primary);
  font-size: 0.72rem;
  font-weight: 700;
}

.cmd-input {
  flex: 1;
  font-family: 'Consolas', 'Menlo', monospace;
  font-size: 0.85rem !important;
}

.cmd-remove {
  flex-shrink: 0;
}

.btn-add-small {
  margin-top: 4px;
  padding: 7px 14px;
  border: 2px dashed var(--border-color);
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--text-muted);
  font-size: 0.85rem;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-add-small:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.btn-add-small:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

/* ===== 删除按钮 ===== */
.btn-remove {
  width: 26px;
  height: 26px;
  border: none;
  border-radius: 50%;
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.75rem;
  transition: all 0.2s;
  flex-shrink: 0;
}

.btn-remove:hover {
  background: #ef4444;
  color: white;
}

/* ===== 开关 ===== */
.switch {
  position: relative;
  display: inline-block;
  width: 46px;
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
  top: 0; left: 0; right: 0; bottom: 0;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 26px;
  transition: all 0.3s ease;
}

.slider::before {
  content: '';
  position: absolute;
  height: 16px; width: 16px;
  left: 3px; bottom: 3px;
  background: var(--text-muted);
  border-radius: 50%;
  transition: all 0.3s ease;
}

.switch input:checked + .slider {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
}

.switch input:checked + .slider::before {
  transform: translateX(20px);
  background: white;
}

/* ===== 添加规则 ===== */
.btn-add {
  width: 100%;
  padding: 11px;
  border: 2px dashed var(--border-color);
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--text-muted);
  font-size: 0.9rem;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-add:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

/* ===== 保存栏 ===== */
.save-bar {
  display: flex;
  justify-content: flex-end;
}

.btn-save {
  padding: 10px 28px;
  border: none;
  border-radius: var(--radius-md);
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  font-size: 0.92rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
  box-shadow: 0 2px 10px rgba(99, 102, 241, 0.25);
}

.btn-save:hover {
  opacity: 0.9;
  transform: translateY(-1px);
}

.btn-save:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  transform: none;
}

/* ===== Toast ===== */
.toast {
  position: fixed;
  top: 20px;
  right: 24px;
  z-index: 9999;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 18px;
  border-radius: var(--radius-md, 8px);
  font-size: 0.88rem;
  font-weight: 500;
  box-shadow: 0 4px 16px rgba(0,0,0,0.15);
  pointer-events: none;
  max-width: 360px;
}

.toast-success {
  color: #065f46;
  background: #d1fae5;
  border: 1px solid #6ee7b7;
}

.toast-error {
  color: #991b1b;
  background: #fee2e2;
  border: 1px solid #fca5a5;
}

.toast-icon {
  flex-shrink: 0;
}

.toast-enter-active {
  transition: all 0.3s ease-out;
}
.toast-leave-active {
  transition: all 0.25s ease-in;
}
.toast-enter-from {
  opacity: 0;
  transform: translateX(40px);
}
.toast-leave-to {
  opacity: 0;
  transform: translateX(40px);
}
</style>
