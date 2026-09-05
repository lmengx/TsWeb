<script setup>
import { ref, onMounted, computed, reactive } from 'vue'
import { get } from '../../utils/api.js'
import { isAdmin } from '../../utils/authHelper.js'
import { getWorldModifyStatus, applyWorldModify } from '../../utils/worldModifyApi.js'
import Loading from '../../components/Loading.vue'

// ═══════════════ 顶部 Tab（默认世界参数） ═══════════════
const activeTab = ref('params') // 'params' | 'progress'

// ═══════════════ 世界进度（原逻辑保留） ═══════════════
const progressData = ref(null)
const isLoading = ref(true)
const error = ref(null)

const fetchProgress = async () => {
  isLoading.value = true
  error.value = null
  try {
    const response = await get('/api/tshock/boss/progress')
    const data = await response.json()
    progressData.value = data
    // 对齐 wmDraft（Boss/事件/其他击败标记同一判定源，保证与接口同步刷新）
    if (isAdminUser) wmLoad()
  } catch (err) {
    error.value = '获取进度数据失败'
    console.error(err)
  } finally {
    isLoading.value = false
  }
}

const bossImageMap = {
  '史莱姆王': 'King_Slime.png',
  '克苏鲁之眼': 'Eye_of_Cthulhu.png',
  '世界吞噬者': 'Eater_of_Worlds.webp',
  '克苏鲁之脑': 'Brain_of_Cthulhu.png',
  '蜂后': 'QueenBee.png',
  '巨鹿': 'Deerclops.png',
  '骷髅王': 'Skeletron.png',
  '血肉墙': 'Wall_of_Flesh.png',
  '史莱姆皇后': 'Queen_Slime.png',
  '毁灭者': 'The_Destroyer.png',
  '机械骷髅王': 'Skeletron_Prime.png',
  '双子魔眼': 'The_Twins.png',
  '世纪之花': 'Plantera.png',
  '石巨人': 'Golem.png',
  '猪龙鱼公爵': 'Duke_Fishron.png',
  '光之女皇': 'Empress_of_Light.png',
  '拜月教教徒': 'Lunatic_Cultist.png',
  '月亮领主': 'Moon_Lord.png'
}
const eventImageMap = {
  '哥布林入侵': 'Goblin.webp',
  '海盗入侵': 'Flying_Dutchman.png',
  '日食': 'eclipse.webp',
  '火星人入侵': 'Martian_Saucer.png',
  '冰雪女王': 'Ice_Queen.png',
  '南瓜王': 'Pumpking.png'
}
const getBossImage = (name) => {
  const imageName = bossImageMap[name]
  return imageName ? `/assets/img/Boss/${imageName}` : null
}
const getEventImage = (name) => {
  const imageName = eventImageMap[name]
  return imageName ? `/assets/img/Boss/${imageName}` : null
}

// ═══════════════ 已击败标记（统一判定） ═══════════════
// 单一判定函数：Boss / 事件 / 其他击败标记全部走同一逻辑。
// 判定源 = WorldModify 字段（wmDraft，保存后本地即时同步）；未加载（非 admin/插件离线）
// 或无字段映射（日食 = 蛾怪击杀判定）时回退服务端 Downed，保证两套数据永不打架。
const downedKeyOf = (kind, item) => (kind === 'boss' ? bossDownedKeys : eventDownedKeys)[item?.Name] || null
const isDowned = (kind, item) => {
  const k = downedKeyOf(kind, item)
  // 字段已加载才用 wmDraft（区分「已加载但 false」与「未加载」）
  return k && wmDraft[k] !== undefined ? !!wmDraft[k] : !!item?.Downed
}
// 卡片与进度条共用同一判定，杜绝两套来源数字不一致
const downedBossCount = computed(() => (progressData.value?.Bosses || []).filter(b => isDowned('boss', b)).length)
const downedBossPercent = computed(() => {
  const total = progressData.value?.TotalBossCount || 0
  return total ? Math.round(downedBossCount.value * 100 / total) : 0
})
const downedEventCount = computed(() => (progressData.value?.Events || []).filter(e => isDowned('event', e)).length)
const downedEventPercent = computed(() => {
  const total = progressData.value?.TotalEventCount || 0
  return total ? Math.round(downedEventCount.value * 100 / total) : 0
})

const getStatusIcon = (completed) => (completed ? '✓' : '✗')
const getStatusClass = (completed) => (completed ? 'status-completed' : 'status-pending')

// ═══════════════ 悬浮窗：修改已击败标记 ═══════════════
const isAdminUser = isAdmin()
const modal = ref(null)
const modalSaving = ref(false)
const modalError = ref('')

const bossDownedKeys = {
  '史莱姆王': 'downedSlimeKing',
  '克苏鲁之眼': 'downedBoss1',
  '世界吞噬者': 'downedBoss2',
  '克苏鲁之脑': 'downedBoss2',
  '蜂后': 'downedQueenBee',
  '巨鹿': 'downedDeerclops',
  '骷髅王': 'downedBoss3',
  '血肉墙': 'hardMode',
  '史莱姆皇后': 'downedQueenSlime',
  '毁灭者': 'downedMechBoss1',
  '机械骷髅王': 'downedMechBoss3',
  '双子魔眼': 'downedMechBoss2',
  '世纪之花': 'downedPlantBoss',
  '石巨人': 'downedGolemBoss',
  '猪龙鱼公爵': 'downedFishron',
  '光之女皇': 'downedEmpressOfLight',
  '拜月教教徒': 'downedAncientCultist',
  '月亮领主': 'downedMoonlord'
}
const eventDownedKeys = {
  '哥布林入侵': 'downedGoblins',
  '海盗入侵': 'downedPirates',
  '火星人入侵': 'downedMartians',
  '冰雪女王': 'downedChristmasIceQueen',
  '南瓜王': 'downedHalloweenKing'
}

// 进度页已覆盖的击败字段（Bosses/Events 卡片），其余 WorldModify 独有击败标记归"其他击败标记"区
const coveredBossKeys = new Set([...Object.values(bossDownedKeys), ...Object.values(eventDownedKeys)])

const openModal = (kind, item) => {
  const fieldKey = downedKeyOf(kind, item)
  // 日食无独立击败字段（以蛾怪击杀判定），modal 直接提供「日食事件开关」
  // （原世界参数页 weather 组的 eclipse 已收敛到此，消除两页重复入口）
  const isEclipse = kind === 'event' && item.Name === '日食'
  modal.value = {
    kind,
    item,
    fieldKey: fieldKey || null,
    downed: isDowned(kind, item),
    eclipseFlag: isEclipse ? !!wmDraft['eclipse'] : undefined,
    image: kind === 'boss' ? getBossImage(item.Name) : getEventImage(item.Name)
  }
  modalError.value = ''
}

/** 其他击败标记（WorldModify 独有：四柱/撒旦军队难度/节日事件等）悬浮窗 */
const openOtherModal = (key) => {
  modal.value = {
    kind: 'other',
    item: { Name: wmMeta.value[key]?.label || key },
    fieldKey: key,
    downed: !!wmDraft[key],
    image: null
  }
  modalError.value = ''
}

const closeModal = () => {
  if (modalSaving.value) return
  modal.value = null
}

const saveModal = async () => {
  const m = modal.value
  if (!m || modalSaving.value) return
  // 组装提交字段：普通击败标记用 fieldKey；日食用 eclipse（无独立 downed 字段）
  const fields = {}
  if (m.eclipseFlag !== undefined) fields.eclipse = m.eclipseFlag
  else if (m.fieldKey) fields[m.fieldKey] = m.downed
  if (!Object.keys(fields).length) return
  modalSaving.value = true
  modalError.value = ''
  try {
    const data = await applyWorldModify(fields)
    if (data.error) throw new Error(data.error)
    for (const [k, v] of Object.entries(data.results || {})) {
      if (v && v !== 'ok') throw new Error(`${wmMeta.value[k]?.label || k}: ${v}`)
    }
    // 同步世界参数表单缓存，保证"其他击败标记"区与进度卡片一致
    for (const k of Object.keys(fields)) {
      wmOriginal[k] = fields[k]
      wmDraft[k] = fields[k]
    }
    modal.value = null
    fetchProgress()
  } catch (err) {
    modalError.value = '修改失败: ' + (err.message || '插件端离线或未响应')
  }
  modalSaving.value = false
}

// ═══════════════ 世界参数（WorldModify 字段，数据驱动） ═══════════════
const wmLoading = ref(false)
const wmApplying = ref(false)
const wmError = ref('')
const wmSuccess = ref('')
const wmGroups = ref([])
const wmMeta = ref({})
const wmOriginal = reactive({})
const wmDraft = reactive({})
const wmResults = ref(null)

/** 参数页隐藏字段：日食开关归进度页日食卡片管理（避免与事件进度重合）；月相/月亮样式只读中文名与 select 控件重复 */
const wmHiddenKeys = new Set(['eclipse', 'moonPhaseName', 'moonTypeName'])
const wmGroupKeys = (gid) => Object.keys(wmMeta.value).filter(k => wmMeta.value[k]?.group === gid && !wmHiddenKeys.has(k))
/** 参数页可见分组（排除 boss 组：击败状态统一由世界进度页管理，避免双入口重合） */
const wmVisibleGroups = computed(() => wmGroups.value.filter(g => g.id !== 'boss'))
/** 进度页"其他击败标记"区：WorldModify 独有的击败字段 */
const otherDownedItems = computed(() => {
  const items = []
  for (const k of Object.keys(wmMeta.value)) {
    if (wmMeta.value[k]?.group !== 'boss') continue
    if (coveredBossKeys.has(k)) continue
    items.push({ key: k, name: wmMeta.value[k].label || k, downed: !!wmDraft[k] })
  }
  return items
})
const wmIsChanged = (k) => wmDraft[k] !== wmOriginal[k]
const wmChangedKeys = computed(() =>
  Object.keys(wmDraft).filter(k => !wmHiddenKeys.has(k) && !wmMeta.value[k]?.readonly && wmDraft[k] !== wmOriginal[k])
)
const wmHasDangerChanged = computed(() => wmChangedKeys.value.some(k => wmMeta.value[k]?.danger))
const wmOptVal = (v) => (typeof v === 'string' && /^-?\d+$/.test(v) ? Number(v) : v)

const wmLoad = async () => {
  if (!isAdminUser) return
  wmLoading.value = true
  wmError.value = ''
  try {
    const data = await getWorldModifyStatus()
    if (data.error) throw new Error(data.error)
    wmGroups.value = data.groups || []
    wmMeta.value = data.meta || {}
    const f = data.fields || {}
    for (const k of Object.keys(wmOriginal)) { delete wmOriginal[k]; delete wmDraft[k] }
    for (const k of Object.keys(f)) {
      wmOriginal[k] = f[k]
      wmDraft[k] = f[k]
    }
    wmResults.value = null
  } catch (e) {
    wmError.value = '加载世界参数失败: ' + (e.message || '插件端离线或未响应')
  }
  wmLoading.value = false
}

const wmReset = () => {
  for (const k of Object.keys(wmDraft)) wmDraft[k] = wmOriginal[k]
}

const wmApply = async () => {
  const keys = wmChangedKeys.value
  if (!keys.length) return
  const dangerKeys = keys.filter(k => wmMeta.value[k]?.danger)
  if (dangerKeys.length) {
    const names = dangerKeys.map(k => wmMeta.value[k]?.label || k).join('、')
    if (!confirm(`以下为危险操作，确定应用吗？\n${names}`)) return
  }
  wmApplying.value = true
  wmError.value = ''
  wmSuccess.value = ''
  try {
    const fields = {}
    for (const k of keys) fields[k] = wmDraft[k]
    const data = await applyWorldModify(fields)
    if (data.error) throw new Error(data.error)
    wmResults.value = data.results || {}
    for (const [k, v] of Object.entries(wmResults.value)) {
      if (v === 'ok') wmOriginal[k] = wmDraft[k]
    }
    const failed = Object.values(wmResults.value).filter(v => v !== 'ok').length
    wmSuccess.value = `已应用 ${data.applied ?? 0} 个字段${failed ? `，${failed} 个失败` : ''}`
    setTimeout(() => { wmSuccess.value = '' }, 4000)
    // 世界参数可能影响进度（如 hardMode），顺带刷新进度
    fetchProgress()
  } catch (e) {
    wmError.value = '应用失败: ' + (e.message || '插件端离线或未响应')
  }
  wmApplying.value = false
}

/** 顶部刷新：按当前 Tab 刷新对应数据 */
const refreshCurrent = () => {
  if (activeTab.value === 'params') wmLoad()
  else fetchProgress()
}

onMounted(() => {
  fetchProgress()
  wmLoad()
})
</script>

<template>
  <div class="progress-container">
    <div class="page-header">
      <h2>世界信息</h2>
      <button class="refresh-btn" @click="refreshCurrent" :disabled="activeTab === 'params' ? wmLoading : isLoading">
        <span v-if="activeTab === 'params' ? wmLoading : isLoading">加载中...</span>
        <span v-else>刷新</span>
      </button>
    </div>

    <!-- 顶部 Tab -->
    <div class="view-tabs">
      <button
        class="view-tab"
        :class="{ active: activeTab === 'params' }"
        @click="activeTab = 'params'"
      >世界参数</button>
      <button
        class="view-tab"
        :class="{ active: activeTab === 'progress' }"
        @click="activeTab = 'progress'"
      >世界进度</button>
    </div>

    <!-- ═══════════ Tab：世界参数（默认） ═══════════ -->
    <div v-if="activeTab === 'params'" class="params-view">
      <div v-if="!isAdminUser" class="no-perm-tip">
        当前账号无修改权限（仅 admin 可修改世界参数）
      </div>

      <template v-else>
        <div class="wm-toolbar">
          <button class="btn-secondary" @click="wmLoad" :disabled="wmApplying">刷新</button>
          <button class="btn-secondary" @click="wmReset" :disabled="wmApplying || !wmChangedKeys.length">重置</button>
          <button
            class="refresh-btn"
            :disabled="wmApplying || !wmChangedKeys.length"
            @click="wmApply"
          >
            {{ wmApplying ? '应用中...' : `应用更改（${wmChangedKeys.length}）` }}
          </button>
          <span v-if="wmHasDangerChanged" class="wm-warn">⚠ 含危险字段</span>
        </div>

        <div v-if="wmError" class="error-box">{{ wmError }}</div>
        <div v-if="wmSuccess" class="success-box">{{ wmSuccess }}</div>

        <div v-if="wmResults" class="wm-results">
          <div v-for="(v, k) in wmResults" :key="k" :class="v === 'ok' ? 'r-ok' : 'r-err'">
            {{ wmMeta[k]?.label || k }}：{{ v }}
          </div>
        </div>

        <Loading v-if="wmLoading" text="正在加载世界参数..." />

        <div v-else class="wm-grid">
          <div v-for="g in wmVisibleGroups" :key="g.id" class="wm-card">
            <h4>{{ g.label }}</h4>
            <div class="wm-field-list">
              <div
                v-for="k in wmGroupKeys(g.id)"
                :key="k"
                class="wm-field-row"
                :class="{ changed: wmIsChanged(k), wide: wmMeta[k].type !== 'bool' }"
              >
                <span class="wm-label" :title="wmMeta[k].label">{{ wmMeta[k].label }}</span>
                <div class="wm-control">
                  <span v-if="wmMeta[k].readonly" class="wm-readonly">
                    {{ wmMeta[k].type === 'bool' ? (wmDraft[k] ? '是' : '否') : wmDraft[k] }}
                  </span>
                  <label v-else-if="wmMeta[k].type === 'bool'" class="switch" :class="{ 'danger-switch': wmMeta[k].danger }">
                    <input type="checkbox" v-model="wmDraft[k]" />
                    <span class="slider"></span>
                  </label>
                  <select v-else-if="wmMeta[k].type === 'select'" v-model="wmDraft[k]" class="wm-select">
                    <option v-for="(label, val) in wmMeta[k].options" :key="val" :value="wmOptVal(val)">
                      {{ label }}
                    </option>
                  </select>
                  <input
                    v-else-if="wmMeta[k].type === 'number' || wmMeta[k].type === 'float'"
                    v-model.number="wmDraft[k]"
                    type="number"
                    :step="wmMeta[k].type === 'float' ? 0.05 : 1"
                    class="wm-input"
                  />
                  <input v-else v-model="wmDraft[k]" type="text" class="wm-input" />
                  <span v-if="wmMeta[k].danger" class="danger-badge">危险</span>
                  <span v-if="wmIsChanged(k)" class="change-badge">已修改</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </template>
    </div>

    <!-- ═══════════ Tab：世界进度 ═══════════ -->
    <div v-else class="progress-view">
      <div v-if="error" class="error-box">{{ error }}</div>

      <Loading v-if="isLoading" text="正在加载进度数据..." />

      <div v-else-if="progressData" class="progress-content">
        <div class="boss-section">
          <div class="section-header">
            <h3>Boss击杀进度</h3>
            <span class="progress-badge success">{{ downedBossCount }}/{{ progressData.TotalBossCount }}</span>
          </div>
          <p class="section-tip">✓ = 已击败标记（可点击卡片修改）；卡片小字为图鉴击杀记录</p>

          <div class="progress-bar-container">
            <div
              class="progress-bar boss"
              :style="{ width: downedBossPercent + '%' }"
            ></div>
          </div>

          <div class="card-grid">
            <div
              v-for="boss in progressData.Bosses"
              :key="boss.NPCID"
              class="boss-card"
              :class="{ completed: isDowned('boss', boss), clickable: isAdminUser }"
              @click="isAdminUser && openModal('boss', boss)"
            >
              <div class="card-image">
                <img
                  v-if="getBossImage(boss.Name)"
                  :src="getBossImage(boss.Name)"
                  :alt="boss.Name"
                  @error="($event.target.style.display = 'none')"
                />
                <div v-else class="image-placeholder">
                  <span>?</span>
                </div>
                <div class="status-badge" :class="getStatusClass(isDowned('boss', boss))">
                  {{ getStatusIcon(isDowned('boss', boss)) }}
                </div>
                <span v-if="isAdminUser" class="card-edit-hint">点击修改</span>
              </div>
              <div class="card-info">
                <span class="boss-name">{{ boss.Name }}</span>
                <span class="boss-count">{{ boss.KillCount }} 击杀</span>
              </div>
            </div>
          </div>
        </div>

        <div class="event-section">
          <div class="section-header">
            <h3>事件进度</h3>
            <span class="progress-badge warning">{{ downedEventCount }}/{{ progressData.TotalEventCount }}</span>
          </div>
          <p class="section-tip">✓ = 已击败标记（可点击卡片修改）</p>

          <div class="progress-bar-container">
            <div
              class="progress-bar event"
              :style="{ width: downedEventPercent + '%' }"
            ></div>
          </div>

          <div class="card-grid">
            <div
              v-for="eventItem in progressData.Events"
              :key="eventItem.EventID"
              class="boss-card"
              :class="{ completed: isDowned('event', eventItem), clickable: isAdminUser }"
              @click="isAdminUser && openModal('event', eventItem)"
            >
              <div class="card-image">
                <img
                  v-if="getEventImage(eventItem.Name)"
                  :src="getEventImage(eventItem.Name)"
                  :alt="eventItem.Name"
                  @error="($event.target.style.display = 'none')"
                />
                <div v-else class="image-placeholder">
                  <span>?</span>
                </div>
                <div class="status-badge" :class="getStatusClass(isDowned('event', eventItem))">
                  {{ getStatusIcon(isDowned('event', eventItem)) }}
                </div>
                <span v-if="isAdminUser" class="card-edit-hint">点击修改</span>
              </div>
              <div class="card-info">
                <span class="boss-name">{{ eventItem.Name }}</span>
              </div>
            </div>
          </div>
        </div>

        <!-- 其他击败标记（WorldModify 独有：四柱/撒旦军队难度/节日事件） -->
        <div v-if="isAdminUser && otherDownedItems.length" class="event-section">
          <div class="section-header">
            <h3>其他击败标记</h3>
            <span class="progress-badge warning">{{ otherDownedItems.filter(o => o.downed).length }}/{{ otherDownedItems.length }}</span>
          </div>
          <p class="section-tip">四柱 / 撒旦军队难度 / 节日事件等（点击卡片修改）</p>

          <div class="card-grid">
            <div
              v-for="o in otherDownedItems"
              :key="o.key"
              class="boss-card"
              :class="{ completed: o.downed, clickable: true }"
              @click="openOtherModal(o.key)"
            >
              <div class="card-image">
                <div class="image-placeholder"><span>?</span></div>
                <div class="status-badge" :class="getStatusClass(o.downed)">
                  {{ getStatusIcon(o.downed) }}
                </div>
                <span class="card-edit-hint">点击修改</span>
              </div>
              <div class="card-info">
                <span class="boss-name">{{ o.name }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ═══════════ 悬浮窗：修改已击败标记 ═══════════ -->
    <div v-if="modal" class="modal-mask" @click.self="closeModal">
      <div class="modal-box">
        <div class="modal-head">
          <h3>{{ modal.kind === 'boss' ? 'Boss 已击败标记' : modal.kind === 'event' ? '事件已击败标记' : '击败标记' }}</h3>
          <button class="modal-close" @click="closeModal" :disabled="modalSaving">×</button>
        </div>

        <div class="modal-body">
          <div class="modal-image">
            <img v-if="modal.image" :src="modal.image" :alt="modal.item.Name" />
            <div v-else class="image-placeholder"><span>?</span></div>
          </div>
          <div class="modal-info">
            <div class="modal-name">{{ modal.item.Name }}</div>
            <div v-if="modal.kind === 'boss'" class="modal-sub">{{ modal.item.KillCount }} 击杀</div>
            <div v-if="modal.eclipseFlag === undefined" class="modal-sub">当前：{{ modal.downed ? '已击败 ✓' : '未击败 ✗' }}</div>
            <div v-else class="modal-sub">当前日食事件：{{ modal.eclipseFlag ? '开启' : '关闭' }}</div>
          </div>

          <div v-if="modal.eclipseFlag !== undefined" class="modal-edit">
            <label class="switch">
              <input type="checkbox" v-model="modal.eclipseFlag" :disabled="modalSaving" />
              <span class="slider"></span>
            </label>
            <span class="modal-edit-label">{{ modal.eclipseFlag ? '开启日食事件' : '关闭日食事件' }}</span>
          </div>
          <div v-else-if="modal.fieldKey" class="modal-edit">
            <label class="switch">
              <input type="checkbox" v-model="modal.downed" :disabled="modalSaving" />
              <span class="slider"></span>
            </label>
            <span class="modal-edit-label">{{ modal.downed ? '标记为已击败' : '标记为未击败' }}</span>
          </div>
          <div v-else class="modal-edit readonly">
            <span>该条目没有可修改的击败标记（日食以蛾怪击杀记录判定）</span>
          </div>

          <div v-if="modalError" class="error-box">{{ modalError }}</div>
        </div>

        <div class="modal-foot">
          <button class="btn-secondary" @click="closeModal" :disabled="modalSaving">取消</button>
          <button
            v-if="modal.fieldKey || modal.eclipseFlag !== undefined"
            class="refresh-btn"
            :disabled="modalSaving"
            @click="saveModal"
          >
            {{ modalSaving ? '保存中...' : '保存' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.progress-container {
  padding: 24px;
  max-width: 1600px;
  margin: 0 auto;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.page-header h2 {
  font-size: 1.5rem;
  font-weight: 600;
  color: var(--text-primary);
}

.refresh-btn {
  padding: 8px 16px;
  background: var(--accent-primary);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  transition: all 0.2s ease;
}

.refresh-btn:hover:not(:disabled) {
  background: #4f46e5;
  transform: translateY(-2px);
}

.refresh-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-secondary {
  padding: 8px 16px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  transition: all 0.2s ease;
}

.btn-secondary:hover:not(:disabled) {
  background: var(--bg-hover);
}

.btn-secondary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* ═══════════ 顶部 Tab ═══════════ */
.view-tabs {
  display: flex;
  gap: 4px;
  margin-bottom: 20px;
  border-bottom: 1px solid var(--border-light);
}

.view-tab {
  padding: 10px 22px;
  color: var(--text-secondary);
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  font-size: 0.95rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
  position: relative;
  top: 1px;
}

.view-tab:hover {
  color: var(--text-primary);
}

.view-tab.active {
  color: var(--accent-primary);
  border-bottom-color: var(--accent-primary);
  font-weight: 600;
}

.no-perm-tip {
  color: var(--text-muted);
  font-size: 0.9rem;
  padding: 24px 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
}

.error-box {
  padding: 16px;
  background: rgba(239, 68, 68, 0.1);
  border: 1px solid rgba(239, 68, 68, 0.2);
  border-radius: var(--radius-md);
  color: #dc2626;
  margin-bottom: 16px;
}

.success-box {
  padding: 12px 16px;
  background: rgba(34, 197, 94, 0.1);
  border: 1px solid rgba(34, 197, 94, 0.3);
  border-radius: var(--radius-md);
  color: #22c55e;
  margin-bottom: 14px;
  font-size: 0.9rem;
}

/* ═══════════ 世界参数 ═══════════ */
.wm-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 14px;
  flex-wrap: wrap;
}

.wm-warn {
  color: #f59e0b;
  font-size: 0.85rem;
  font-weight: 600;
}

.wm-results {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  padding: 12px 16px;
  margin-bottom: 16px;
  font-size: 0.85rem;
  max-height: 200px;
  overflow-y: auto;
}

.r-ok { color: #22c55e; }
.r-err { color: #ef4444; word-break: break-all; }

.wm-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(560px, 1fr));
  gap: 16px;
}

.wm-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  padding: 16px;
}

.wm-card h4 {
  margin: 0 0 10px 0;
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 600;
  border-bottom: 1px solid var(--border-light);
  padding-bottom: 8px;
}

.wm-field-list {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 6px 16px;
}

.wm-field-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 4px 8px;
  border-radius: var(--radius-sm);
  min-width: 0;
}

/* 非 bool 字段（文本/数字/下拉）占整行 */
.wm-field-row.wide {
  grid-column: 1 / -1;
}

.wm-field-row.changed {
  background: rgba(99, 102, 241, 0.08);
}

.wm-label {
  color: var(--text-secondary);
  font-size: 0.85rem;
  flex: 1;
  min-width: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.wm-control {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.wm-input {
  width: 130px;
  padding: 5px 10px;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-sm);
  background: var(--bg-tertiary);
  color: var(--text-primary);
  font-size: 0.85rem;
}

.wm-input:focus, .wm-select:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.wm-select {
  width: 170px;
  padding: 5px 10px;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-sm);
  background: var(--bg-tertiary);
  color: var(--text-primary);
  font-size: 0.85rem;
}

.wm-readonly {
  color: var(--text-muted);
  font-size: 0.85rem;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-sm);
  padding: 4px 10px;
  min-width: 60px;
  text-align: center;
}

.danger-badge {
  color: #f59e0b;
  font-size: 0.72rem;
  border: 1px solid rgba(245, 158, 11, 0.4);
  border-radius: 6px;
  padding: 1px 6px;
}

.change-badge {
  color: #818cf8;
  font-size: 0.72rem;
  border: 1px solid rgba(99, 102, 241, 0.4);
  border-radius: 6px;
  padding: 1px 6px;
  white-space: nowrap;
}

/* ═══════════ 世界进度 ═══════════ */
.progress-content {
  display: flex;
  flex-direction: column;
  gap: 32px;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.section-header h3 {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
}

.section-tip {
  margin: 0 0 14px 0;
  color: var(--text-muted);
  font-size: 0.78rem;
}

.progress-badge {
  padding: 6px 14px;
  color: white;
  border-radius: 20px;
  font-size: 0.85rem;
  font-weight: 600;
}

.progress-badge.success {
  background: linear-gradient(135deg, #10b981, #34d399);
}

.progress-badge.warning {
  background: linear-gradient(135deg, #8b5cf6, #a78bfa);
}

.progress-bar-container {
  height: 8px;
  background: var(--bg-secondary);
  border-radius: var(--radius-sm);
  overflow: hidden;
  margin-bottom: 20px;
}

.progress-bar {
  height: 100%;
  border-radius: var(--radius-sm);
  transition: width 0.5s ease;
}

.progress-bar.boss {
  background: linear-gradient(90deg, #10b981, #34d399);
}

.progress-bar.event {
  background: linear-gradient(90deg, #8b5cf6, #a78bfa);
}

.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 16px;
}

.boss-card {
  background: var(--bg-card);
  border-radius: var(--radius-lg);
  border: 1px solid var(--border-light);
  overflow: hidden;
  transition: all 0.3s ease;
  position: relative;
}

.boss-card:hover {
  transform: translateY(-4px);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  border-color: var(--accent-primary);
}

.boss-card.clickable {
  cursor: pointer;
}

.boss-card.completed {
  border-color: rgba(16, 185, 129, 0.4);
}

.boss-card.completed:hover {
  border-color: rgba(16, 185, 129, 0.6);
}

.card-image {
  position: relative;
  width: 100%;
  height: 120px;
  background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.card-image img {
  width: 80%;
  height: 80%;
  object-fit: contain;
  filter: drop-shadow(0 4px 8px rgba(0, 0, 0, 0.3));
}

.image-placeholder {
  width: 80%;
  height: 80%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(255, 255, 255, 0.1);
  border-radius: var(--radius-md);
  color: var(--text-secondary);
  font-size: 2rem;
}

.status-badge {
  position: absolute;
  top: 8px;
  right: 8px;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.9rem;
  font-weight: bold;
  color: white;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
}

.status-badge.status-completed {
  background: linear-gradient(135deg, #10b981, #059669);
}

.status-badge.status-pending {
  background: linear-gradient(135deg, #ef4444, #dc2626);
}

.card-edit-hint {
  position: absolute;
  bottom: 6px;
  left: 50%;
  transform: translateX(-50%);
  background: rgba(0, 0, 0, 0.6);
  color: #fff;
  font-size: 0.7rem;
  padding: 2px 10px;
  border-radius: 10px;
  opacity: 0;
  transition: opacity 0.2s ease;
  pointer-events: none;
}

.boss-card:hover .card-edit-hint {
  opacity: 1;
}

.card-info {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.boss-name {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--text-primary);
  text-align: center;
}

.boss-count {
  font-size: 0.75rem;
  color: var(--text-secondary);
  text-align: center;
}

/* ═══════════ 悬浮窗 ═══════════ */
.modal-mask {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.55);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 20px;
}

.modal-box {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-lg);
  width: 100%;
  max-width: 380px;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.4);
}

.modal-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border-light);
}

.modal-head h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.05rem;
}

.modal-close {
  background: none;
  border: none;
  color: var(--text-secondary);
  font-size: 1.4rem;
  cursor: pointer;
  line-height: 1;
  padding: 0 4px;
}

.modal-close:hover { color: var(--text-primary); }
.modal-close:disabled { opacity: 0.4; cursor: not-allowed; }

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14px;
}

.modal-image {
  width: 140px;
  height: 140px;
  background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
  border-radius: var(--radius-md);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.modal-image img {
  width: 85%;
  height: 85%;
  object-fit: contain;
  filter: drop-shadow(0 4px 10px rgba(0, 0, 0, 0.35));
}

.modal-info {
  text-align: center;
}

.modal-name {
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--text-primary);
}

.modal-sub {
  margin-top: 4px;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.modal-edit {
  display: flex;
  align-items: center;
  gap: 12px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  padding: 12px 18px;
  width: 100%;
  justify-content: center;
}

.modal-edit.readonly {
  color: var(--text-muted);
  font-size: 0.85rem;
}

.modal-edit-label {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--text-primary);
}

.modal-foot {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 14px 20px;
  border-top: 1px solid var(--border-light);
}

/* ═══════════ Switch（通用） ═══════════ */
.switch {
  position: relative;
  display: inline-block;
  width: 46px;
  height: 25px;
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
  inset: 0;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-light);
  border-radius: 25px;
  transition: 0.25s;
}

.slider::before {
  content: '';
  position: absolute;
  height: 17px;
  width: 17px;
  left: 3px;
  top: 3px;
  background: var(--text-muted);
  border-radius: 50%;
  transition: 0.25s;
}

.switch input:checked + .slider {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
}

.switch input:checked + .slider::before {
  transform: translateX(21px);
  background: white;
}

.switch.danger-switch input:checked + .slider {
  background: #f59e0b;
  border-color: #f59e0b;
}

@media (max-width: 640px) {
  .card-grid {
    grid-template-columns: repeat(auto-fill, minmax(110px, 1fr));
    gap: 12px;
  }

  .card-image {
    height: 100px;
  }

  .wm-grid {
    grid-template-columns: 1fr;
  }

  .wm-field-list {
    grid-template-columns: 1fr;
  }
}
</style>
