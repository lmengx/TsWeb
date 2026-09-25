<script setup>
import { ref, onMounted, watch, onUnmounted } from 'vue'
import { get, post } from '../../../utils/api.js'
import Loading from '../../../components/Loading.vue'

const loading = ref(true)
const error = ref('')
const success = ref('')
let saveTimer = null
let ready = false

const bossLimitMode = ref('disabled')
const bossLimitMinPlayers = ref(7)
const quitLimitEnabled = ref(false)
const lateCompEnabled = ref(false)

// 进度锁 · 按时间锁
const progressLockMode = ref('killbased')
const serverStartTime = ref('')
const worldId = ref('')
const blockLockedBossSpawn = ref(true)
const timeSchedule = ref([])

// BossLimit 活跃追踪状态
const bossLimitStatus = ref(null)
const statusLoading = ref(false)
let statusTimer = null

const bossModeOptions = [
  { value: 'disabled', label: '不做任何限制' },
  { value: 'playerlimit', label: '按最低人数限制' },
  { value: 'killrequired', label: '不允许召唤未击败的 Boss' }
]

const progressLockOptions = [
  { value: 'killbased', label: '按击杀进度' },
  { value: 'timelock', label: '按时间锁' }
]

// 进度档名（与反作弊进度档一致，供解锁计划下拉选择）
const bossNameOptions = [
  '史莱姆王', '克苏鲁之眼', '世界吞噬者', '克苏鲁之脑', '蜂后', '巨鹿', '骷髅王',
  '血肉墙', '史莱姆皇后', '毁灭者', '机械骷髅王', '双子魔眼', '世纪之花',
  '石巨人', '猪龙鱼公爵', '光之女皇', '拜月教教徒', '月亮领主'
]

// 新增解锁计划行（临时编辑用）
const newScheduleName = ref('血肉墙')
const newScheduleDay = ref(2)
const newScheduleTime = ref('12:00')

const autoSave = () => {
  if (!ready) return
  clearTimeout(saveTimer)
  saveTimer = setTimeout(async () => {
    error.value = ''
    success.value = ''
    try {
      const res = await post('/api/config/boss', {
        bossLimitMode: bossLimitMode.value,
        bossLimitEnabled: bossLimitMode.value !== 'disabled',
        bossLimitMinPlayers: bossLimitMinPlayers.value,
        quitLimitEnabled: quitLimitEnabled.value,
        lateCompEnabled: lateCompEnabled.value,
        progressLockMode: progressLockMode.value,
        blockLockedBossSpawn: blockLockedBossSpawn.value,
        schedule: JSON.stringify(timeSchedule.value.map(s => ({
          Name: s.name,
          Day: s.day,
          Time: s.time
        })))
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
  }, 500)
}

watch(bossLimitMode, autoSave)
watch(bossLimitMinPlayers, autoSave)
watch(quitLimitEnabled, autoSave)
watch(lateCompEnabled, autoSave)
watch(progressLockMode, autoSave)
watch(blockLockedBossSpawn, autoSave)
watch(timeSchedule, autoSave, { deep: true })

const fetchBossLimitStatus = async () => {
  statusLoading.value = true
  try {
    const res = await get('/api/config/bosslimit/status')
    const data = await res.json()
    if (data.status === '200') {
      bossLimitStatus.value = data
    }
  } catch {
    // 静默失败，不阻塞页面
  }
  statusLoading.value = false
}

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/boss')
    const data = await res.json()
    if (data.bossLimitMode !== undefined) bossLimitMode.value = data.bossLimitMode
    if (data.bossLimitMinPlayers !== undefined) bossLimitMinPlayers.value = data.bossLimitMinPlayers
    if (data.quitLimitEnabled !== undefined) quitLimitEnabled.value = data.quitLimitEnabled
    if (data.lateCompEnabled !== undefined) lateCompEnabled.value = data.lateCompEnabled

    if (data.progressLockMode !== undefined) progressLockMode.value = data.progressLockMode
    if (data.serverStartTime !== undefined) serverStartTime.value = data.serverStartTime
    if (data.worldId !== undefined) worldId.value = data.worldId
    if (data.blockLockedBossSpawn !== undefined) blockLockedBossSpawn.value = data.blockLockedBossSpawn
    if (Array.isArray(data.timeSchedule)) timeSchedule.value = data.timeSchedule
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  ready = true
  loading.value = false

  // 加载 bosslimit 活跃追踪状态
  fetchBossLimitStatus()
}

// ═══ 时间锁操作 ═══

const setServerStartTime = async () => {
  if (!serverStartTime.value) {
    error.value = '请先输入开服时间'
    return
  }
  try {
    const res = await post('/api/config/boss', { serverStartTime: serverStartTime.value })
    const data = await res.json()
    if (data.status === '200') {
      success.value = '开服时间已更新'
      setTimeout(() => { success.value = '' }, 1500)
      await fetchConfig()
    } else {
      error.value = data.error || '设置失败'
    }
  } catch (err) {
    error.value = '设置失败: ' + err.message
  }
}

const addSchedule = () => {
  if (!newScheduleName.value) {
    error.value = '请选择进度档'
    return
  }
  if (newScheduleDay.value < 1) {
    error.value = '第N天必须 ≥ 1'
    return
  }
  const idx = timeSchedule.value.findIndex(s => s.name === newScheduleName.value)
  if (idx >= 0) {
    // 重名覆盖
    timeSchedule.value[idx] = {
      name: newScheduleName.value,
      day: newScheduleDay.value,
      time: newScheduleTime.value
    }
  } else {
    timeSchedule.value.push({
      name: newScheduleName.value,
      day: newScheduleDay.value,
      time: newScheduleTime.value
    })
  }
  autoSave()
}

const removeSchedule = (idx) => {
  timeSchedule.value.splice(idx, 1)
  autoSave()
}

const formatUnlockAt = (s) => {
  if (!s.unlockAt) return '未计算'
  return s.unlocked ? `${s.unlockAt} 已解锁` : `${s.unlockAt} 解锁`
}

onMounted(() => {
  fetchConfig()
  // 每 10 秒刷新一次活跃追踪状态
  statusTimer = setInterval(fetchBossLimitStatus, 10000)
})

onUnmounted(() => {
  if (statusTimer) clearInterval(statusTimer)
})
</script>

<template>
  <div class="settings-page">
    <Loading v-if="loading" text="加载中..." />

    <div v-else class="settings-content">
      <div class="settings-grid">
        <!-- Boss 限制 -->
        <div class="section-card">
          <h3>Boss 召唤限制</h3>
          <p class="section-desc">控制 Boss 召唤的拦截策略</p>
          <div class="radio-group">
            <label
              v-for="opt in bossModeOptions"
              :key="opt.value"
              class="radio-item"
              :class="{ active: bossLimitMode === opt.value }"
            >
              <input
                type="radio"
                v-model="bossLimitMode"
                :value="opt.value"
                class="radio-input"
              />
              <span class="radio-label">{{ opt.label }}</span>
            </label>
          </div>
          <div v-if="bossLimitMode === 'playerlimit'" class="toggle-row">
            <span class="toggle-label">最低在线人数</span>
            <div class="number-control">
              <button class="num-btn" @click="bossLimitMinPlayers = Math.max(1, bossLimitMinPlayers - 1)">−</button>
              <span class="num-value">{{ bossLimitMinPlayers }}</span>
              <button class="num-btn" @click="bossLimitMinPlayers = Math.min(999, bossLimitMinPlayers + 1)">+</button>
            </div>
          </div>
        </div>

        <!-- 进度锁 · 按时间锁 -->
        <div class="section-card">
          <h3>进度锁 · 按时间锁</h3>
          <p class="section-desc">进度锁可配置为按时间解锁：开服后第 N 天指定时刻自动解锁对应 Boss 档（纯按时间，不看击杀）</p>

          <div class="radio-group">
            <label
              v-for="opt in progressLockOptions"
              :key="opt.value"
              class="radio-item"
              :class="{ active: progressLockMode === opt.value }"
            >
              <input
                type="radio"
                v-model="progressLockMode"
                :value="opt.value"
                class="radio-input"
              />
              <span class="radio-label">{{ opt.label }}</span>
            </label>
          </div>

          <template v-if="progressLockMode === 'timelock'">
            <!-- 开服时间 -->
            <div class="toggle-row">
              <span class="toggle-label">开服时间</span>
              <span class="toggle-hint">地图更换时自动重置为当前时间，也可手动指定</span>
            </div>
            <div class="inline-control">
              <input
                v-model="serverStartTime"
                type="text"
                placeholder="yyyy-MM-dd HH:mm:ss"
                class="text-input"
              />
              <button class="btn-primary" @click="setServerStartTime">手动指定</button>
            </div>

            <!-- 世界 ID -->
            <div class="toggle-row">
              <span class="toggle-label">地图 ID</span>
              <span class="toggle-value">{{ worldId || '未记录（启动后自动记录）' }}</span>
            </div>

            <!-- BOSS 生成/召唤拦截 -->
            <div class="toggle-row">
              <span class="toggle-label">BOSS 生成/召唤拦截</span>
              <span class="toggle-hint">拦截未解锁档 Boss 的召唤与自然生成</span>
              <label class="switch">
                <input type="checkbox" v-model="blockLockedBossSpawn" />
                <span class="slider"></span>
              </label>
            </div>

            <!-- 解锁计划 -->
            <div class="schedule-block">
              <div class="schedule-header">
                <span class="toggle-label">解锁计划</span>
                <span class="toggle-hint">开服后第 N 天 HH:mm 解锁对应档</span>
              </div>

              <div v-if="timeSchedule.length === 0" class="schedule-empty">
                暂无解锁计划，添加一个进度档：
              </div>

              <div v-for="(s, idx) in timeSchedule" :key="idx" class="schedule-row">
                <span class="schedule-name">{{ s.name }}</span>
                <span class="schedule-desc">开服后第 {{ s.day }} 天 {{ s.time }}</span>
                <span class="schedule-status" :class="{ done: s.unlocked }">
                  {{ s.unlocked ? '已解锁' : s.unlockAt ? s.unlockAt + ' 解锁' : '未计算' }}
                </span>
                <button class="btn-icon" @click="removeSchedule(idx)">✕</button>
              </div>

              <div class="schedule-add">
                <select v-model="newScheduleName" class="select-input">
                  <option v-for="n in bossNameOptions" :key="n" :value="n">{{ n }}</option>
                </select>
                <span class="schedule-add-label">开服后第</span>
                <input v-model.number="newScheduleDay" type="number" min="1" class="num-input" />
                <span class="schedule-add-label">天</span>
                <input v-model="newScheduleTime" type="time" class="time-input" />
                <button class="btn-primary" @click="addSchedule">添加</button>
              </div>
            </div>
          </template>
        </div>

        <!-- Boss 退出惩罚 + 晚入补偿 -->
        <div class="section-card">
          <h3>Boss 退出惩罚 & 晚入补偿</h3>
          <p class="section-desc">控制玩家在 Boss 战中退出或晚入的行为处理</p>

          <div class="toggle-row">
            <span class="toggle-label">退出惩罚</span>
            <span class="toggle-hint">战斗中退出的玩家上线后将被击杀</span>
            <label class="switch">
              <input type="checkbox" v-model="quitLimitEnabled" />
              <span class="slider"></span>
            </label>
          </div>

          <div class="toggle-row">
            <span class="toggle-label">晚入补偿</span>
            <span class="toggle-hint">新加入的玩家按比例增加 Boss 血量</span>
            <label class="switch">
              <input type="checkbox" v-model="lateCompEnabled" />
              <span class="slider"></span>
            </label>
          </div>
        </div>

        <!-- 活跃追踪状态 -->
        <div v-if="bossLimitStatus" class="section-card">
          <div class="tracking-header">
            <h3 class="tracking-title">当前活跃追踪</h3>
            <span v-if="statusLoading" class="tracking-refresh">刷新中...</span>
          </div>
          <div class="tracking-grid">
            <div class="tracking-stat">
              <span class="stat-number">{{ bossLimitStatus.trackedBosses ?? 0 }}</span>
              <span class="stat-label">追踪 BOSS</span>
            </div>
            <div class="tracking-stat">
              <span class="stat-number">{{ bossLimitStatus.trackedPlayers ?? 0 }}</span>
              <span class="stat-label">伤害者</span>
            </div>
          </div>
          <div v-if="bossLimitStatus.activeBosses && bossLimitStatus.activeBosses.length > 0" class="tracking-detail">
            <div v-for="(boss, idx) in bossLimitStatus.activeBosses" :key="idx" class="boss-track-row">
              <span class="boss-track-name">{{ boss.bossName }}</span>
              <span class="boss-track-dmg">{{ boss.damagerCount }} 人伤害</span>
              <span class="boss-track-spawn">出现时 {{ boss.onlineOnSpawn }}人在线</span>
            </div>
          </div>
          <div v-else class="tracking-idle">
            当前无活跃 Boss 战
          </div>
        </div>
      </div>

      <!-- Toast 通知 -->
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
  </div>
</template>

<style scoped>
.settings-page {
  padding: 20px;
  width: 100%;
}

.settings-content {
  max-width: 700px;
}

.settings-grid {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.section-card {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
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
  margin: 0 0 20px 0;
  color: var(--text-muted);
  font-size: 0.85rem;
}

.radio-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.radio-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  cursor: pointer;
  transition: all 0.2s ease;
}

.radio-item:hover {
  border-color: var(--accent-primary);
}

.radio-item.active {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.08);
}

.radio-input {
  accent-color: var(--accent-primary);
}

.radio-label {
  color: var(--text-primary);
  font-size: 0.95rem;
}

.toggle-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 0;
  border-bottom: 1px solid var(--border-light);
}

.toggle-row:last-child {
  border-bottom: none;
}

.toggle-label {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.95rem;
}

.toggle-hint {
  flex: 1;
  color: var(--text-muted);
  font-size: 0.8rem;
}

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

.number-control {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-left: auto;
}

.num-btn {
  width: 32px;
  height: 32px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 1rem;
  cursor: pointer;
  transition: all 0.2s ease;
}

.num-btn:hover {
  border-color: var(--accent-primary);
}

.num-value {
  min-width: 32px;
  text-align: center;
  color: var(--text-primary);
  font-weight: 600;
  font-size: 1rem;
}

.tracking-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.tracking-title {
  margin: 0 !important;
}

.tracking-refresh {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.tracking-grid {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
}

.tracking-stat {
  flex: 1;
  text-align: center;
  padding: 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-light);
}

.stat-number {
  display: block;
  font-size: 1.6rem;
  font-weight: 700;
  color: var(--accent-primary);
}

.stat-label {
  display: block;
  margin-top: 4px;
  font-size: 0.8rem;
  color: var(--text-muted);
}

.tracking-detail {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.boss-track-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-light);
  font-size: 0.85rem;
}

.boss-track-name {
  color: var(--text-primary);
  font-weight: 500;
}

.boss-track-dmg,
.boss-track-spawn {
  color: var(--text-muted);
}

.tracking-idle {
  text-align: center;
  padding: 20px;
  color: var(--text-muted);
  font-size: 0.9rem;
}

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

/* ═══ 进度锁 · 按时间锁 ═══ */
.toggle-value {
  flex: 1;
  color: var(--text-primary);
  font-size: 0.9rem;
  font-family: var(--font-mono);
}

.inline-control {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 0;
}

.text-input {
  flex: 1;
  padding: 8px 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 0.9rem;
  font-family: var(--font-mono);
  outline: none;
  transition: border-color 0.2s ease;
}

.text-input:focus {
  border-color: var(--accent-primary);
}

.btn-primary {
  padding: 8px 16px;
  background: var(--accent-primary);
  border: none;
  border-radius: var(--radius-sm);
  color: #fff;
  font-size: 0.85rem;
  cursor: pointer;
  transition: filter 0.2s ease;
  flex-shrink: 0;
}

.btn-primary:hover {
  filter: brightness(1.1);
}

.schedule-block {
  margin-top: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.schedule-header {
  display: flex;
  align-items: center;
  gap: 12px;
}

.schedule-empty {
  padding: 12px;
  color: var(--text-muted);
  font-size: 0.85rem;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  border: 1px dashed var(--border-color);
}

.schedule-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-light);
  font-size: 0.85rem;
}

.schedule-name {
  color: var(--text-primary);
  font-weight: 500;
  min-width: 90px;
}

.schedule-desc {
  color: var(--text-muted);
}

.schedule-status {
  flex: 1;
  text-align: right;
  color: var(--accent-error);
  font-size: 0.8rem;
}

.schedule-status.done {
  color: var(--accent-secondary);
}

.btn-icon {
  width: 26px;
  height: 26px;
  background: transparent;
  border: none;
  color: var(--text-muted);
  font-size: 0.85rem;
  cursor: pointer;
  border-radius: var(--radius-sm);
  transition: all 0.2s ease;
  flex-shrink: 0;
}

.btn-icon:hover {
  color: var(--accent-error);
  background: rgba(239, 68, 68, 0.1);
}

.schedule-add {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 0;
  flex-wrap: wrap;
}

.select-input {
  padding: 8px 10px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 0.85rem;
  outline: none;
  min-width: 110px;
}

.select-input:focus {
  border-color: var(--accent-primary);
}

.schedule-add-label {
  color: var(--text-muted);
  font-size: 0.85rem;
  white-space: nowrap;
}

.num-input {
  width: 64px;
  padding: 8px 10px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 0.85rem;
  outline: none;
}

.num-input:focus {
  border-color: var(--accent-primary);
}

.time-input {
  padding: 8px 10px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 0.85rem;
  outline: none;
}

.time-input:focus {
  border-color: var(--accent-primary);
}
</style>
