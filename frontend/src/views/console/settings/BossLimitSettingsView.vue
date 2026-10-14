<script setup>
import { ref, computed, onMounted, watch, onUnmounted, nextTick } from 'vue'
import { get, post } from '../../../utils/api.js'
import Loading from '../../../components/Loading.vue'

const loading = ref(true)
const error = ref('')
const success = ref('')
const activeTab = ref('summon')
const summonRules = ref([])
const summonSaving = ref(false)
let saveTimer = null
let ready = false

const bossLimitMode = ref('disabled')
const bossLimitMinPlayers = ref(7)
const quitLimitEnabled = ref(false)
const lateCompEnabled = ref(false)
const ghostRespawnSeconds = ref(20)

// 进度锁 · 按时间锁（叠加开关）
const timeLockEnabled = ref(false)
const serverStartTime = ref('')
const serverStartTimeInput = ref('') // datetime-local 编辑值（yyyy-MM-ddTHH:mm）
const worldId = ref('')
const blockLockedBossSpawn = ref(true)
const timeSchedule = ref([])

// BossLimit 活跃追踪状态
const bossLimitStatus = ref(null)
const statusLoading = ref(false)
let statusTimer = null

const bossModeOptions = [
  { value: 'disabled', label: '不限制人数与击杀进度' },
  { value: 'playerlimit', label: '按最低人数限制' },
  { value: 'killrequired', label: '不允许召唤未击败的 Boss' }
]

const summonPolicyOptions = [
  { value: 'auto', label: '按原规则' },
  { value: 'allow', label: '允许召唤' },
  { value: 'deny', label: '禁止召唤' }
]

const bossIcons = {
  '史莱姆王': 'King_Slime.png', '克苏鲁之眼': 'Eye_of_Cthulhu.png',
  '世界吞噬者': 'Eater_of_Worlds.webp', '克苏鲁之脑': 'Brain_of_Cthulhu.png',
  '蜂后': 'QueenBee.png', '巨鹿': 'Deerclops.png', '骷髅王': 'Skeletron.png',
  '血肉墙': 'Wall_of_Flesh.png', '史莱姆皇后': 'Queen_Slime.png',
  '毁灭者': 'The_Destroyer.png', '机械骷髅王': 'Skeletron_Prime.png',
  '双子魔眼': 'The_Twins.png', '世纪之花': 'Plantera.png', '石巨人': 'Golem.png',
  '猪龙鱼公爵': 'Duke_Fishron.png', '光之女皇': 'Empress_of_Light.png',
  '拜月教教徒': 'Lunatic_Cultist.png', '月亮领主': 'Moon_Lord.png',
  '哀木': 'Mourning_Wood.png', '南瓜王': 'Pumpking.png',
  '常绿尖叫怪': 'Everscream.png', '圣诞坦克': 'Santa_NK1.png',
  '冰雪女王': 'Ice_Queen.png', '荷兰飞盗船': 'Flying_Dutchman.png',
  '火星飞碟': 'Martian_Saucer.png'
}
const summonSearch = ref('')
const policyLabel = policy => summonPolicyOptions.find(option => option.value === policy)?.label || '按原规则'
const policyCounts = computed(() => Object.fromEntries(summonPolicyOptions.map(option => [
  option.value, summonRules.value.filter(rule => rule.policy === option.value).length
])))
const summonGroups = computed(() => {
  const query = summonSearch.value.trim()
  const matches = summonRules.value.filter(rule => rule.name.includes(query))
  return [
    { name: '主线 Boss', description: '从史莱姆王到月亮领主', rules: matches.filter(rule => bossNameOptions.includes(rule.name)) },
    { name: '事件与特殊 Boss', description: '入侵、节日与特殊挑战', rules: matches.filter(rule => !bossNameOptions.includes(rule.name)) }
  ].filter(group => group.rules.length)
})

const setSummonPolicy = async (target, policy) => {
  if (summonSaving.value) return
  summonSaving.value = true
  error.value = ''
  success.value = ''
  try {
    const res = await post('/api/config/boss', { summonTarget: target, summonPolicy: policy })
    const data = await res.json()
    if (data.status !== '200') throw new Error(data.error || '保存失败')
    summonRules.value = summonRules.value.map(rule => target === 'all' || rule.name === target
      ? { ...rule, policy } : rule)
    success.value = data.message || '已保存'
    setTimeout(() => { success.value = '' }, 1500)
  } catch (err) {
    error.value = '保存失败: ' + err.message
  } finally {
    summonSaving.value = false
  }
}

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
        ghostRespawnSeconds: ghostRespawnSeconds.value,
        timeLockEnabled: timeLockEnabled.value,
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
watch(ghostRespawnSeconds, autoSave)
watch(timeLockEnabled, autoSave)
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
  ready = false
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/config/boss')
    const data = await res.json()
    if (data.status !== '200') throw new Error(data.error || '加载失败')
    if (Array.isArray(data.summonRules)) summonRules.value = data.summonRules
    if (data.bossLimitMode !== undefined) bossLimitMode.value = data.bossLimitMode
    if (data.bossLimitMinPlayers !== undefined) bossLimitMinPlayers.value = data.bossLimitMinPlayers
    if (data.quitLimitEnabled !== undefined) quitLimitEnabled.value = data.quitLimitEnabled
    if (data.lateCompEnabled !== undefined) lateCompEnabled.value = data.lateCompEnabled
    if (data.ghostRespawnSeconds !== undefined) ghostRespawnSeconds.value = data.ghostRespawnSeconds

    if (data.timeLockEnabled !== undefined) timeLockEnabled.value = data.timeLockEnabled
    if (data.serverStartTime !== undefined) {
      serverStartTime.value = data.serverStartTime
      // 服务端 "yyyy-MM-dd HH:mm:ss" → datetime-local "yyyy-MM-ddTHH:mm"
      serverStartTimeInput.value = (data.serverStartTime || '').replace(' ', 'T').slice(0, 16)
    }
    if (data.worldId !== undefined) worldId.value = data.worldId
    if (data.blockLockedBossSpawn !== undefined) blockLockedBossSpawn.value = data.blockLockedBossSpawn
    if (Array.isArray(data.timeSchedule)) timeSchedule.value = data.timeSchedule
    // 等配置赋值引发的 watch 执行完毕，避免加载页面也触发保存及审计。
    await nextTick()
    ready = true
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  loading.value = false

  // 加载 bosslimit 活跃追踪状态
  fetchBossLimitStatus()
}

// ═══ 时间锁操作 ═══

const setServerStartTime = async () => {
  if (!serverStartTimeInput.value) {
    error.value = '请先选择开服时间'
    return
  }
  // datetime-local "yyyy-MM-ddTHH:mm" → 服务端 "yyyy-MM-dd HH:mm:ss"
  const formatted = serverStartTimeInput.value.replace('T', ' ') + ':00'
  try {
    const res = await post('/api/config/boss', { serverStartTime: formatted })
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
  clearTimeout(saveTimer)
  if (statusTimer) clearInterval(statusTimer)
})
</script>

<template>
  <div class="settings-page">
    <Loading v-if="loading" text="加载中..." />

    <div v-else class="settings-content">
      <div class="page-heading">
        <div>
          <h2>Boss 限制</h2>
          <p>管理召唤许可、解锁进度与战斗规则</p>
        </div>
        <span class="autosave-note"><span></span>修改后自动保存</span>
      </div>
      <div class="settings-tabs" role="tablist" aria-label="Boss 限制设置">
        <button id="boss-summon-tab" type="button" role="tab" :aria-selected="activeTab === 'summon'"
          aria-controls="boss-summon-panel" :class="{ active: activeTab === 'summon' }"
          @click="activeTab = 'summon'">召唤限制</button>
        <button id="boss-prevention-tab" type="button" role="tab" :aria-selected="activeTab === 'prevention'"
          aria-controls="boss-prevention-panel" :class="{ active: activeTab === 'prevention' }"
          @click="activeTab = 'prevention'">防逃课</button>
      </div>
      <div class="settings-grid">
        <div v-show="activeTab === 'summon'" id="boss-summon-panel" class="settings-grid"
          role="tabpanel" aria-labelledby="boss-summon-tab">
        <div class="section-card summon-section">
          <div class="summon-heading">
            <div>
              <h3>手动召唤许可 <span class="total-count">{{ summonRules.length }} 位 Boss</span></h3>
              <p class="section-desc">为每一场挑战，设定合适的开启时机。</p>
            </div>
            <div v-if="summonRules.length" class="policy-overview" aria-label="召唤策略统计">
              <span v-for="option in summonPolicyOptions" :key="option.value" :class="option.value">
                <i></i>{{ option.label }} <b>{{ policyCounts[option.value] }}</b>
              </span>
            </div>
          </div>
          <div v-if="summonRules.length" class="summon-toolbar">
            <label class="boss-search">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" aria-hidden="true"><circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 4.5 4.5"/></svg>
              <input v-model="summonSearch" type="search" placeholder="搜索 Boss 名称" aria-label="搜索 Boss 名称" />
            </label>
            <div class="summon-actions" aria-label="全部 Boss 操作">
              <button type="button" class="bulk-button allow" :disabled="summonSaving" @click="setSummonPolicy('all', 'allow')">允许全部</button>
              <button type="button" class="bulk-button deny" :disabled="summonSaving" @click="setSummonPolicy('all', 'deny')">禁止全部</button>
              <button type="button" class="bulk-button auto" :disabled="summonSaving" @click="setSummonPolicy('all', 'auto')">全部按原规则</button>
            </div>
          </div>
          <div class="summon-guide">允许召唤会跳过人数、击杀和时间限制；禁止召唤也会阻止自然生成。「按原规则」沿用下方设置。</div>
          <section v-for="group in summonGroups" :key="group.name" class="boss-group">
            <div class="boss-group-heading"><h4>{{ group.name }} <span>{{ group.rules.length }}</span></h4><span>{{ group.description }}</span></div>
            <div class="summon-list">
              <article v-for="rule in group.rules" :key="rule.name" class="boss-card" :class="'policy-' + rule.policy" :aria-label="rule.name">
                <div class="boss-card-heading">
                  <div class="boss-portrait" aria-hidden="true">
                    <img v-if="bossIcons[rule.name]" :src="'/assets/img/Boss/' + bossIcons[rule.name]" alt="" />
                    <svg v-else viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="1.8"><path d="m24 5 15 9v18l-15 9-15-9V14z"/><path d="m17 19 7-6 7 6-2 12H19zM19 23h2m6 0h2m-8 6h6"/></svg>
                  </div>
                  <div class="boss-card-info"><h5>{{ rule.name }}</h5><span class="policy-status"><i></i>{{ policyLabel(rule.policy) }}</span></div>
                </div>
                <div class="policy-control" role="group" :aria-label="rule.name + '召唤策略'">
                  <button v-for="opt in summonPolicyOptions" :key="opt.value" type="button"
                    :class="[opt.value, { selected: rule.policy === opt.value }]" :aria-pressed="rule.policy === opt.value"
                    :aria-label="rule.name + '：' + opt.label" :disabled="summonSaving"
                    @click="setSummonPolicy(rule.name, opt.value)">{{ opt.value === 'auto' ? '按原规则' : opt.value === 'allow' ? '允许' : '禁止' }}</button>
                </div>
              </article>
            </div>
          </section>
          <p v-if="summonRules.length && !summonGroups.length" class="search-empty">没有找到对应的 Boss，试试其他名称。</p>
          <p v-if="!summonRules.length" class="section-desc">当前服务器未提供手动召唤设置，请先更新 TSWeb 插件。</p>
        </div>
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
          <p class="section-desc">按时间锁是叠加开关：开启后，解锁计划中配置了时间的 Boss 档按时间解锁（不看击杀）；未配置的档仍按击杀进度判定</p>

          <div class="toggle-row">
            <span class="toggle-label">按时间锁</span>
            <span class="toggle-hint">开启后叠加时间解锁；关闭则恢复纯击杀进度判定</span>
            <label class="switch">
              <input type="checkbox" v-model="timeLockEnabled" />
              <span class="slider"></span>
            </label>
          </div>

          <!-- 开服时间 -->
          <div class="toggle-row">
            <span class="toggle-label">开服时间</span>
            <span class="toggle-hint">地图更换时自动重置为当前时间，也可手动指定</span>
          </div>
          <div class="inline-control">
            <input
              v-model="serverStartTimeInput"
              type="datetime-local"
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

          <!-- 解锁计划（始终可见；未开启按时间锁时不生效） -->
          <div class="schedule-block">
            <div class="schedule-header">
              <span class="toggle-label">解锁计划</span>
              <span class="toggle-hint">{{ timeLockEnabled ? '开服后第 N 天 HH:mm 解锁对应档' : '已关闭按时间锁，需开启后才生效' }}</span>
            </div>

            <div v-if="timeSchedule.length === 0" class="schedule-empty">
              暂无解锁计划，添加一个进度档：
            </div>

            <div
              v-for="(s, idx) in timeSchedule"
              :key="idx"
              class="schedule-row"
              :class="{ dimmed: !timeLockEnabled }"
            >
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
        </div>

        </div>
        <div v-show="activeTab === 'prevention'" id="boss-prevention-panel" class="settings-grid"
          role="tabpanel" aria-labelledby="boss-prevention-tab">
        <!-- BOSS 死亡幽灵模式 + 晚入补偿 -->
        <div class="section-card">
          <h3>BOSS 死亡幽灵模式 & 晚入补偿</h3>
          <p class="section-desc">BOSS 存活时死亡的玩家变幽灵，倒计时结束才复活；倒计时期间退出重进不重置剩余时间</p>

          <div class="toggle-row">
            <span class="toggle-label">死亡幽灵模式</span>
            <span class="toggle-hint">仅对打过该 BOSS 的玩家生效；关闭会立即释放所有幽灵态玩家</span>
            <label class="switch">
              <input type="checkbox" v-model="quitLimitEnabled" />
              <span class="slider"></span>
            </label>
          </div>

          <div class="toggle-row" :class="{ dimmed: !quitLimitEnabled }">
            <span class="toggle-label">幽灵复活倒计时</span>
            <span class="toggle-hint">1~3600 秒</span>
            <div class="number-control">
              <button class="num-btn" @click="ghostRespawnSeconds = Math.max(1, ghostRespawnSeconds - 5)">−</button>
              <span class="num-value">{{ ghostRespawnSeconds }}s</span>
              <button class="num-btn" @click="ghostRespawnSeconds = Math.min(3600, ghostRespawnSeconds + 5)">+</button>
            </div>
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
            <div class="tracking-stat">
              <span class="stat-number">{{ (bossLimitStatus.ghosts || []).length }}</span>
              <span class="stat-label">幽灵态玩家</span>
            </div>
          </div>

          <div v-if="bossLimitStatus.ghosts && bossLimitStatus.ghosts.length > 0" class="tracking-detail">
            <div v-for="(g, idx) in bossLimitStatus.ghosts" :key="idx" class="boss-track-row">
              <span class="boss-track-name">{{ g.player }}</span>
              <span class="boss-track-dmg">剩余 {{ g.remainSeconds }}s / 共 {{ g.totalSeconds }}s</span>
              <span class="boss-track-spawn">{{ g.bossName }}</span>
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
  max-width: 1180px;
}

.page-heading { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-bottom: 24px; }
.page-heading h2 { margin: 0 0 6px; font-size: 1.5rem; color: var(--text-primary); letter-spacing: -.03em; }
.page-heading p { margin: 0; color: var(--text-secondary); font-size: .85rem; }
.autosave-note { display: flex; align-items: center; gap: 7px; color: var(--text-muted); font-size: .75rem; white-space: nowrap; }
.autosave-note > span { width: 5px; height: 5px; border-radius: 50%; background: var(--accent-secondary); }

.settings-grid {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.settings-tabs {
  display: flex;
  gap: 4px;
  width: fit-content;
  padding: 4px;
  margin-bottom: 20px;
  border: 1px solid var(--border-color);
  border-radius: 12px;
  background: var(--bg-secondary);
}

.settings-tabs button {
  padding: 10px 26px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--text-muted);
  font: inherit;
  cursor: pointer;
}

.settings-tabs button.active {
  color: var(--text-primary);
  background: var(--bg-tertiary);
  box-shadow: 0 2px 6px #00000012;
}

.summon-section { background: linear-gradient(135deg, color-mix(in srgb, var(--accent-primary) 5%, transparent), transparent 40%), var(--bg-card); }
.summon-heading { display: flex; justify-content: space-between; align-items: flex-start; gap: 20px; flex-wrap: wrap; }
.total-count { display: inline-block; margin-left: 8px; padding: 3px 8px; border: 1px solid var(--border-color); border-radius: 6px; color: var(--text-secondary); font-size: .7rem; font-weight: 400; vertical-align: middle; }
.policy-overview { display: flex; flex-wrap: wrap; gap: 16px; font-size: .75rem; color: var(--text-secondary); padding-top: 5px; }
.policy-overview > span { display: flex; align-items: center; gap: 6px; }
.policy-overview b { color: var(--text-primary); font-variant-numeric: tabular-nums; }
.policy-overview i, .policy-status i { width: 5px; height: 5px; border-radius: 50%; background: currentColor; }
.policy-overview .auto i { color: var(--text-muted); }
.policy-overview .allow i { color: var(--accent-secondary); }
.policy-overview .deny i { color: var(--accent-error); }
.summon-toolbar { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 12px; padding: 16px 0; border-top: 1px solid var(--border-light); }
.boss-search { display: flex; align-items: center; gap: 8px; padding: 0 12px; border: 1px solid var(--border-color); border-radius: 8px; background: var(--bg-secondary); width: 245px; max-width: 100%; }
.boss-search svg { width: 17px; height: 17px; flex-shrink: 0; color: var(--text-muted); }
.boss-search input { width: 100%; min-width: 0; padding: 10px 0; color: var(--text-primary); border: 0; outline: 0; background: transparent; font: inherit; font-size: .8rem; }
.boss-search:focus-within { border-color: var(--accent-primary); }
.summon-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.bulk-button { padding: 9px 13px; border: 1px solid var(--border-color); border-radius: 8px; font: inherit; font-size: .78rem; cursor: pointer; background: var(--bg-tertiary); color: var(--text-secondary); }
.bulk-button.allow { color: var(--accent-secondary); border-color: color-mix(in srgb, var(--accent-secondary) 25%, transparent); background: color-mix(in srgb, var(--accent-secondary) 8%, transparent); }
.bulk-button.deny { color: var(--accent-error); border-color: color-mix(in srgb, var(--accent-error) 25%, transparent); background: color-mix(in srgb, var(--accent-error) 8%, transparent); }
.bulk-button:hover { filter: brightness(1.15); }
.summon-guide { padding: 11px 14px; border-left: 2px solid color-mix(in srgb, var(--accent-primary) 60%, transparent); border-radius: 0 6px 6px 0; background: color-mix(in srgb, var(--accent-primary) 6%, transparent); color: var(--text-secondary); font-size: .75rem; line-height: 1.8; }
.boss-group { margin-top: 24px; }
.boss-group-heading { display: flex; align-items: baseline; gap: 14px; margin-bottom: 12px; }
.boss-group-heading h4 { margin: 0; color: var(--text-primary); font-size: .85rem; font-weight: 600; }
.boss-group-heading h4 span { margin-left: 5px; color: var(--text-muted); font-size: .7rem; font-weight: 400; }
.boss-group-heading > span { color: var(--text-muted); font-size: .7rem; }
.summon-list { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
.boss-card { --policy-color: var(--text-secondary); min-width: 0; padding: 15px; border: 1px solid var(--border-color); border-radius: 12px; background: var(--bg-secondary); transition: border-color .18s, background .18s; }
.boss-card.policy-allow { --policy-color: var(--accent-secondary); border-color: color-mix(in srgb, var(--accent-secondary) 27%, var(--border-light)); }
.boss-card.policy-deny { --policy-color: var(--accent-error); border-color: color-mix(in srgb, var(--accent-error) 27%, var(--border-light)); }
.boss-card:hover { background: var(--bg-tertiary); border-color: color-mix(in srgb, var(--policy-color) 45%, var(--border-color)); }
.boss-card-heading { display: flex; align-items: center; gap: 12px; margin-bottom: 14px; }
.boss-portrait { display: grid; place-items: center; width: 60px; height: 60px; flex-shrink: 0; border-radius: 10px; border: 1px solid var(--border-light); background: radial-gradient(ellipse at center, color-mix(in srgb, var(--accent-primary) 13%, transparent), transparent 75%), var(--bg-primary); }
.boss-portrait img { max-width: 48px; max-height: 48px; width: auto; height: auto; object-fit: contain; image-rendering: pixelated; filter: drop-shadow(0 3px 4px #0004); }
.boss-portrait svg { width: 38px; height: 38px; color: var(--text-muted); }
.boss-card-info { min-width: 0; }
.boss-card-info h5 { margin: 0 0 7px; color: var(--text-primary); font-size: .9rem; font-weight: 600; }
.policy-status { display: flex; align-items: center; gap: 6px; font-size: .7rem; color: var(--policy-color); }
.policy-control { display: grid; grid-template-columns: 1.3fr 1fr 1fr; gap: 3px; padding: 3px; background: var(--bg-primary); border: 1px solid var(--border-light); border-radius: 7px; }
.policy-control button { padding: 6px 2px; border: 1px solid transparent; border-radius: 5px; font: inherit; font-size: .72rem; color: var(--text-muted); background: transparent; cursor: pointer; }
.policy-control button:hover { color: var(--text-primary); background: var(--bg-hover); }
.policy-control .selected { color: var(--text-primary); background: var(--bg-tertiary); border-color: var(--border-color); }
.policy-control .allow.selected { color: var(--accent-secondary); background: color-mix(in srgb, var(--accent-secondary) 12%, transparent); border-color: color-mix(in srgb, var(--accent-secondary) 20%, transparent); }
.policy-control .deny.selected { color: var(--accent-error); background: color-mix(in srgb, var(--accent-error) 12%, transparent); border-color: color-mix(in srgb, var(--accent-error) 20%, transparent); }
.search-empty { padding: 30px 0 12px; color: var(--text-muted); text-align: center; font-size: .85rem; }
button:focus-visible { outline: 2px solid var(--accent-primary); outline-offset: 3px; }

@media (max-width: 1000px) { .summon-list { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 600px) {
  .settings-page { padding: 12px; }
  .section-card { padding: 18px !important; }
  .page-heading { align-items: flex-start; }
  .autosave-note { margin-top: 7px; font-size: .65rem; }
  .page-heading p { max-width: 210px; line-height: 1.6; font-size: .78rem; }
  .summon-heading { gap: 0; }
  .policy-overview { margin-bottom: 18px; gap: 12px; }
  .boss-search { width: 100%; }
  .summon-actions { width: 100%; }
  .bulk-button { flex: 1; padding-inline: 7px; white-space: nowrap; }
  .summon-list { grid-template-columns: 1fr; gap: 10px; }
  .boss-card { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
  .boss-card-heading { flex: 1; margin-bottom: 0; }
  .boss-portrait { width: 52px; height: 52px; }
  .boss-portrait img { max-width: 43px; max-height: 43px; }
  .policy-control { width: 100%; }
  .boss-group-heading { gap: 8px; }
  .toggle-row { flex-wrap: wrap; }
  .toggle-hint { min-width: 100px; }
  .inline-control { flex-wrap: wrap; }
  .text-input { min-width: 0; }
  .schedule-row { flex-wrap: wrap; }
  .schedule-status { flex-basis: 50%; text-align: left; }
  .tracking-stat { padding: 12px 6px; }
  .boss-track-row { flex-wrap: wrap; gap: 8px; }
}

.btn-secondary {
  padding: 8px 16px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  cursor: pointer;
}

button:disabled, select:disabled {
  opacity: 0.55;
  cursor: wait;
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

.toggle-row.dimmed {
  opacity: 0.45;
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

.schedule-row.dimmed {
  opacity: 0.55;
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
