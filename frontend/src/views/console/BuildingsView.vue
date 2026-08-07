<script setup>
import { ref, onMounted, computed } from 'vue'
import { listBuildings, getBuildingInfo } from '../../api/houseApi.js'

const loading = ref(false)
const error = ref('')
const buildings = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = 20

// 详情弹窗
const showDetail = ref(false)
const detailLoading = ref(false)
const detail = ref(null)

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))

const fetchBuildings = async () => {
  loading.value = true
  error.value = ''
  try {
    const result = await listBuildings(page.value, pageSize)
    if (result.error) {
      error.value = result.error
    } else {
      buildings.value = result.items || []
      total.value = result.total || 0
    }
  } catch (err) {
    error.value = err.message
  }
  loading.value = false
}

const openDetail = async (file) => {
  detail.value = null
  showDetail.value = true
  detailLoading.value = true
  try {
    const result = await getBuildingInfo(file)
    detail.value = result.error ? { error: result.error } : result
  } catch (err) {
    detail.value = { error: err.message }
  }
  detailLoading.value = false
}

const closeDetail = () => { showDetail.value = false; detail.value = null }

const prevPage = () => { if (page.value > 1) { page.value--; fetchBuildings() } }
const nextPage = () => { if (page.value < totalPages.value) { page.value++; fetchBuildings() } }

const fmtBytes = (n) => {
  if (!n && n !== 0) return '-'
  if (n < 1024) return n + ' B'
  if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB'
  return (n / 1024 / 1024).toFixed(2) + ' MB'
}

const fmtDate = (s) => (s || '-').replace('T', ' ').slice(0, 19)

onMounted(fetchBuildings)
</script>

<template>
  <div class="buildings-view">
    <div class="page-header">
      <h2>建筑存档</h2>
      <span class="sub">本地 .tsb 导出文件（{TShock.SavePath}/TSWeb/Buildings/）</span>
    </div>

    <div class="section">
      <div v-if="error" class="error-message">{{ error }}</div>
      <div v-if="loading" class="loading">加载中...</div>

      <div v-else-if="buildings.length === 0" class="empty-state">
        暂无建筑存档（游戏中管理员使用 /h export [屋名] 导出）
      </div>

      <div v-else class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>文件名</th>
              <th>建筑名</th>
              <th>作者</th>
              <th>尺寸</th>
              <th>实体数</th>
              <th>大小</th>
              <th>导出时间</th>
              <th></th>
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
              <td><button class="detail-btn" @click="openDetail(b.file)">详情</button></td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- 分页 -->
      <div v-if="total > 0" class="pagination">
        <button @click="prevPage" :disabled="page <= 1">← 上一页</button>
        <span class="page-info">第 {{ page }} / {{ totalPages }} 页（共 {{ total }} 个文件）</span>
        <button @click="nextPage" :disabled="page >= totalPages">下一页 →</button>
      </div>
    </div>

    <!-- 详情弹窗 -->
    <Teleport to="body">
      <div v-if="showDetail" class="modal-overlay" @click.self="closeDetail">
        <div class="modal">
          <div class="modal-header">
            <h3>建筑详情</h3>
            <button class="modal-close" @click="closeDetail">✕</button>
          </div>
          <div class="modal-body">
            <div v-if="detailLoading" class="loading">加载中...</div>
            <div v-else-if="detail && detail.error" class="error-message">{{ detail.error }}</div>
            <div v-else-if="detail" class="detail-content">
              <div class="detail-title">{{ detail.meta?.name || detail.file }}</div>
              <div class="meta-line">作者：{{ detail.meta?.author || '—' }}　导出：{{ fmtDate(detail.meta?.createdAt) }}</div>
              <div v-if="detail.meta?.description" class="meta-line">{{ detail.meta.description }}</div>
              <div v-if="detail.meta?.source" class="meta-line text-muted">
                来源世界：{{ detail.meta.source.world || '—' }}　种子：{{ detail.meta.source.worldSeed ?? '—' }}　版本：{{ detail.meta.source.gameVersion || '—' }}
              </div>

              <div class="detail-grid">
                <div class="kv"><span>尺寸</span><b>{{ detail.size?.width }}×{{ detail.size?.height }}</b></div>
                <div class="kv"><span>文件大小</span><b>{{ fmtBytes(detail.sizeBytes) }}</b></div>
                <div class="kv"><span>编码</span><b>{{ detail.tile?.encoding }}</b></div>
                <div class="kv"><span>压缩</span><b>{{ detail.tile?.compression }}</b></div>
                <div class="kv"><span>格数</span><b>{{ detail.tile?.expectedCount }}</b></div>
                <div class="kv"><span>实体总数</span><b>{{ (detail.entities || []).length }}</b></div>
                <div class="kv"><span>最大方块 ID</span><b>{{ detail.compat?.maxTileId }}</b></div>
                <div class="kv"><span>最大墙体 ID</span><b>{{ detail.compat?.maxWallId }}</b></div>
                <div class="kv"><span>最大物品 ID</span><b>{{ detail.compat?.maxItemId }}</b></div>
                <div class="kv"><span>执行器/电线</span><b>{{ detail.compat?.requiresActuator ? '是' : '否' }}/{{ detail.compat?.requiresWire ? '是' : '否' }}</b></div>
              </div>

              <div class="checksum-line mono">SHA-256: {{ detail.tile?.checksum }}</div>

              <div class="entity-title">实体构成</div>
              <div class="chips">
                <span v-for="(count, kind) in detail.entitiesSummary" :key="kind" class="entity-chip">
                  {{ kind }} × {{ count }}
                </span>
                <span v-if="!detail.entitiesSummary || Object.keys(detail.entitiesSummary).length === 0" class="text-muted">无实体</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.buildings-view { padding: 20px; width: 100%; }
.page-header { margin-bottom: 16px; display: flex; align-items: baseline; gap: 10px; }
.page-header h2 { margin: 0; color: var(--text-primary); font-size: 1.5rem; }
.sub { color: var(--text-muted); font-size: 0.85rem; }

.section {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 20px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.error-message {
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.1);
  color: var(--accent-error);
  border-radius: var(--radius-md);
  margin-bottom: 16px;
  border: 1px solid rgba(239, 68, 68, 0.3);
}
.loading { text-align: center; padding: 40px; color: var(--text-muted); }
.empty-state { text-align: center; padding: 40px; color: var(--text-muted); }

.table-wrap { overflow-x: auto; }
.data-table { width: 100%; border-collapse: collapse; font-size: 0.88rem; }
.data-table th {
  text-align: left; padding: 10px 12px; color: var(--text-secondary);
  border-bottom: 1px solid var(--border-light); font-weight: 600; white-space: nowrap;
}
.data-table td { padding: 10px 12px; border-bottom: 1px solid var(--border-light); color: var(--text-primary); }
.data-table tr:hover td { background: var(--bg-hover); }
.mono { font-family: ui-monospace, Consolas, monospace; font-size: 0.8rem; color: var(--text-secondary); }

.detail-btn {
  padding: 5px 12px; background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white; border: none; border-radius: var(--radius-sm); cursor: pointer; font-size: 0.8rem;
}
.detail-btn:hover { transform: translateY(-1px); box-shadow: 0 2px 8px rgba(99, 102, 241, 0.3); }

.pagination {
  display: flex; align-items: center; justify-content: center; gap: 16px; margin-top: 20px;
}
.pagination button {
  padding: 8px 16px; background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white; border: none; border-radius: var(--radius-md); cursor: pointer; font-size: 0.85rem;
}
.pagination button:disabled { opacity: 0.5; cursor: not-allowed; }
.page-info { color: var(--text-secondary); font-size: 0.85rem; }

/* ── 弹窗 ── */
.modal-overlay {
  position: fixed; inset: 0; z-index: 10000;
  background: rgba(0, 0, 0, 0.5); display: flex; align-items: center; justify-content: center;
  animation: fadeIn 0.2s ease;
}
.modal {
  width: 640px; max-width: 92vw; max-height: 82vh; display: flex; flex-direction: column;
  background: var(--bg-primary); border-radius: var(--radius-xl);
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.4); border: 1px solid var(--border-light);
  animation: slideUp 0.25s ease;
}
.modal-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 16px 20px; border-bottom: 1px solid var(--border-light);
}
.modal-header h3 { margin: 0; color: var(--text-primary); font-size: 1.1rem; }
.modal-close {
  width: 32px; height: 32px; border-radius: 10px; border: 1px solid var(--border-light);
  background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer;
}
.modal-body { padding: 20px; overflow-y: auto; }

.detail-title { color: var(--text-primary); font-size: 1.15rem; font-weight: 700; margin-bottom: 6px; }
.meta-line { color: var(--text-secondary); font-size: 0.85rem; margin-bottom: 4px; }
.text-muted { color: var(--text-muted); }

.detail-grid {
  display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 10px; margin: 14px 0;
}
.kv {
  background: var(--bg-tertiary); border: 1px solid var(--border-light);
  border-radius: var(--radius-md); padding: 8px 10px;
}
.kv span { display: block; color: var(--text-muted); font-size: 0.75rem; margin-bottom: 2px; }
.kv b { color: var(--text-primary); font-size: 0.88rem; }

.checksum-line {
  color: var(--text-muted); font-size: 0.75rem; word-break: break-all;
  padding: 8px; background: var(--bg-tertiary); border-radius: var(--radius-md);
}

.entity-title { color: var(--text-secondary); font-weight: 600; font-size: 0.85rem; margin: 14px 0 8px; }
.chips { display: flex; flex-wrap: wrap; gap: 6px; }
.entity-chip {
  padding: 4px 10px; border-radius: var(--radius-md); font-size: 0.8rem;
  background: rgba(99, 102, 241, 0.12); color: var(--accent-primary);
}

@keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
@keyframes slideUp { from { transform: translateY(20px); opacity: 0; } to { transform: translateY(0); opacity: 1; } }
</style>
