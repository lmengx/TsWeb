<script setup>
import { ref, onMounted, watch, nextTick, onUnmounted } from 'vue'
import { get, post } from '../../utils/api.js'

const loading = ref(true)
const saving = ref(false)
const error = ref('')
const success = ref('')
const groupList = ref([])
const groupsLoading = ref(true)

// 配置数据
const config = ref({
  qqBind: { enabled: false, targetGroup: 'vip', mode: 'auto' },
  playtimeThresholds: [],
  ignoreGroups: []
})

const modeOptions = [
  { value: 'auto', label: '自动（检查父组链，不降级）' },
  { value: 'force', label: '强制（忽略当前组直接设为目标组）' }
]

let saveTimer = null
let ready = false

// ===== 时间转换辅助 =====
const toDays = (minutes) => Math.floor(minutes / 1440)
const toHours = (minutes) => Math.floor((minutes % 1440) / 60)
const toMins = (minutes) => minutes % 60

const fromDHM = (d, h, m) => {
  const dd = parseInt(d) || 0
  const hh = parseInt(h) || 0
  const mm = parseInt(m) || 0
  return Math.max(1, dd * 1440 + hh * 60 + mm)
}

const save = async () => {
  if (!ready) return
  clearTimeout(saveTimer)
  saveTimer = setTimeout(async () => {
    saving.value = true
    error.value = ''
    success.value = ''
    try {
      const res = await post('/api/config/promotion', config.value)
      const data = await res.json()
      if (data.status === '200' || data.response === '配置已保存') {
        success.value = '已保存'
        setTimeout(() => { success.value = '' }, 1500)
      } else {
        error.value = data.error || '保存失败'
      }
    } catch (err) {
      error.value = '保存失败: ' + err.message
    }
    saving.value = false
  }, 500)
}

watch(config, () => { save() }, { deep: true })

const fetchData = async () => {
  loading.value = true
  groupsLoading.value = true

  try {
    // 加载组列表（用已有 /api/tshock/groups 接口）
    const groupRes = await get('/api/tshock/groups')
    const groupData = await groupRes.json()
    if (groupData.groups) {
      groupList.value = groupData.groups.map(g => g.GroupName).sort()
    }
    groupsLoading.value = false
  } catch (err) {
    console.warn('加载组列表失败:', err)
    groupsLoading.value = false
  }

  try {
    const res = await get('/api/config/promotion')
    const data = await res.json()

    if (data.qqBind) {
      config.value.qqBind = {
        enabled: data.qqBind.enabled ?? true,
        targetGroup: data.qqBind.targetGroup || 'vip',
        mode: data.qqBind.mode || 'force'
      }
    }
    if (data.playtimeThresholds) {
      config.value.playtimeThresholds = data.playtimeThresholds.map(t => ({
        minutes: t.minutes || 3000,
        targetGroup: t.targetGroup || 'trustedplayer',
        mode: t.mode || 'auto'
      }))
    }
    if (data.ignoreGroups) {
      config.value.ignoreGroups = data.ignoreGroups
    }

    await nextTick()
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }

  ready = true
  loading.value = false
}

// ===== 游玩时长阈值操作 =====
const addThreshold = () => {
  config.value.playtimeThresholds.push({
    minutes: 60,
    targetGroup: 'default',
    mode: 'auto'
  })
}

const removeThreshold = (index) => {
  config.value.playtimeThresholds.splice(index, 1)
}

// ===== 忽略组操作 =====
const newIgnoreGroup = ref('')

const addIgnoreGroup = () => {
  const name = newIgnoreGroup.value.trim()
  if (!name) return
  if (config.value.ignoreGroups.some(g => g.toLowerCase() === name.toLowerCase())) {
    newIgnoreGroup.value = ''
    return
  }
  config.value.ignoreGroups.push(name)
  newIgnoreGroup.value = ''
}

const removeIgnoreGroup = (index) => {
  config.value.ignoreGroups.splice(index, 1)
}

onMounted(() => {
  fetchData()
})
</script>

<template>
  <div class="promotion-page">
    <div class="page-header">
      <h2>权限提升配置</h2>
      <p class="page-desc">配置 QQ 绑定和游玩时长的自动权限提升规则</p>
    </div>

    <div v-if="loading" class="loading-state">
      <p>加载中...</p>
    </div>

    <div v-else class="promotion-content">
      <!-- ===== QQ 绑定提升 ===== -->
      <div class="section-card">
        <div class="section-header">
          <h3>QQ 绑定提升</h3>
          <label class="switch">
            <input type="checkbox" v-model="config.qqBind.enabled" />
            <span class="slider"></span>
          </label>
        </div>
        <p class="section-desc">玩家绑定 QQ 后自动提升权限组</p>

        <div class="form-row">
          <div class="form-group">
            <label class="form-label">目标权限组</label>
            <select v-model="config.qqBind.targetGroup" class="form-select" :disabled="!config.qqBind.enabled">
              <option value="" disabled>选择权限组...</option>
              <option v-for="g in groupList" :key="g" :value="g">{{ g }}</option>
            </select>
          </div>
          <div class="form-group">
            <label class="form-label">提升模式</label>
            <select v-model="config.qqBind.mode" class="form-select" :disabled="!config.qqBind.enabled">
              <option v-for="opt in modeOptions" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
            </select>
          </div>
        </div>
      </div>

      <!-- ===== 游玩时长提升 ===== -->
      <div class="section-card">
        <div class="section-header">
          <h3>游玩时长提升</h3>
          <span class="badge">{{ config.playtimeThresholds.length }} 条规则</span>
        </div>
        <p class="section-desc">达到指定游玩时长后自动提升权限组（取最高匹配规则）</p>

        <div class="threshold-list">
          <div
            v-for="(threshold, index) in config.playtimeThresholds"
            :key="index"
            class="threshold-item"
          >
            <div class="threshold-header">
              <span class="threshold-index">#{{ index + 1 }}</span>
              <button
                class="btn-remove"
                @click="removeThreshold(index)"
                title="删除此规则"
              >✕</button>
            </div>

            <div class="threshold-body">
              <!-- 时长输入：天 / 小时 / 分钟 -->
              <div class="form-group time-group">
                <label class="form-label">游玩时长</label>
                <div class="dhm-inputs">
                  <div class="dhm-item">
                    <input
                      type="number"
                      min="0"
                      :value="toDays(threshold.minutes)"
                      @input="threshold.minutes = fromDHM($event.target.value, toHours(threshold.minutes), toMins(threshold.minutes))"
                      class="form-input dhm-input"
                      placeholder="0"
                    />
                    <span class="dhm-label">天</span>
                  </div>
                  <div class="dhm-item">
                    <input
                      type="number"
                      min="0"
                      max="23"
                      :value="toHours(threshold.minutes)"
                      @input="threshold.minutes = fromDHM(toDays(threshold.minutes), $event.target.value, toMins(threshold.minutes))"
                      class="form-input dhm-input"
                      placeholder="0"
                    />
                    <span class="dhm-label">时</span>
                  </div>
                  <div class="dhm-item">
                    <input
                      type="number"
                      min="0"
                      max="59"
                      :value="toMins(threshold.minutes)"
                      @input="threshold.minutes = fromDHM(toDays(threshold.minutes), toHours(threshold.minutes), $event.target.value)"
                      class="form-input dhm-input"
                      placeholder="0"
                    />
                    <span class="dhm-label">分</span>
                  </div>
                </div>
                <span class="time-total">= {{ threshold.minutes }} 分钟（{{ (threshold.minutes / 60).toFixed(1) }} 小时）</span>
              </div>

              <!-- 目标组 -->
              <div class="form-group">
                <label class="form-label">目标权限组</label>
                <select v-model="threshold.targetGroup" class="form-select">
                  <option value="" disabled>选择权限组...</option>
                  <option v-for="g in groupList" :key="g" :value="g">{{ g }}</option>
                </select>
              </div>

              <!-- 模式 -->
              <div class="form-group">
                <label class="form-label">提升模式</label>
                <select v-model="threshold.mode" class="form-select">
                  <option v-for="opt in modeOptions" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
                </select>
              </div>
            </div>
          </div>
        </div>

        <button class="btn-add" @click="addThreshold">
          ＋ 添加阈值
        </button>
      </div>

      <!-- ===== 忽略组 ===== -->
      <div class="section-card">
        <div class="section-header">
          <h3>忽略组</h3>
        </div>
        <p class="section-desc">属于以下组的玩家将<strong>始终不会被自动提升</strong>。</p>
        <div class="ignore-list">
          <div
            v-for="(group, index) in config.ignoreGroups"
            :key="index"
            class="ignore-tag-wrapper"
          >
            <span class="ignore-tag">{{ group }}</span>
            <button class="ignore-remove" @click="removeIgnoreGroup(index)" title="移除此组">✕</button>
          </div>
          <div class="ignore-add-row">
            <input
              v-model="newIgnoreGroup"
              class="form-input ignore-input"
              placeholder="输入组名..."
              @keydown.enter="addIgnoreGroup"
            />
            <button class="btn-add-ignore" @click="addIgnoreGroup" :disabled="!newIgnoreGroup.trim()">＋</button>
          </div>
        </div>
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
.promotion-page {
  padding: 20px;
  width: 100%;
}

.page-header {
  margin-bottom: 20px;
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

.promotion-content {
  max-width: 800px;
  display: flex;
  flex-direction: column;
  gap: 20px;
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
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 4px;
}

.section-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.1rem;
  font-weight: 600;
}

.section-desc {
  margin: 4px 0 20px 0;
  color: var(--text-muted);
  font-size: 0.85rem;
}

.section-desc strong {
  color: var(--text-primary);
}

.badge {
  font-size: 0.78rem;
  padding: 2px 10px;
  border-radius: 12px;
  background: var(--accent-primary);
  color: white;
  font-weight: 500;
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
  padding: 10px 12px;
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

.form-select:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.form-select option {
  background: var(--bg-card);
  color: var(--text-primary);
}

/* ===== Switch ===== */
.switch {
  position: relative;
  display: inline-block;
  width: 50px;
  height: 28px;
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
  border-radius: 28px;
  transition: all 0.3s ease;
}

.slider::before {
  content: '';
  position: absolute;
  height: 18px; width: 18px;
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
  transform: translateX(22px);
  background: white;
}

/* ===== 阈值列表 ===== */
.threshold-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 16px;
}

.threshold-item {
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
  padding: 16px;
  border: 1px solid var(--border-color);
}

.threshold-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.threshold-index {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--accent-primary);
}

.btn-remove {
  width: 24px;
  height: 24px;
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
}

.btn-remove:hover {
  background: #ef4444;
  color: white;
}

.threshold-body {
  display: flex;
  flex-direction: column;
  gap: 0;
}

/* ===== 天/时/分 输入 ===== */
.time-group {
  margin-bottom: 8px;
}

.dhm-inputs {
  display: flex;
  gap: 8px;
  margin-bottom: 4px;
}

.dhm-item {
  display: flex;
  align-items: center;
  gap: 4px;
  flex: 1;
}

.dhm-input {
  text-align: center;
  min-width: 0;
}

.dhm-label {
  color: var(--text-muted);
  font-size: 0.8rem;
  width: 20px;
}

.time-total {
  display: block;
  font-size: 0.78rem;
  color: var(--text-muted);
  margin-top: 2px;
}

/* ===== 添加按钮 ===== */
.btn-add {
  width: 100%;
  padding: 10px;
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

/* ===== 忽略组 ===== */
.ignore-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.ignore-tag-wrapper {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 4px 4px 12px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
}

.ignore-tag {
  color: var(--text-primary);
  font-size: 0.85rem;
  font-weight: 500;
}

.ignore-remove {
  width: 20px;
  height: 20px;
  border: none;
  border-radius: 50%;
  background: rgba(239, 68, 68, 0.12);
  color: #ef4444;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.6rem;
  transition: all 0.2s;
  flex-shrink: 0;
}

.ignore-remove:hover {
  background: #ef4444;
  color: white;
}

.ignore-add-row {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.ignore-input {
  width: 130px;
  padding: 6px 10px !important;
  font-size: 0.82rem !important;
}

.btn-add-ignore {
  width: 28px;
  height: 28px;
  border: none;
  border-radius: 50%;
  background: var(--accent-primary);
  color: white;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.9rem;
  font-weight: 600;
  transition: all 0.2s;
  flex-shrink: 0;
}

.btn-add-ignore:hover {
  opacity: 0.85;
}

.btn-add-ignore:disabled {
  opacity: 0.4;
  cursor: not-allowed;
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
