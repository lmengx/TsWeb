<script setup>
import { ref, computed, onMounted } from 'vue'
import { listHouses, listBuildings, getBuildingInfo,
  exportBuildingToLocal, exportBuildingToBackend, getOnlinePlayers,
  deleteLocalBuilding, listBackendBuildings, sendBuildingToBackend,
  uploadBuildingToPlugin, importBuildingToWorld, deleteBackendBuilding, downloadBackendBuilding } from '../../api/houseApi.js'
import Loading from '../../components/Loading.vue'

// ═══ 顶部主 Tab ═══
const mainTab = ref('houses')   // 'houses' | 'buildings'

// ═══ Tab1 · 房屋管理 ═══
const houses = ref([])
const houseTotal = ref(0)
const housePage = ref(1)
const housePageSize = 10
const housesLoading = ref(false)
const housesError = ref('')
const expanded = ref({})
const exportMenu = ref(null)    // 当前展开导出菜单的房屋名
const exportBusy = ref('')      // 正在导出的房屋名

const houseTotalPages = computed(() => Math.max(1, Math.ceil(houseTotal.value / housePageSize)))

const PERMS = [
  { key: 'entry', label: '进入' }, { key: 'tp', label: '传送' }, { key: 'place', label: '放置' },
  { key: 'break', label: '破坏' }, { key: 'explosion', label: '爆炸物' }, { key: 'liquid', label: '液体' },
  { key: 'chest', label: '箱子' },
  { key: 'plant', label: '植物' }, { key: 'spawn', label: '复活点' }, { key: 'grave', label: '挖坟' },
  { key: 'switch', label: '开关' }, { key: 'door', label: '门' }, { key: 'fragile', label: '易碎品' }
]

const fetchHouses = async () => {
  housesLoading.value = true
  housesError.value = ''
  try {
    const result = await listHouses(housePage.value, housePageSize)
    if (result.error) housesError.value = result.error
    else { houses.value = result.items || []; houseTotal.value = result.total || 0 }
  } catch (err) { housesError.value = err.message }
  housesLoading.value = false
}

const toggleHouse = (name) => { expanded.value[name] = !expanded.value[name] }
const housePrev = () => { if (housePage.value > 1) { housePage.value--; fetchHouses() } }
const houseNext = () => { if (housePage.value < houseTotalPages.value) { housePage.value++; fetchHouses() } }
const areaText = (h) => `(${h.area.x}, ${h.area.y}) → (${h.area.x + h.area.width - 1}, ${h.area.y + h.area.height - 1})`

// 房屋导出（插件本地 / 后端）
const doExport = async (house, target) => {
  exportBusy.value = house.name
  try {
    if (target === 'local') {
      const r = await exportBuildingToLocal(house.name)
      if (r.error) notify(r.error, 'error')
      else notify(`已导出到插件本地：${r.file}（${r.width}×${r.height}）`, 'ok')
    } else {
      const r = await exportBuildingToBackend(house.name)
      if (r.error) notify(r.error, 'error')
      else notify(`已导出到后端：${r.name}（${fmtBytes(r.size)}）`, 'ok')
    }
  } catch (e) { notify(e.message, 'error') }
  exportBusy.value = ''
  exportMenu.value = null
}

// ═══ Tab2 · 建筑存档 ═══
const sourceTab = ref('local')   // 'local' | 'backend'
const buildings = ref([])
const buildingsTotal = ref(0)
const buildingsPage = ref(1)
const buildingsPageSize = 20
const buildingsLoading = ref(false)
const buildingsError = ref('')
const backendFiles = ref([])
const backendLoading = ref(false)
const backendError = ref('')
const backendLoaded = ref(false)

const buildingsTotalPages = computed(() => Math.max(1, Math.ceil(buildingsTotal.value / buildingsPageSize)))

const fetchBuildings = async () => {
  buildingsLoading.value = true
  buildingsError.value = ''
  try {
    const result = await listBuildings(buildingsPage.value, buildingsPageSize)
    if (result.error) buildingsError.value = result.error
    else { buildings.value = result.items || []; buildingsTotal.value = result.total || 0 }
  } catch (err) { buildingsError.value = err.message }
  buildingsLoading.value = false
}

const fetchBackend = async () => {
  backendLoading.value = true
  backendError.value = ''
  try {
    const result = await listBackendBuildings()
    if (result.error) backendError.value = result.error
    else backendFiles.value = result.files || []
  } catch (err) { backendError.value = err.message }
  backendLoading.value = false
  backendLoaded.value = true
}

const switchSource = (tab) => {
  sourceTab.value = tab
  if (tab === 'backend' && !backendLoaded.value) fetchBackend()
}

const switchMain = (tab) => {
  mainTab.value = tab
  if (tab === 'buildings' && sourceTab.value === 'backend' && !backendLoaded.value) fetchBackend()
}

const buildingsPrev = () => { if (buildingsPage.value > 1) { buildingsPage.value--; fetchBuildings() } }
const buildingsNext = () => { if (buildingsPage.value < buildingsTotalPages.value) { buildingsPage.value++; fetchBuildings() } }

// 详情弹窗（插件本地 .tsb 完整外壳）
const showDetail = ref(false)
const detailLoading = ref(false)
const detail = ref(null)
const openDetail = async (file) => {
  detail.value = null
  showDetail.value = true
  detailLoading.value = true
  try {
    const result = await getBuildingInfo(file)
    detail.value = result.error ? { error: result.error } : result
  } catch (err) { detail.value = { error: err.message } }
  detailLoading.value = false
}
const closeDetail = () => { showDetail.value = false; detail.value = null }

// 详情弹窗（后端 .tsb，基于列表 meta）
const showBackendDetail = ref(false)
const backendDetail = ref(null)
const openBackendDetail = (f) => { backendDetail.value = f; showBackendDetail.value = true }
const closeBackendDetail = () => { showBackendDetail.value = false; backendDetail.value = null }

// ═══ 导入向导 ═══
const showImport = ref(false)
const importFile = ref('')
const importSource = ref('local')
const importAnchor = ref('player')
const importPlayer = ref('')
const importCoordsX = ref('')
const importCoordsY = ref('')
const importHouse = ref('')
const importAlign = ref('center')
const importBusy = ref(false)
const importResult = ref(null)
const onlinePlayers = ref([])
const houseOptions = ref([])

const ALIGNS = [
  { key: 'topLeft', label: '左上' },
  { key: 'topRight', label: '右上' },
  { key: 'bottomLeft', label: '左下' },
  { key: 'bottomRight', label: '右下' },
  { key: 'center', label: '中心' }
]

const openImport = (file, source) => {
  importFile.value = file
  importSource.value = source
  importResult.value = null
  showImport.value = true
  if (onlinePlayers.value.length === 0) refreshPlayers()
  if (houseOptions.value.length === 0) refreshHouseOptions()
}
const closeImport = () => { showImport.value = false; importResult.value = null }

const refreshPlayers = async () => {
  try {
    const r = await getOnlinePlayers()
    onlinePlayers.value = r.players || []
  } catch { onlinePlayers.value = [] }
}
const refreshHouseOptions = async () => {
  try {
    const r = await listHouses(1, 100)
    houseOptions.value = r.items || []
  } catch { houseOptions.value = [] }
}

const doImport = async () => {
  importBusy.value = true
  importResult.value = null
  try {
    // 后端来源：先上传到插件 TSWeb/Buildings/
    if (importSource.value === 'backend') {
      const up = await uploadBuildingToPlugin(importFile.value)
      if (up.error) {
        importResult.value = { ok: false, msg: '上传到插件失败：' + up.error }
        return
      }
    }
    const payload = { file: importFile.value, anchor: importAnchor.value, align: importAlign.value }
    if (importAnchor.value === 'player') payload.anchorPlayer = importPlayer.value
    else if (importAnchor.value === 'coords') payload.coords = `${importCoordsX.value},${importCoordsY.value}`
    else if (importAnchor.value === 'house') payload.anchorHouse = importHouse.value

    const r = await importBuildingToWorld(payload)
    if (r.success) {
      importResult.value = { ok: true, msg: `导入成功：${r.width}×${r.height}，起始 (${r.startX}, ${r.startY})` }
      fetchBuildings()
    } else {
      importResult.value = { ok: false, msg: r.error || '导入失败' }
    }
  } catch (e) {
    importResult.value = { ok: false, msg: e.message }
  }
  importBusy.value = false
}

// ═══ 文件操作 ═══
const sending = ref('')
const doSendToBackend = async (file) => {
  sending.value = file
  try {
    const r = await sendBuildingToBackend(file)
    if (r.error) notify(r.error, 'error')
    else {
      notify(`已发送到后端：${r.name}（${fmtBytes(r.size)}），本地已移除`, 'ok')
      fetchBuildings()
      fetchBackend()
    }
  } catch (e) { notify(e.message, 'error') }
  sending.value = ''
}

const deleting = ref('')
const doDeleteLocal = async (file) => {
  if (!confirm(`确定删除插件本地建筑文件 ${file}？`)) return
  deleting.value = file
  try {
    const r = await deleteLocalBuilding(file)
    if (r.error) notify(r.error, 'error')
    else { notify('已删除', 'ok'); fetchBuildings() }
  } catch (e) { notify(e.message, 'error') }
  deleting.value = ''
}
const doDeleteBackend = async (file) => {
  if (!confirm(`确定删除后端建筑文件 ${file}？`)) return
  deleting.value = file
  try {
    const r = await deleteBackendBuilding(file)
    if (r.error) notify(r.error, 'error')
    else { notify('已删除', 'ok'); fetchBackend() }
  } catch (e) { notify(e.message, 'error') }
  deleting.value = ''
}
const doDownloadBackend = async (file) => {
  try { await downloadBackendBuilding(file) }
  catch (e) { notify(e.message, 'error') }
}

// ═══ 工具 ═══
const toast = ref(null)
const notify = (msg, type) => {
  toast.value = { msg, type }
  setTimeout(() => { toast.value = null }, 3200)
}
const fmtBytes = (n) => {
  if (!n && n !== 0) return '-'
  if (n < 1024) return n + ' B'
  if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB'
  return (n / 1024 / 1024).toFixed(2) + ' MB'
}
const fmtDate = (s) => (s || '-').replace('T', ' ').slice(0, 19)

onMounted(() => { fetchHouses() })
</script>

<template>
  <div class="house-mgmt">
    <div class="page-header">
      <h2>房屋与建筑管理</h2>
      <span class="sub">HouseRegion 圈地数据 · .tsb 建筑导入导出</span>
    </div>

    <!-- 顶部主 Tab -->
    <div class="main-tabs">
      <button class="main-tab" :class="{ active: mainTab === 'houses' }" @click="mainTab = 'houses'">🏠 房屋管理</button>
      <button class="main-tab" :class="{ active: mainTab === 'buildings' }" @click="switchMain('buildings')">📦 建筑存档</button>
    </div>

    <!-- ═══ Tab1 房屋管理 ═══ -->
    <div v-if="mainTab === 'houses'" class="section">
      <div v-if="housesError" class="error-message">{{ housesError }}</div>
      <div v-if="housesLoading"><Loading text="加载中..." /></div>
      <div v-else-if="houses.length === 0" class="empty-state">暂无房屋数据（游戏中 /h c 圈地创建）</div>

      <div v-else class="house-list">
        <div v-for="h in houses" :key="h.name" class="house-card" :class="{ expanded: expanded[h.name] }">
          <div class="house-head" @click="toggleHouse(h.name)">
            <div class="house-title">
              <span class="house-name">{{ h.name }}</span>
              <span class="house-author">房主：{{ h.authorName || h.author }}</span>
            </div>
            <span class="house-area">{{ h.area.width }}×{{ h.area.height }}</span>
            <span class="export-zone" @click.stop>
              <button class="export-btn" @click="exportMenu = exportMenu === h.name ? null : h.name" :disabled="exportBusy === h.name">
                {{ exportBusy === h.name ? '导出中…' : '导出 ▼' }}
              </button>
              <div v-if="exportMenu === h.name" class="export-menu">
                <button class="export-opt" @click="doExport(h, 'local')">💾 插件端本地</button>
                <button class="export-opt" @click="doExport(h, 'backend')">☁️ 后端</button>
              </div>
            </span>
            <span class="expand-arrow" :class="{ rotated: expanded[h.name] }">▼</span>
          </div>

          <div v-if="expanded[h.name]" class="house-detail">
            <div class="detail-grid">
              <div class="detail-item">
                <div class="detail-label">区域范围</div>
                <div class="detail-value">{{ areaText(h) }}</div>
              </div>
              <div class="detail-item">
                <div class="detail-label">传送点</div>
                <div class="detail-value">({{ h.tp.x }}, {{ h.tp.y }})</div>
              </div>
              <div class="detail-item">
                <div class="detail-label">驱离点</div>
                <div class="detail-value" :class="{ 'text-muted': !h.expel }">{{ h.expel ? `(${h.expel.x}, ${h.expel.y})` : '未设置' }}</div>
              </div>
              <div class="detail-item">
                <div class="detail-label">违规驱离</div>
                <div class="detail-value">
                  <span class="perm-chip" :class="h.expelOnViolate === 1 ? 'on' : 'off'">{{ h.expelOnViolate === 1 ? '✓ 开' : '✗ 关' }}</span>
                </div>
              </div>
            </div>
            <div class="detail-row">
              <div class="detail-label">共有者</div>
              <div class="chips">
                <span v-for="n in h.ownerNames" :key="n" class="perm-chip on">{{ n }}</span>
                <span v-if="!h.ownerNames || h.ownerNames.length === 0" class="text-muted">无</span>
              </div>
            </div>
            <div class="detail-row">
              <div class="detail-label">使用者</div>
              <div class="chips">
                <span v-for="n in h.userNames" :key="n" class="perm-chip mid">{{ n }}</span>
                <span v-if="!h.userNames || h.userNames.length === 0" class="text-muted">无</span>
              </div>
            </div>
            <div class="detail-row">
              <div class="detail-label">权限</div>
              <div class="chips">
                <span v-for="p in PERMS" :key="p.key" class="perm-chip" :class="h.permissions[p.key] === 1 ? 'on' : 'off'">
                  {{ p.label }} {{ h.permissions[p.key] === 1 ? '✓' : '✗' }}
                </span>
              </div>
            </div>
            <div class="detail-row">
              <div class="detail-label">通知</div>
              <div class="chips">
                <span class="perm-chip" :class="h.notify.breakPlace === 1 ? 'on' : 'off'">破坏通知 {{ h.notify.breakPlace === 1 ? '开' : '关' }}</span>
                <span class="perm-chip" :class="h.notify.enter === 1 ? 'on' : 'off'">进入通知 {{ h.notify.enter === 1 ? '开' : '关' }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div v-if="houseTotal > 0" class="pagination">
        <button @click="housePrev" :disabled="housePage <= 1">← 上一页</button>
        <span class="page-info">第 {{ housePage }} / {{ houseTotalPages }} 页（共 {{ houseTotal }} 个房屋）</span>
        <button @click="houseNext" :disabled="housePage >= houseTotalPages">下一页 →</button>
      </div>
    </div>

    <!-- ═══ Tab2 建筑存档 ═══ -->
    <div v-else class="section">
      <div class="sub-tabs">
        <button class="sub-tab" :class="{ active: sourceTab === 'local' }" @click="switchSource('local')">插件本地（TSWeb/Buildings）</button>
        <button class="sub-tab" :class="{ active: sourceTab === 'backend' }" @click="switchSource('backend')">后端（data/transfer/building）</button>
      </div>

      <!-- 插件本地 -->
      <div v-if="sourceTab === 'local'">
        <div v-if="buildingsError" class="error-message">{{ buildingsError }}</div>
        <div v-if="buildingsLoading"><Loading text="加载中..." /></div>
        <div v-else-if="buildings.length === 0" class="empty-state">暂无建筑存档（房屋卡片「导出」或游戏中 /h export 生成）</div>
        <div v-else class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th>文件名</th><th>建筑名</th><th>作者</th><th>尺寸</th><th>实体</th><th>大小</th><th>导出时间</th><th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="b in buildings" :key="b.file">
                <td class="mono">{{ b.file }}</td>
                <td>{{ b.name || '—' }}</td>
                <td>{{ b.author || '—' }}</td>
                <td>{{ b.width }}×{{ b.height }}</td>
                <td>{{ b.entities }}</td>
                <td>{{ fmtBytes(b.sizeBytes) }}</td>
                <td>{{ fmtDate(b.createdAt) }}</td>
                <td class="ops">
                  <button class="op-btn primary" @click="openImport(b.file, 'local')">⬇ 导入</button>
                  <button class="op-btn" :disabled="sending === b.file" @click="doSendToBackend(b.file)">{{ sending === b.file ? '发送中…' : '☁️ 发后端' }}</button>
                  <button class="op-btn" @click="openDetail(b.file)">详情</button>
                  <button class="op-btn danger" :disabled="deleting === b.file" @click="doDeleteLocal(b.file)">删除</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-if="buildingsTotal > 0" class="pagination">
          <button @click="buildingsPrev" :disabled="buildingsPage <= 1">← 上一页</button>
          <span class="page-info">第 {{ buildingsPage }} / {{ buildingsTotalPages }} 页（共 {{ buildingsTotal }} 个文件）</span>
          <button @click="buildingsNext" :disabled="buildingsPage >= buildingsTotalPages">下一页 →</button>
        </div>
      </div>

      <!-- 后端 -->
      <div v-else>
        <div v-if="backendError" class="error-message">{{ backendError }}</div>
        <div v-if="backendLoading"><Loading text="加载中..." /></div>
        <div v-else-if="backendFiles.length === 0" class="empty-state">后端暂无建筑存档（房屋「导出→后端」或本地文件「发后端」）</div>
        <div v-else class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th>文件名</th><th>建筑名</th><th>作者</th><th>尺寸</th><th>实体</th><th>大小</th><th>导出时间</th><th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="f in backendFiles" :key="f.file">
                <td class="mono">{{ f.file }}</td>
                <td>{{ f.name || '—' }}</td>
                <td>{{ f.author || '—' }}</td>
                <td>{{ f.width }}×{{ f.height }}</td>
                <td>{{ f.entities }}</td>
                <td>{{ fmtBytes(f.sizeBytes) }}</td>
                <td>{{ fmtDate(f.createdAt) }}</td>
                <td class="ops">
                  <button class="op-btn primary" @click="openImport(f.file, 'backend')">⬇ 导入</button>
                  <button class="op-btn" @click="doDownloadBackend(f.file)">⬇ 下载</button>
                  <button class="op-btn" @click="openBackendDetail(f)">详情</button>
                  <button class="op-btn danger" :disabled="deleting === f.file" @click="doDeleteBackend(f.file)">删除</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- ═══ 导入向导 ═══ -->
    <Teleport to="body">
      <div v-if="showImport" class="modal-overlay" @click.self="closeImport">
        <div class="modal">
          <div class="modal-header">
            <h3>导入建筑到世界</h3>
            <button class="modal-close" @click="closeImport">✕</button>
          </div>
          <div class="modal-body">
            <div class="step-title">① 文件</div>
            <div class="file-chip">{{ importFile }} <span class="src-tag">{{ importSource === 'backend' ? '后端' : '插件本地' }}</span></div>

            <div class="step-title">② 位置来源</div>
            <div class="anchor-tabs">
              <button class="anchor-tab" :class="{ active: importAnchor === 'player' }" @click="importAnchor = 'player'">在线玩家</button>
              <button class="anchor-tab" :class="{ active: importAnchor === 'coords' }" @click="importAnchor = 'coords'">指定坐标</button>
              <button class="anchor-tab" :class="{ active: importAnchor === 'house' }" @click="importAnchor = 'house'">现有领地</button>
            </div>

            <div v-if="importAnchor === 'player'" class="anchor-fields">
              <select v-model="importPlayer" class="input">
                <option value="" disabled>选择在线玩家</option>
                <option v-for="p in onlinePlayers" :key="p.name" :value="p.name">{{ p.name }}（{{ p.tileX }}, {{ p.tileY }}）</option>
              </select>
              <button class="op-btn" @click="refreshPlayers">刷新</button>
            </div>
            <div v-else-if="importAnchor === 'coords'" class="anchor-fields">
              <input v-model="importCoordsX" class="input coords" type="number" placeholder="X" />
              <input v-model="importCoordsY" class="input coords" type="number" placeholder="Y" />
              <span class="text-muted">（方块坐标）</span>
            </div>
            <div v-else class="anchor-fields">
              <select v-model="importHouse" class="input">
                <option value="" disabled>选择领地（房屋）</option>
                <option v-for="h in houseOptions" :key="h.name" :value="h.name">{{ h.name }}（{{ h.area.width }}×{{ h.area.height }}）</option>
              </select>
            </div>

            <div class="step-title">③ 对齐点（建筑哪一点贴到目标点）</div>
            <div class="align-grid">
              <button v-for="a in ALIGNS" :key="a.key" class="align-btn" :class="{ active: importAlign === a.key }" @click="importAlign = a.key">
                {{ a.label }}
              </button>
            </div>
            <div class="align-hint text-muted">
              <template v-if="importAnchor === 'player'">玩家脚部格作为目标点</template>
              <template v-else-if="importAnchor === 'coords'">指定坐标 (X, Y) 作为目标点</template>
              <template v-else>领地范围校验：建筑必须完整落在所选领地内</template>
            </div>

            <div v-if="importResult" class="import-result" :class="importResult.ok ? 'ok' : 'err'">{{ importResult.msg }}</div>

            <div class="modal-footer">
              <button class="op-btn" @click="closeImport">取消</button>
              <button class="op-btn primary" :disabled="importBusy" @click="doImport">{{ importBusy ? '导入中…' : '确认导入' }}</button>
            </div>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- ═══ 详情弹窗（插件本地） ═══ -->
    <Teleport to="body">
      <div v-if="showDetail" class="modal-overlay" @click.self="closeDetail">
        <div class="modal">
          <div class="modal-header">
            <h3>建筑详情</h3>
            <button class="modal-close" @click="closeDetail">✕</button>
          </div>
          <div class="modal-body">
            <div v-if="detailLoading"><Loading text="加载中..." /></div>
            <div v-else-if="detail && detail.error" class="error-message">{{ detail.error }}</div>
            <div v-else-if="detail" class="detail-content">
              <div class="detail-title">{{ detail.meta?.name || detail.file }}</div>
              <div class="meta-line">作者：{{ detail.meta?.author || '—' }}　导出：{{ fmtDate(detail.meta?.createdAt) }}</div>
              <div v-if="detail.meta?.source" class="meta-line text-muted">来源世界：{{ detail.meta.source.world || '—' }}　版本：{{ detail.meta.source.gameVersion || '—' }}</div>
              <div class="detail-grid">
                <div class="kv"><span>尺寸</span><b>{{ detail.size?.width }}×{{ detail.size?.height }}</b></div>
                <div class="kv"><span>文件大小</span><b>{{ fmtBytes(detail.sizeBytes) }}</b></div>
                <div class="kv"><span>编码</span><b>{{ detail.tile?.encoding }}</b></div>
                <div class="kv"><span>压缩</span><b>{{ detail.tile?.compression }}</b></div>
                <div class="kv"><span>格数</span><b>{{ detail.tile?.expectedCount }}</b></div>
                <div class="kv"><span>实体总数</span><b>{{ (detail.entities || []).length }}</b></div>
              </div>
              <div class="entity-title">实体构成</div>
              <div class="chips">
                <span v-for="(count, kind) in detail.entitiesSummary" :key="kind" class="entity-chip">{{ kind }} × {{ count }}</span>
                <span v-if="!detail.entitiesSummary || Object.keys(detail.entitiesSummary).length === 0" class="text-muted">无实体</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- ═══ 详情弹窗（后端） ═══ -->
    <Teleport to="body">
      <div v-if="showBackendDetail" class="modal-overlay" @click.self="closeBackendDetail">
        <div class="modal">
          <div class="modal-header">
            <h3>建筑详情（后端）</h3>
            <button class="modal-close" @click="closeBackendDetail">✕</button>
          </div>
          <div class="modal-body" v-if="backendDetail">
            <div class="detail-title">{{ backendDetail.name || backendDetail.file }}</div>
            <div class="meta-line">作者：{{ backendDetail.author || '—' }}　导出：{{ fmtDate(backendDetail.createdAt) }}</div>
            <div class="detail-grid">
              <div class="kv"><span>尺寸</span><b>{{ backendDetail.width }}×{{ backendDetail.height }}</b></div>
              <div class="kv"><span>实体</span><b>{{ backendDetail.entities }}</b></div>
              <div class="kv"><span>文件大小</span><b>{{ fmtBytes(backendDetail.sizeBytes) }}</b></div>
              <div class="kv"><span>文件名</span><b class="mono">{{ backendDetail.file }}</b></div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- ═══ Toast ═══ -->
    <Teleport to="body">
      <transition name="toast">
        <div v-if="toast" class="toast" :class="toast.type">{{ toast.msg }}</div>
      </transition>
    </Teleport>
  </div>
</template>

<style scoped>
.house-mgmt { padding: 20px; width: 100%; }
.page-header { margin-bottom: 16px; display: flex; align-items: baseline; gap: 10px; }
.page-header h2 { margin: 0; color: var(--text-primary); font-size: 1.5rem; }
.sub { color: var(--text-muted); font-size: 0.85rem; }

.main-tabs { display: flex; gap: 8px; margin-bottom: 16px; }
.main-tab {
  padding: 9px 18px; border-radius: 10px; border: 1px solid var(--border-light);
  background: var(--bg-card); color: var(--text-secondary); cursor: pointer; font-size: 0.9rem; font-weight: 600;
}
.main-tab.active { background: linear-gradient(135deg, var(--accent-primary), #4f46e5); color: white; border-color: transparent; }

.sub-tabs { display: flex; gap: 8px; margin-bottom: 16px; }
.sub-tab {
  padding: 7px 14px; border-radius: 8px; border: 1px solid var(--border-light);
  background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; font-size: 0.82rem;
}
.sub-tab.active { background: rgba(99, 102, 241, 0.15); color: var(--accent-primary); border-color: rgba(99,102,241,.4); }

.section {
  background: var(--bg-card); border-radius: var(--radius-xl); padding: 20px;
  box-shadow: var(--shadow-md); border: 1px solid var(--border-light);
}
.error-message { padding: 12px 16px; background: rgba(239,68,68,.1); color: var(--accent-error); border-radius: var(--radius-md); margin-bottom: 16px; border: 1px solid rgba(239,68,68,.3); }
.empty-state { text-align: center; padding: 40px; color: var(--text-muted); }

/* 房屋卡片 */
.house-list { display: flex; flex-direction: column; gap: 12px; }
/* 注意：不能在此处 overflow: hidden，否则「导出」下拉菜单向下展开会被裁剪遮挡 */
.house-card { border: 1px solid var(--border-light); border-radius: var(--radius-lg); background: var(--bg-tertiary); transition: border-color 0.2s; }
.house-card.expanded { border-color: var(--accent-primary); }
.house-head { display: flex; align-items: center; gap: 12px; padding: 14px 16px; cursor: pointer; border-radius: var(--radius-lg) var(--radius-lg) 0 0; }
.house-head:hover { background: var(--bg-hover); }
.house-title { display: flex; flex-direction: column; gap: 2px; flex: 1; }
.house-name { color: var(--text-primary); font-weight: 700; font-size: 1.05rem; }
.house-author { color: var(--text-muted); font-size: 0.8rem; }
.house-area { color: var(--accent-secondary); font-weight: 600; font-size: 0.9rem; }
.expand-arrow { color: var(--text-muted); font-size: 0.7rem; transition: transform 0.2s; }
.expand-arrow.rotated { transform: rotate(180deg); }

.export-zone { position: relative; }
.export-btn {
  padding: 6px 12px; border-radius: 8px; border: 1px solid rgba(99,102,241,.35);
  background: rgba(99,102,241,.1); color: var(--accent-primary); cursor: pointer; font-size: 0.8rem; font-weight: 600;
}
.export-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.export-menu {
  position: absolute; right: 0; top: calc(100% + 6px); z-index: 9999;
  background: var(--bg-primary); border: 1px solid var(--border-light); border-radius: 10px;
  box-shadow: var(--shadow-lg); padding: 6px; min-width: 150px; display: flex; flex-direction: column; gap: 2px;
}
.export-opt { padding: 9px 12px; border: none; background: transparent; border-radius: 8px; color: var(--text-primary); cursor: pointer; font-size: 0.85rem; text-align: left; }
.export-opt:hover { background: var(--bg-hover); }

.house-detail { padding: 0 16px 16px; border-top: 1px dashed var(--border-light); }
.detail-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; padding: 14px 0; }
.detail-item { background: var(--bg-card); border: 1px solid var(--border-light); border-radius: var(--radius-md); padding: 10px 12px; }
.detail-label { color: var(--text-secondary); font-size: 0.8rem; margin-bottom: 4px; }
.detail-value { color: var(--text-primary); font-size: 0.9rem; word-break: break-all; }
.detail-row { display: flex; align-items: flex-start; gap: 12px; padding: 6px 0; }
.detail-row .detail-label { width: 64px; flex-shrink: 0; margin: 0; padding-top: 5px; }
.chips { display: flex; flex-wrap: wrap; gap: 6px; }
.perm-chip { padding: 4px 10px; border-radius: var(--radius-md); font-size: 0.8rem; font-weight: 500; }
.perm-chip.on { background: rgba(34,197,94,.12); color: #22c55e; }
.perm-chip.mid { background: rgba(99,102,241,.12); color: var(--accent-primary); }
.perm-chip.off { background: rgba(245,158,11,.12); color: #f59e0b; }
.text-muted { color: var(--text-muted); font-size: 0.85rem; }

/* 表格 */
.table-wrap { overflow-x: auto; }
.data-table { width: 100%; border-collapse: collapse; font-size: 0.88rem; }
.data-table th { text-align: left; padding: 10px 12px; color: var(--text-secondary); border-bottom: 1px solid var(--border-light); font-weight: 600; white-space: nowrap; }
.data-table td { padding: 10px 12px; border-bottom: 1px solid var(--border-light); color: var(--text-primary); }
.data-table tr:hover td { background: var(--bg-hover); }
.mono { font-family: ui-monospace, Consolas, monospace; font-size: 0.8rem; color: var(--text-secondary); }
.ops { display: flex; gap: 6px; flex-wrap: wrap; }
.op-btn {
  padding: 5px 10px; border-radius: var(--radius-sm); border: 1px solid var(--border-light);
  background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; font-size: 0.78rem;
}
.op-btn:hover { border-color: var(--accent-primary); color: var(--accent-primary); }
.op-btn.primary { background: linear-gradient(135deg, var(--accent-primary), #4f46e5); color: white; border: none; }
.op-btn.primary:disabled { opacity: 0.6; cursor: not-allowed; }
.op-btn.danger:hover { border-color: rgba(239,68,68,.5); color: var(--accent-error); }
.op-btn:disabled { opacity: 0.6; cursor: not-allowed; }

.pagination { display: flex; align-items: center; justify-content: center; gap: 16px; margin-top: 20px; }
.pagination button { padding: 8px 16px; background: linear-gradient(135deg, var(--accent-primary), #4f46e5); color: white; border: none; border-radius: var(--radius-md); cursor: pointer; font-size: 0.85rem; }
.pagination button:disabled { opacity: 0.5; cursor: not-allowed; }
.page-info { color: var(--text-secondary); font-size: 0.85rem; }

/* 弹窗 */
.modal-overlay { position: fixed; inset: 0; z-index: 10000; background: rgba(0,0,0,.5); display: flex; align-items: center; justify-content: center; animation: fadeIn .2s ease; }
.modal { width: 560px; max-width: 92vw; max-height: 84vh; display: flex; flex-direction: column; background: var(--bg-primary); border-radius: var(--radius-xl); box-shadow: 0 20px 60px rgba(0,0,0,.4); border: 1px solid var(--border-light); animation: slideUp .25s ease; }
.modal-header { display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid var(--border-light); }
.modal-header h3 { margin: 0; color: var(--text-primary); font-size: 1.1rem; }
.modal-close { width: 32px; height: 32px; border-radius: 10px; border: 1px solid var(--border-light); background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; }
.modal-body { padding: 20px; overflow-y: auto; }
.modal-footer { display: flex; justify-content: flex-end; gap: 10px; margin-top: 18px; }

.step-title { color: var(--text-secondary); font-weight: 700; font-size: 0.85rem; margin: 14px 0 8px; }
.file-chip { display: inline-flex; align-items: center; gap: 8px; padding: 6px 12px; background: var(--bg-tertiary); border: 1px solid var(--border-light); border-radius: 8px; color: var(--text-primary); font-size: 0.85rem; }
.src-tag { padding: 2px 8px; background: rgba(99,102,241,.15); color: var(--accent-primary); border-radius: 6px; font-size: 0.72rem; }
.anchor-tabs { display: flex; gap: 8px; margin-bottom: 10px; }
.anchor-tab { padding: 7px 14px; border-radius: 8px; border: 1px solid var(--border-light); background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; font-size: 0.82rem; }
.anchor-tab.active { background: rgba(99,102,241,.15); color: var(--accent-primary); border-color: rgba(99,102,241,.4); }
.anchor-fields { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }
.input { padding: 8px 12px; border-radius: 8px; border: 1px solid var(--border-light); background: var(--bg-tertiary); color: var(--text-primary); font-size: 0.85rem; flex: 1; }
.input.coords { flex: 0 0 90px; }

.align-grid { display: flex; gap: 8px; flex-wrap: wrap; }
.align-btn { padding: 8px 16px; border-radius: 8px; border: 1px solid var(--border-light); background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; font-size: 0.85rem; }
.align-btn.active { background: rgba(99,102,241,.18); color: var(--accent-primary); border-color: var(--accent-primary); }
.align-hint { margin-top: 8px; }

.import-result { margin-top: 12px; padding: 10px 14px; border-radius: 8px; font-size: 0.85rem; }
.import-result.ok { background: rgba(34,197,94,.1); color: #22c55e; border: 1px solid rgba(34,197,94,.3); }
.import-result.err { background: rgba(239,68,68,.1); color: var(--accent-error); border: 1px solid rgba(239,68,68,.3); }

.detail-title { color: var(--text-primary); font-size: 1.15rem; font-weight: 700; margin-bottom: 6px; }
.meta-line { color: var(--text-secondary); font-size: 0.85rem; margin-bottom: 4px; }
.detail-grid .kv { background: var(--bg-tertiary); border: 1px solid var(--border-light); border-radius: var(--radius-md); padding: 8px 10px; }
.kv span { display: block; color: var(--text-muted); font-size: 0.75rem; margin-bottom: 2px; }
.kv b { color: var(--text-primary); font-size: 0.88rem; word-break: break-all; }
.entity-title { color: var(--text-secondary); font-weight: 600; font-size: 0.85rem; margin: 14px 0 8px; }
.entity-chip { padding: 4px 10px; border-radius: var(--radius-md); font-size: 0.8rem; background: rgba(99,102,241,.12); color: var(--accent-primary); }

/* Toast */
.toast { position: fixed; top: 20px; left: 50%; transform: translateX(-50%); z-index: 20000; padding: 12px 22px; border-radius: 10px; font-size: 0.88rem; box-shadow: var(--shadow-lg); color: #fff; }
.toast.ok { background: linear-gradient(135deg, #22c55e, #16a34a); }
.toast.error { background: linear-gradient(135deg, #ef4444, #dc2626); }
.toast-enter-active, .toast-leave-active { transition: all .3s ease; }
.toast-enter-from, .toast-leave-to { opacity: 0; transform: translate(-50%, -10px); }

@keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
@keyframes slideUp { from { transform: translateY(20px); opacity: 0; } to { transform: translateY(0); opacity: 1; } }
</style>
