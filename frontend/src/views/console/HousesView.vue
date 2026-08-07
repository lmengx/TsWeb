<script setup>
import { ref, onMounted, computed } from 'vue'
import { listHouses } from '../../api/houseApi.js'

const loading = ref(false)
const error = ref('')
const houses = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = 10
const expanded = ref({})   // 房屋展开状态

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))

const PERMS = [
  { key: 'entry', label: '进入' },
  { key: 'tp', label: '传送' },
  { key: 'place', label: '放置' },
  { key: 'break', label: '破坏' },
  { key: 'liquid', label: '液体' },
  { key: 'chest', label: '箱子' },
  { key: 'plant', label: '植物' },
  { key: 'spawn', label: '复活点' },
  { key: 'grave', label: '挖坟' },
  { key: 'switch', label: '开关' },
  { key: 'door', label: '门' },
  { key: 'fragile', label: '易碎品' }
]

const fetchHouses = async () => {
  loading.value = true
  error.value = ''
  try {
    const result = await listHouses(page.value, pageSize)
    if (result.error) {
      error.value = result.error
    } else {
      houses.value = result.items || []
      total.value = result.total || 0
    }
  } catch (err) {
    error.value = err.message
  }
  loading.value = false
}

const toggle = (name) => { expanded.value[name] = !expanded.value[name] }

const prevPage = () => { if (page.value > 1) { page.value--; fetchHouses() } }
const nextPage = () => { if (page.value < totalPages.value) { page.value++; fetchHouses() } }

const areaText = (h) => `(${h.area.x}, ${h.area.y}) → (${h.area.x + h.area.width - 1}, ${h.area.y + h.area.height - 1})`

const fmtBytes = (n) => {
  if (!n && n !== 0) return '-'
  if (n < 1024) return n + ' B'
  if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB'
  return (n / 1024 / 1024).toFixed(2) + ' MB'
}

onMounted(fetchHouses)
</script>

<template>
  <div class="houses-view">
    <div class="page-header">
      <h2>房屋管理</h2>
      <span class="sub">HouseRegion 圈地插件数据</span>
    </div>

    <div class="section">
      <div v-if="error" class="error-message">{{ error }}</div>
      <div v-if="loading" class="loading">加载中...</div>

      <div v-else-if="houses.length === 0" class="empty-state">
        暂无房屋数据（游戏中 /h c 圈地创建）
      </div>

      <div v-else class="house-list">
        <div v-for="h in houses" :key="h.name" class="house-card" :class="{ expanded: expanded[h.name] }">
          <!-- 头部 -->
          <div class="house-head" @click="toggle(h.name)">
            <div class="house-title">
              <span class="house-name">{{ h.name }}</span>
              <span class="house-author">房主：{{ h.authorName || h.author }}</span>
            </div>
            <span class="house-area">{{ h.area.width }}×{{ h.area.height }}</span>
            <span class="expand-arrow" :class="{ rotated: expanded[h.name] }">▼</span>
          </div>

          <!-- 详情 -->
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
                <div class="detail-value" :class="{ 'text-muted': !h.expel }">
                  {{ h.expel ? `(${h.expel.x}, ${h.expel.y})` : '未设置' }}
                </div>
              </div>
              <div class="detail-item">
                <div class="detail-label">违规驱离</div>
                <div class="detail-value">
                  <span class="perm-chip" :class="h.expelOnViolate === 1 ? 'on' : 'off'">
                    {{ h.expelOnViolate === 1 ? '✓ 开' : '✗ 关' }}
                  </span>
                </div>
              </div>
            </div>

            <!-- 授权 -->
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

            <!-- 权限 -->
            <div class="detail-row">
              <div class="detail-label">权限</div>
              <div class="chips">
                <span v-for="p in PERMS" :key="p.key" class="perm-chip" :class="h.permissions[p.key] === 1 ? 'on' : 'off'">
                  {{ p.label }} {{ h.permissions[p.key] === 1 ? '✓' : '✗' }}
                </span>
              </div>
            </div>

            <!-- 通知 -->
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

      <!-- 分页 -->
      <div v-if="total > 0" class="pagination">
        <button @click="prevPage" :disabled="page <= 1">← 上一页</button>
        <span class="page-info">第 {{ page }} / {{ totalPages }} 页（共 {{ total }} 个房屋）</span>
        <button @click="nextPage" :disabled="page >= totalPages">下一页 →</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.houses-view { padding: 20px; width: 100%; }
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

.house-list { display: flex; flex-direction: column; gap: 12px; }

.house-card {
  border: 1px solid var(--border-light);
  border-radius: var(--radius-lg);
  background: var(--bg-tertiary);
  overflow: hidden;
  transition: border-color 0.2s;
}
.house-card.expanded { border-color: var(--accent-primary); }

.house-head {
  display: flex; align-items: center; gap: 12px;
  padding: 14px 16px; cursor: pointer;
}
.house-head:hover { background: var(--bg-hover); }

.house-title { display: flex; flex-direction: column; gap: 2px; flex: 1; }
.house-name { color: var(--text-primary); font-weight: 700; font-size: 1.05rem; }
.house-author { color: var(--text-muted); font-size: 0.8rem; }
.house-area { color: var(--accent-secondary); font-weight: 600; font-size: 0.9rem; }
.expand-arrow { color: var(--text-muted); font-size: 0.7rem; transition: transform 0.2s; }
.expand-arrow.rotated { transform: rotate(180deg); }

.house-detail { padding: 0 16px 16px; border-top: 1px dashed var(--border-light); }

.detail-grid {
  display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 12px; padding: 14px 0;
}
.detail-item { background: var(--bg-card); border: 1px solid var(--border-light); border-radius: var(--radius-md); padding: 10px 12px; }
.detail-label { color: var(--text-secondary); font-size: 0.8rem; margin-bottom: 4px; }
.detail-value { color: var(--text-primary); font-size: 0.9rem; word-break: break-all; }

.detail-row { display: flex; align-items: flex-start; gap: 12px; padding: 6px 0; }
.detail-row .detail-label { width: 64px; flex-shrink: 0; margin: 0; padding-top: 5px; }
.chips { display: flex; flex-wrap: wrap; gap: 6px; }

.perm-chip {
  padding: 4px 10px; border-radius: var(--radius-md);
  font-size: 0.8rem; font-weight: 500;
}
.perm-chip.on { background: rgba(34, 197, 94, 0.12); color: #22c55e; }
.perm-chip.mid { background: rgba(99, 102, 241, 0.12); color: var(--accent-primary); }
.perm-chip.off { background: rgba(245, 158, 11, 0.12); color: #f59e0b; }
.text-muted { color: var(--text-muted); font-size: 0.85rem; }

.pagination {
  display: flex; align-items: center; justify-content: center; gap: 16px;
  margin-top: 20px;
}
.pagination button {
  padding: 8px 16px; background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white; border: none; border-radius: var(--radius-md); cursor: pointer; font-size: 0.85rem;
}
.pagination button:disabled { opacity: 0.5; cursor: not-allowed; }
.page-info { color: var(--text-secondary); font-size: 0.85rem; }
</style>
