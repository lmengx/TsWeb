<script setup>
import { ref, computed, onMounted } from 'vue'
import { get, post, patch, del } from '../../utils/api.js'

// ═══════════════════════════════════════════════════════════
// 投票管理（蓝色主题）：发起投票 / 编辑 / 归档 / 删除 / 明细
//  - 数据后端权威 votes.json；匿名仅对玩家生效，管理端明细全量可见
// ═══════════════════════════════════════════════════════════

const rounds = ref([])
const loading = ref(true)
const error = ref('')
const busy = ref(false)

// ── 新建轮次表单 ──
const form = ref({
  title: '',
  description: '',
  options: ['', ''],
  maxVotesPerUser: 1,
  baseWeight: 1,
  allowProposals: false,
  maxProposalsPerUser: 1,
  weightRules: [{ field: 'playtime_hours', op: '>', threshold: 50, weight: 0.5 }],
  endAt: ''
})
const showForm = ref(false)
const formError = ref('')
const formOk = ref('')

// ── 编辑轮次 ──
const showEdit = ref(false)
const editId = ref(null)
const editForm = ref({ title: '', description: '', endAt: '' })
const editError = ref('')

// ── 明细查看 ──
const showDetail = ref(false)
const detailLoading = ref(false)
const detailError = ref('')
const detail = ref(null)          // { ...round, options: [ { ...o, votes, score, voters: [] } ] }
const detailTab = ref('vote')     // 'vote' 投票明细 | 'proposal' 提案明细

const opOptions = [
  { value: '>', label: '>' },
  { value: '>=', label: '≥' }
]
const fieldOptions = [
  { value: 'playtime_hours', label: '游玩时长（小时）' }
]

const statusText = (s) => ({ open: '进行中', closed: '已结束' }[s] || s)
const fmtTime = (t) => (t ? new Date(t).toLocaleString('zh-CN', { hour12: false }) : '—')

/** ISO → datetime-local 输入框值（本地时区） */
const toLocalInput = (iso) => {
  if (!iso) return ''
  const d = new Date(iso)
  const pad = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

// ── 双分类：活跃中（未归档） / 已归档 ──
const activeTab = ref('active')
const activeRounds = computed(() => rounds.value.filter(r => !r.archived))
const archivedRounds = computed(() => rounds.value.filter(r => r.archived))

/** 选项加权分占比（含 0 票兜底） */
const pct = (option, options) => {
  const total = options.reduce((s, o) => s + (Number(o.score) || 0), 0)
  if (total <= 0) return '0%'
  return ((Number(option.score || 0) / total) * 100).toFixed(1) + '%'
}

const loadRounds = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/vote/admin/rounds')
    const data = await res.json()
    rounds.value = data.rounds || []
  } catch (err) {
    error.value = '加载失败: ' + err.message
  } finally {
    loading.value = false
  }
}

// ── 表单操作 ──
const addOption = () => { form.value.options.push('') }
const removeOption = (i) => {
  if (form.value.options.length <= 1) return
  form.value.options.splice(i, 1)
}
const addRule = () => form.value.weightRules.push({ field: 'playtime_hours', op: '>', threshold: 50, weight: 0.5 })
const removeRule = (i) => form.value.weightRules.splice(i, 1)

const resetForm = () => {
  form.value = {
    title: '',
    description: '',
    options: ['', ''],
    maxVotesPerUser: 1,
    baseWeight: 1,
    allowProposals: false,
    maxProposalsPerUser: 1,
    weightRules: [{ field: 'playtime_hours', op: '>', threshold: 50, weight: 0.5 }],
    endAt: ''
  }
  showForm.value = false
  formError.value = ''
  formOk.value = ''
}

const createRound = async () => {
  formError.value = ''
  formOk.value = ''
  busy.value = true
  try {
    const payload = {
      title: form.value.title.trim(),
      description: form.value.description.trim(),
      options: form.value.options.map(o => o.trim()).filter(Boolean),
      maxVotesPerUser: parseInt(form.value.maxVotesPerUser) || 1,
      baseWeight: Number(form.value.baseWeight) || 1,
      allowProposals: form.value.allowProposals,
      maxProposalsPerUser: parseInt(form.value.maxProposalsPerUser) || 1,
      weightRules: form.value.weightRules
        .filter(r => r && (Number(r.threshold) !== undefined) && (Number(r.weight) !== 0))
        .map(r => ({ field: r.field, op: r.op, threshold: Number(r.threshold), weight: Number(r.weight) })),
      endAt: form.value.endAt || null
    }
    const res = await post('/api/vote/rounds', payload)
    const data = await res.json()
    if (!data.success) {
      formError.value = data.error || '创建失败'
      return
    }
    formOk.value = '轮次已创建'
    resetForm()
    loadRounds()
  } catch (err) {
    formError.value = '创建失败: ' + err.message
  } finally {
    busy.value = false
  }
}

// ── 编辑轮次（标题 / 说明 / 截止时间）──
const openEdit = (r) => {
  editId.value = r.id
  editForm.value = {
    title: r.title || '',
    description: r.description || '',
    endAt: toLocalInput(r.endAt)
  }
  editError.value = ''
  showEdit.value = true
}

const saveEdit = async () => {
  editError.value = ''
  busy.value = true
  try {
    const payload = {
      title: editForm.value.title.trim(),
      description: editForm.value.description.trim(),
      endAt: editForm.value.endAt || null
    }
    const res = await patch(`/api/vote/rounds/${editId.value}`, payload)
    const data = await res.json()
    if (!data.success) { editError.value = data.error || '保存失败'; return }
    showEdit.value = false
    loadRounds()
  } catch (err) {
    editError.value = '保存失败: ' + err.message
  } finally {
    busy.value = false
  }
}

// ── 明细查看（投票明细 + 提案明细，管理员全量）──
const openDetail = async (r) => {
  detail.value = null
  detailError.value = ''
  detailTab.value = 'vote'
  showDetail.value = true
  detailLoading.value = true
  try {
    const res = await get(`/api/vote/admin/rounds/${r.id}/detail`)
    const data = await res.json()
    if (!data.success) { detailError.value = data.error || '加载失败'; return }
    detail.value = data.round || null
  } catch (err) {
    detailError.value = '加载失败: ' + err.message
  } finally {
    detailLoading.value = false
  }
}

const proposals = computed(() => (detail.value?.options || []).filter(o => o.type === 'custom'))

// ── 轮次操作 ──
const closeRound = async (r) => {
  if (!confirm(`确认结束「${r.title}」？结束后玩家无法再投票/提案`)) return
  busy.value = true
  try {
    const res = await post(`/api/vote/rounds/${r.id}/close`)
    const data = await res.json()
    if (!data.success) { error.value = data.error || '操作失败'; return }
    loadRounds()
  } catch (err) {
    error.value = '操作失败: ' + err.message
  } finally {
    busy.value = false
  }
}

const removeRound = async (r) => {
  if (!confirm(`确认删除「${r.title}」？其投票记录将一并删除，不可恢复！`)) return
  busy.value = true
  try {
    const res = await del(`/api/vote/rounds/${r.id}`)
    const data = await res.json()
    if (!data.success) { error.value = data.error || '操作失败'; return }
    loadRounds()
  } catch (err) {
    error.value = '操作失败: ' + err.message
  } finally {
    busy.value = false
  }
}

/** 归档：从玩家页隐藏（可取消） */
const archiveRound = async (r) => {
  if (!confirm(`确认归档「${r.title}」？归档后玩家将无法在投票页看到它，可随时取消归档`)) return
  busy.value = true
  try {
    const res = await post(`/api/vote/rounds/${r.id}/archive`)
    const data = await res.json()
    if (!data.success) { error.value = data.error || '操作失败'; return }
    loadRounds()
  } catch (err) {
    error.value = '操作失败: ' + err.message
  } finally {
    busy.value = false
  }
}

/** 取消归档：重新对玩家页可见 */
const unarchiveRound = async (r) => {
  if (!confirm(`确认取消归档「${r.title}」？玩家将重新在投票页看到它`)) return
  busy.value = true
  try {
    const res = await post(`/api/vote/rounds/${r.id}/unarchive`)
    const data = await res.json()
    if (!data.success) { error.value = data.error || '操作失败'; return }
    loadRounds()
  } catch (err) {
    error.value = '操作失败: ' + err.message
  } finally {
    busy.value = false
  }
}

onMounted(loadRounds)
</script>

<template>
  <div class="vote-settings">
    <div class="vs-header">
      <div>
        <h3>投票管理</h3>
        <p class="vs-sub">发起周期投票：自定义说明、选项、每用户票数、权重规则、自主提案；管理端可查看全部投票/提案明细</p>
      </div>
      <button class="btn-primary" @click="showForm = !showForm">
        {{ showForm ? '收起表单' : '发起新投票' }}
      </button>
    </div>

    <div v-if="error" class="msg error">{{ error }}</div>

    <!-- ═══ 新建轮次（分区表单） ═══ -->
    <div v-if="showForm" class="vs-card form-card">
      <!-- ① 基本信息 -->
      <div class="form-section">
        <div class="sec-title">基本信息</div>
        <div class="form-row">
          <label class="form-label">投票标题 <em class="req">*</em></label>
          <input v-model="form.title" class="form-input" placeholder="如：下周期地图类型投票" maxlength="60" />
        </div>
        <div class="form-row">
          <label class="form-label">说明内容 <span class="hint-tip">（显示在玩家页标题下方，可留空，支持换行）</span></label>
          <textarea v-model="form.description" class="form-input textarea" rows="3" maxlength="300" placeholder="介绍本次投票背景、规则、注意事项等，玩家页会展示"></textarea>
        </div>
        <div class="form-row">
          <label class="form-label">截止时间 <span class="hint-tip">（留空 = 长期有效，仅可手动结束）</span></label>
          <input v-model="form.endAt" type="datetime-local" class="form-input" />
        </div>
      </div>

      <!-- ② 初始选项 -->
      <div class="form-section">
        <div class="sec-title">初始选项 <span class="hint-tip">（至少 1 个）</span></div>
        <div v-for="(o, i) in form.options" :key="i" class="option-line">
          <span class="opt-idx">{{ i + 1 }}</span>
          <input v-model="form.options[i]" class="form-input" :placeholder="`选项 ${i + 1}`" maxlength="50" />
          <button class="btn-mini danger" @click="removeOption(i)" :disabled="form.options.length <= 1" title="删除该选项">✕</button>
        </div>
        <button class="btn-mini" @click="addOption">添加选项</button>
      </div>

      <!-- ③ 投票规则 -->
      <div class="form-section">
        <div class="sec-title">投票规则</div>
        <div class="form-grid">
          <div class="form-row">
            <label class="form-label">每用户最多可投选项数</label>
            <input v-model.number="form.maxVotesPerUser" type="number" min="1" class="form-input" />
          </div>
          <div class="form-row">
            <label class="form-label">初始权重（每次投票分值，可小数）</label>
            <input v-model.number="form.baseWeight" type="number" min="0" step="0.1" class="form-input" />
          </div>
        </div>
      </div>

      <!-- ④ 加权规则 -->
      <div class="form-section">
        <div class="sec-title">条件加权规则 <span class="hint-tip">（满足多条可累加）</span></div>
        <div v-for="(r, i) in form.weightRules" :key="i" class="rule-line">
          <span class="rule-text">如果</span>
          <select v-model="r.field" class="form-select">
            <option v-for="f in fieldOptions" :key="f.value" :value="f.value">{{ f.label }}</option>
          </select>
          <select v-model="r.op" class="form-select narrow">
            <option v-for="o in opOptions" :key="o.value" :value="o.value">{{ o.label }}</option>
          </select>
          <input v-model.number="r.threshold" type="number" min="0" class="form-input narrow" placeholder="阈值" />
          <span class="rule-text">加权</span>
          <input v-model.number="r.weight" type="number" step="0.1" class="form-input narrow" placeholder="+" />
          <button class="btn-mini danger" @click="removeRule(i)">✕</button>
        </div>
        <button class="btn-mini" @click="addRule">添加加权规则</button>
      </div>

      <!-- ⑤ 提案设置 -->
      <div class="form-section">
        <div class="sec-title">玩家自主提案</div>
        <div class="form-grid">
          <div class="form-row">
            <label class="form-label switch-label">
              <input v-model="form.allowProposals" type="checkbox" class="checkbox" />
              允许玩家提交自定义选项
            </label>
          </div>
          <div class="form-row" v-if="form.allowProposals">
            <label class="form-label">每用户最多提案数</label>
            <input v-model.number="form.maxProposalsPerUser" type="number" min="1" class="form-input" />
          </div>
        </div>
      </div>

      <div v-if="formError" class="msg error">{{ formError }}</div>
      <div v-if="formOk" class="msg ok">{{ formOk }}</div>

      <div class="form-actions">
        <button class="btn-primary" :disabled="busy" @click="createRound">
          {{ busy ? '提交中...' : '创建投票轮次' }}
        </button>
        <button class="btn-ghost" @click="resetForm">清空</button>
      </div>
    </div>

    <!-- ═══ 双分类切换 ═══ -->
    <div class="vs-tabs">
      <button class="vs-tab" :class="{ on: activeTab === 'active' }" @click="activeTab = 'active'">
        活跃中 <span class="tab-count">{{ activeRounds.length }}</span>
      </button>
      <button class="vs-tab" :class="{ on: activeTab === 'archived' }" @click="activeTab = 'archived'">
        已归档 <span class="tab-count">{{ archivedRounds.length }}</span>
      </button>
    </div>

    <!-- ═══ 轮次列表 ═══ -->
    <div v-if="loading" class="loading">加载中...</div>

    <div v-else class="vs-list">
      <div v-for="r in (activeTab === 'active' ? activeRounds : archivedRounds)" :key="r.id" class="vs-card round-card" :class="{ closed: r.status === 'closed' }">
        <div class="round-head">
          <div class="round-title-wrap">
            <span class="round-title">{{ r.title }}</span>
            <span class="round-status" :class="r.status">{{ statusText(r.status) }}</span>
            <span v-if="r.archived" class="round-status archived">已归档</span>
          </div>
          <div class="round-actions">
            <button class="btn-mini" @click="openDetail(r)" title="查看投票明细与提案明细">明细</button>
            <button class="btn-mini" @click="openEdit(r)" title="编辑标题/说明/截止时间">编辑</button>
            <template v-if="activeTab === 'active'">
              <button v-if="r.status === 'open'" class="btn-mini" @click="closeRound(r)">结束投票</button>
              <button class="btn-mini" @click="archiveRound(r)">归档</button>
            </template>
            <template v-else>
              <button class="btn-mini" @click="unarchiveRound(r)">取消归档</button>
            </template>
            <button class="btn-mini danger" @click="removeRound(r)">删除</button>
          </div>
        </div>

        <div v-if="r.description" class="round-desc">{{ r.description }}</div>

        <div class="round-meta">
          <span>开始 <b>{{ fmtTime(r.createdAt) }}</b></span>
          <span>截止 <b>{{ r.endAt ? fmtTime(r.endAt) : '长期有效' }}</b></span>
          <span v-if="r.closedAt">结束 <b>{{ fmtTime(r.closedAt) }}</b>（{{ r.endedBy || '手动' }}）</span>
          <span>初始权重 <b>{{ r.baseWeight }}</b></span>
          <span>每用户可投 <b>{{ r.maxVotesPerUser }}</b> 个选项</span>
          <span>自主提案 <b>{{ r.allowProposals ? '开' : '关' }}</b></span>
          <span v-if="r.allowProposals">每用户提案 <b>{{ r.maxProposalsPerUser }}</b> 个</span>
          <span v-if="r.weightRules.length">加权规则 <b>{{ r.weightRules.length }}</b> 条</span>
          <span v-if="r.archivedAt">归档于 <b>{{ fmtTime(r.archivedAt) }}</b>（{{ r.archivedBy || '手动' }}）</span>
        </div>

        <div class="result-table">
          <div class="result-row header">
            <span class="c-option">选项</span>
            <span class="c-score">加权分</span>
            <span class="c-votes">票数</span>
            <span class="c-bar">占比</span>
          </div>
          <div v-for="o in r.options" :key="o.id" class="result-row" :class="{ highlight: o.type === 'custom' }">
            <span class="c-option">
              {{ o.text }}
              <span v-if="o.type === 'custom'" class="tag custom">自定义</span>
              <span v-if="o.type === 'custom' && !o.anonymous" class="tag proposer">{{ o.proposer }}</span>
              <span v-else-if="o.type === 'custom'" class="tag anon">匿名</span>
            </span>
            <span class="c-score">{{ o.score }}</span>
            <span class="c-votes">{{ o.votes }}</span>
            <span class="c-bar">
              <div class="bar-track">
                <div class="bar-fill" :style="{ width: pct(o, r.options) }"></div>
              </div>
              <span class="pct-num">{{ pct(o, r.options) }}</span>
            </span>
          </div>
        </div>
      </div>

      <div v-if="(activeTab === 'active' ? activeRounds : archivedRounds).length === 0" class="empty">
        {{ activeTab === 'active' ? '暂无活跃轮次，点击右上角发起第一个投票' : '暂无已归档轮次' }}
      </div>
    </div>

    <!-- ═══ 编辑轮次模态框 ═══ -->
    <Teleport to="body">
      <div v-if="showEdit" class="modal-mask" @click.self="showEdit = false">
        <div class="modal">
          <button class="modal-close" @click="showEdit = false" aria-label="关闭">✕</button>
          <h3>编辑投票轮次</h3>
          <div class="modal-form">
            <div class="form-row">
              <label class="form-label">标题</label>
              <input v-model="editForm.title" class="form-input" maxlength="60" />
            </div>
            <div class="form-row">
              <label class="form-label">说明内容</label>
              <textarea v-model="editForm.description" class="form-input textarea" rows="3" maxlength="300" placeholder="玩家页标题下方展示，可留空"></textarea>
            </div>
            <div class="form-row">
              <label class="form-label">截止时间 <span class="hint-tip">（留空 = 长期有效）</span></label>
              <input v-model="editForm.endAt" type="datetime-local" class="form-input" />
            </div>
            <div v-if="editError" class="msg error">{{ editError }}</div>
            <div class="modal-actions">
              <button class="btn-primary" :disabled="busy" @click="saveEdit">{{ busy ? '保存中...' : '保存修改' }}</button>
              <button class="btn-ghost" @click="showEdit = false">取消</button>
            </div>
          </div>
        </div>
      </div>
    </Teleport>

    <!-- ═══ 明细查看模态框（投票明细 / 提案明细，匿名对管理员无效） ═══ -->
    <Teleport to="body">
      <div v-if="showDetail" class="modal-mask" @click.self="showDetail = false">
        <div class="modal wide">
          <button class="modal-close" @click="showDetail = false" aria-label="关闭">✕</button>
          <h3>{{ detail?.title || '轮次' }} · 明细</h3>
          <p v-if="detail" class="modal-sub">
            共 {{ detail.options?.reduce((s, o) => s + (o.votes || 0), 0) || 0 }} 票 · 匿名对玩家生效，管理端全量可见
          </p>

          <div class="detail-tabs">
            <button class="detail-tab" :class="{ on: detailTab === 'vote' }" @click="detailTab = 'vote'">
              投票明细 <span class="tab-count">{{ detail?.options?.reduce((s, o) => s + (o.votes || 0), 0) || 0 }}</span>
            </button>
            <button class="detail-tab" :class="{ on: detailTab === 'proposal' }" @click="detailTab = 'proposal'">
              提案明细 <span class="tab-count">{{ proposals.length }}</span>
            </button>
          </div>

          <div v-if="detailLoading" class="loading">加载中...</div>
          <div v-else-if="detailError" class="msg error">{{ detailError }}</div>

          <!-- 投票明细：按选项分组 -->
          <div v-else-if="detailTab === 'vote'" class="detail-body">
            <div v-for="o in detail.options" :key="o.id" class="detail-group">
              <div class="group-head">
                <span class="group-name">
                  {{ o.text }}
                  <span v-if="o.type === 'custom' && !o.anonymous" class="tag proposer">{{ o.proposer }} 提案</span>
                  <span v-else-if="o.type === 'custom'" class="tag anon">匿名提案</span>
                </span>
                <span class="group-stat"><b class="score-blue">{{ o.score }}</b> 分 · {{ o.votes }} 票</span>
              </div>
              <div v-if="o.voters && o.voters.length" class="voter-table">
                <div class="voter-row header">
                  <span>投票人</span><span>QQ</span><span>权重</span><span>时间</span>
                </div>
                <div v-for="(v, vi) in o.voters" :key="vi" class="voter-row">
                  <span class="v-name">{{ v.username }}</span>
                  <span class="v-qq">{{ v.qq || '—' }}</span>
                  <span class="v-w">{{ v.weight }}</span>
                  <span class="v-at">{{ fmtTime(v.at) }}</span>
                </div>
              </div>
              <div v-else class="group-empty">暂无投票</div>
            </div>
            <div v-if="!detail.options.length" class="group-empty">暂无选项</div>
          </div>

          <!-- 提案明细 -->
          <div v-else class="detail-body">
            <div v-if="proposals.length" class="detail-group">
              <div class="group-head"><span class="group-name">玩家提案（{{ proposals.length }} 个）</span></div>
              <div class="voter-table">
                <div class="voter-row header">
                  <span>提案内容</span><span>提案人</span><span>匿名</span><span>票数/加权分</span>
                </div>
                <div v-for="o in proposals" :key="o.id" class="voter-row">
                  <span class="v-name">{{ o.text }}</span>
                  <span class="v-qq">{{ o.proposer || '—' }}</span>
                  <span class="v-w">{{ o.anonymous ? '是' : '否' }}</span>
                  <span class="v-at">{{ o.votes }} 票 / {{ o.score }} 分</span>
                </div>
              </div>
              <!-- 每个提案的投票人 -->
              <div v-for="o in proposals" :key="'v' + o.id" class="sub-group">
                <div class="group-head sub">
                  <span class="group-name">「{{ o.text }}」投票人 <template v-if="o.anonymous"><span class="tag anon">匿名提案</span></template></span>
                  <span class="group-stat">{{ o.votes }} 票</span>
                </div>
                <div v-if="o.voters && o.voters.length" class="voter-table">
                  <div class="voter-row header">
                    <span>投票人</span><span>QQ</span><span>权重</span><span>时间</span>
                  </div>
                  <div v-for="(v, vi) in o.voters" :key="vi" class="voter-row">
                    <span class="v-name">{{ v.username }}</span>
                    <span class="v-qq">{{ v.qq || '—' }}</span>
                    <span class="v-w">{{ v.weight }}</span>
                    <span class="v-at">{{ fmtTime(v.at) }}</span>
                  </div>
                </div>
                <div v-else class="group-empty">暂无投票</div>
              </div>
            </div>
            <div v-else class="group-empty">本轮无玩家提案</div>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
/* ═══ 蓝色主题：局部覆盖全局 CSS 变量（原全局为紫色 #6366f1/#4f46e5） ═══ */
.vote-settings {
  --accent-primary: #3b82f6;
  --accent-primary-hover: #2563eb;
  --accent-soft: rgba(59, 130, 246, 0.12);
  padding: 4px;
}

.vs-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 16px; gap: 12px; }
.vs-header h3 { margin: 0 0 4px; color: var(--text-primary); font-size: 1.2rem; }
.vs-sub { margin: 0; color: var(--text-secondary); font-size: 0.85rem; }

.btn-primary {
  padding: 10px 18px;
  background: linear-gradient(135deg, #3b82f6, #2563eb);
  color: #fff;
  border: none;
  border-radius: 10px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
  white-space: nowrap;
  box-shadow: 0 4px 14px rgba(37, 99, 235, 0.25);
}
.btn-primary:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 6px 18px rgba(37, 99, 235, 0.35); }
.btn-primary:disabled { opacity: 0.6; cursor: not-allowed; }
.btn-ghost { padding: 10px 16px; background: transparent; color: var(--text-secondary); border: 1px solid var(--border-light); border-radius: 10px; cursor: pointer; }
.btn-ghost:hover { border-color: #3b82f6; color: #3b82f6; }
.btn-mini {
  padding: 4px 12px; font-size: 0.8rem; border-radius: 8px;
  border: 1px solid var(--border-light);
  background: var(--bg-tertiary); color: var(--text-primary);
  cursor: pointer; transition: all 0.15s; white-space: nowrap;
}
.btn-mini:hover:not(:disabled) { border-color: #3b82f6; color: #3b82f6; }
.btn-mini.danger { color: #ef4444; }
.btn-mini.danger:hover:not(:disabled) { border-color: #ef4444; color: #ef4444; }
.btn-mini:disabled { opacity: 0.5; cursor: not-allowed; }

.vs-tabs { display: flex; gap: 8px; margin-bottom: 16px; }
.vs-tab {
  padding: 8px 22px; font-size: 0.88rem; border-radius: 999px;
  border: 1px solid var(--border-light); background: var(--bg-tertiary);
  color: var(--text-secondary); cursor: pointer; font-weight: 600; transition: all 0.2s;
  display: inline-flex; align-items: center; gap: 6px;
}
.vs-tab.on { background: linear-gradient(135deg, #3b82f6, #2563eb); color: #fff; border-color: transparent; box-shadow: 0 4px 12px rgba(37, 99, 235, 0.3); }
.tab-count { font-size: 0.72rem; font-weight: 700; background: rgba(255,255,255,0.85); color: #2563eb; padding: 0 7px; border-radius: 999px; }
.vs-tab:not(.on) .tab-count { background: var(--bg-hover); color: var(--text-secondary); }

.vs-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 14px;
  padding: 20px;
  margin-bottom: 16px;
  box-shadow: var(--shadow-sm);
}

/* ── 创建表单分区 ── */
.form-card { padding: 6px 20px 20px; }
.form-section {
  border-bottom: 1px dashed var(--border-light);
  padding: 16px 0;
  display: flex; flex-direction: column; gap: 10px;
}
.form-section:last-of-type { border-bottom: none; }
.sec-title {
  font-size: 0.9rem; font-weight: 800; color: #2563eb;
  letter-spacing: 0.02em;
  display: flex; align-items: center; gap: 8px;
  padding-left: 10px;
  border-left: 3px solid #3b82f6;
}
.hint-tip { font-size: 0.74rem; color: var(--text-muted); font-weight: 500; }
.req { color: #ef4444; font-style: normal; }

.form-row { display: flex; flex-direction: column; gap: 6px; }
.form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
.form-label { font-size: 0.82rem; font-weight: 600; color: var(--text-primary); display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.form-input, .form-select {
  padding: 9px 12px;
  border: 1px solid var(--border-light);
  border-radius: 8px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  font-size: 0.9rem;
  box-sizing: border-box;
  outline: none;
  transition: all 0.15s;
}
.form-input:focus, .form-select:focus { border-color: #3b82f6; box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.15); background: var(--bg-card); }
.form-input.textarea { resize: vertical; min-height: 60px; line-height: 1.6; }
.form-input.narrow { width: 90px; }
.form-select.narrow { width: 70px; }
.checkbox { accent-color: #3b82f6; width: 16px; height: 16px; }
.switch-label { cursor: pointer; }

.option-line, .rule-line { display: flex; align-items: center; gap: 8px; }
.option-line .form-input { flex: 1; }
.opt-idx {
  width: 22px; height: 22px; flex-shrink: 0;
  display: inline-flex; align-items: center; justify-content: center;
  background: var(--accent-soft); color: #2563eb;
  border-radius: 6px; font-size: 0.75rem; font-weight: 700;
}
.rule-text { font-size: 0.8rem; color: var(--text-secondary); white-space: nowrap; }
.form-actions { display: flex; gap: 10px; margin-top: 4px; }

.msg { padding: 10px 14px; border-radius: 8px; font-size: 0.85rem; }
.msg.error { background: rgba(239, 68, 68, 0.1); color: #ef4444; }
.msg.ok { background: rgba(34, 197, 94, 0.1); color: #16a34a; }

.loading, .empty { text-align: center; color: var(--text-secondary); padding: 40px 0; font-size: 0.9rem; }

/* ── 轮次卡片 ── */
.round-card.closed { opacity: 0.78; }
.round-head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; gap: 10px; flex-wrap: wrap; }
.round-title-wrap { display: flex; align-items: center; gap: 8px; min-width: 0; flex-wrap: wrap; }
.round-title { font-weight: 700; color: var(--text-primary); font-size: 1rem; }
.round-status { font-size: 0.72rem; font-weight: 700; padding: 2px 10px; border-radius: 10px; }
.round-status.open { background: rgba(34, 197, 94, 0.12); color: #16a34a; }
.round-status.closed { background: rgba(107, 114, 128, 0.15); color: var(--text-secondary); }
.round-status.archived { background: rgba(100, 116, 139, 0.18); color: #64748b; }
.round-actions { display: flex; gap: 6px; flex-shrink: 0; flex-wrap: wrap; }

.round-desc {
  margin-bottom: 10px;
  padding: 8px 12px;
  border-left: 3px solid #3b82f6;
  background: rgba(59, 130, 246, 0.05);
  border-radius: 0 8px 8px 0;
  color: var(--text-secondary);
  font-size: 0.82rem;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
}

.round-meta { display: flex; flex-wrap: wrap; gap: 6px 16px; font-size: 0.8rem; color: var(--text-secondary); margin-bottom: 12px; }
.round-meta b { color: var(--text-primary); font-variant-numeric: tabular-nums; }

.result-table { border: 1px solid var(--border-light); border-radius: 10px; overflow: hidden; }
.result-row { display: grid; grid-template-columns: 1.4fr 80px 60px 1fr; align-items: center; padding: 8px 12px; gap: 8px; font-size: 0.85rem; }
.result-row.header { background: var(--bg-hover); font-weight: 600; color: var(--text-secondary); font-size: 0.78rem; }
.result-row:not(.header) { border-top: 1px solid var(--border-light); }
.result-row.highlight { background: rgba(59, 130, 246, 0.06); }
.c-option { display: flex; align-items: center; gap: 6px; color: var(--text-primary); min-width: 0; overflow-wrap: anywhere; }
.c-score { font-weight: 700; color: #3b82f6; font-variant-numeric: tabular-nums; }
.c-votes { color: var(--text-secondary); font-variant-numeric: tabular-nums; }
.c-bar { display: flex; align-items: center; gap: 8px; }
.bar-track { flex: 1; height: 8px; background: var(--bg-hover); border-radius: 4px; overflow: hidden; }
.bar-fill { height: 100%; background: linear-gradient(90deg, #3b82f6, #60a5fa); border-radius: 4px; transition: width 0.4s ease; }
.pct-num { font-size: 0.75rem; color: var(--text-secondary); width: 48px; text-align: right; font-variant-numeric: tabular-nums; }
.tag { font-size: 0.68rem; padding: 1px 8px; border-radius: 8px; font-weight: 600; white-space: nowrap; }
.tag.custom { background: rgba(59, 130, 246, 0.12); color: #2563eb; }
.tag.proposer { background: rgba(59, 130, 246, 0.1); color: #2563eb; }
.tag.anon { background: rgba(107, 114, 128, 0.12); color: var(--text-secondary); }

/* ── 模态框 ── */
.modal-mask {
  position: fixed; inset: 0; z-index: 200;
  display: flex; align-items: center; justify-content: center;
  background: rgba(15, 23, 42, 0.45);
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  padding: 20px; box-sizing: border-box;
}
.modal {
  position: relative;
  width: min(440px, 92vw);
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 16px;
  padding: 26px 24px 22px;
  box-shadow: var(--shadow-lg);
  box-sizing: border-box;
  max-height: 88vh;
  overflow-y: auto;
  animation: modalIn 0.2s ease;
}
.modal.wide { width: min(760px, 94vw); }
@keyframes modalIn {
  from { opacity: 0; transform: translateY(10px) scale(0.98); }
  to { opacity: 1; transform: none; }
}
.modal-close {
  position: absolute; top: 12px; right: 14px;
  background: none; border: none; color: var(--text-secondary);
  font-size: 1.05rem; cursor: pointer; padding: 4px 8px; border-radius: 8px;
}
.modal-close:hover { background: rgba(0, 0, 0, 0.06); color: var(--text-primary); }
.modal h3 { margin: 0 0 6px; font-size: 1.15rem; font-weight: 800; color: var(--text-primary); }
.modal-sub { margin: 0 0 16px; font-size: 0.8rem; color: var(--text-secondary); }
.modal-form { display: flex; flex-direction: column; gap: 12px; }
.modal-actions { display: flex; gap: 10px; margin-top: 6px; }

/* ── 明细模态框 ── */
.detail-tabs { display: flex; gap: 8px; margin-bottom: 14px; }
.detail-tab {
  padding: 7px 18px; font-size: 0.84rem; border-radius: 999px;
  border: 1px solid var(--border-light); background: var(--bg-tertiary);
  color: var(--text-secondary); cursor: pointer; font-weight: 600; transition: all 0.2s;
  display: inline-flex; align-items: center; gap: 6px;
}
.detail-tab.on { background: linear-gradient(135deg, #3b82f6, #2563eb); color: #fff; border-color: transparent; }
.detail-body { display: flex; flex-direction: column; gap: 14px; }
.detail-group {
  border: 1px solid var(--border-light);
  border-radius: 12px;
  overflow: hidden;
  background: var(--bg-tertiary);
}
.group-head {
  display: flex; justify-content: space-between; align-items: center; gap: 10px;
  padding: 9px 14px;
  background: rgba(59, 130, 246, 0.06);
  border-bottom: 1px solid var(--border-light);
  flex-wrap: wrap;
}
.group-head.sub { background: var(--bg-hover); }
.group-name { font-weight: 700; color: var(--text-primary); font-size: 0.88rem; display: flex; align-items: center; gap: 6px; flex-wrap: wrap; overflow-wrap: anywhere; }
.group-stat { font-size: 0.78rem; color: var(--text-secondary); white-space: nowrap; }
.score-blue { color: #3b82f6; font-weight: 800; }
.voter-table { font-size: 0.82rem; }
.voter-row {
  display: grid; grid-template-columns: 1.2fr 1fr 0.6fr 1.4fr;
  gap: 8px; padding: 7px 14px; align-items: center;
}
.voter-row:not(.header) { border-top: 1px solid var(--border-light); }
.voter-row.header { background: var(--bg-hover); font-weight: 600; color: var(--text-secondary); font-size: 0.74rem; }
.v-name { color: var(--text-primary); font-weight: 600; overflow-wrap: anywhere; }
.v-qq { color: var(--text-secondary); font-variant-numeric: tabular-nums; }
.v-w { color: #3b82f6; font-weight: 700; font-variant-numeric: tabular-nums; }
.v-at { color: var(--text-muted); font-variant-numeric: tabular-nums; }
.group-empty { padding: 16px 14px; text-align: center; color: var(--text-muted); font-size: 0.82rem; }
.sub-group { margin-top: 4px; }

@media (max-width: 640px) {
  .form-grid { grid-template-columns: 1fr; }
  .voter-row { grid-template-columns: 1fr 1fr; }
  .voter-row.header { display: none; }
  .voter-row:not(.header) { border-top: 1px dashed var(--border-light); }
}
</style>
