<script setup>
import { ref, computed, onMounted } from 'vue'
import { get, post } from '../../utils/api.js'

const loading = ref(true)
const error = ref('')
const success = ref('')
const saving = ref(false)

// ═══ 配置（与插件 BugFixesConfig 对齐）═══
const config = ref({
  enabled: false,
  loginFix: true,
  chestFix: true,
  minionLimit: true,
  lightning: true,
})

// 子功能项（名称 / 说明 / 字段 key）
const subItems = [
  { key: 'loginFix', name: '登录修复', desc: '修复 UUID 变更导致无法进服的连接层 Bug：进服时对已有账户发起密码挑战验证' },
  { key: 'chestFix', name: '宝箱修复', desc: '宝箱数据包校验：拦截复制 / 越界槽位 / 恶意扩容 / NPC 减益伤害等异常数据包' },
  { key: 'minionLimit', name: '召唤物限制', desc: '召唤物数量上限：实时统计活跃召唤弹幕槽位，超限拦截创建并踢出' },
  { key: 'lightning', name: '粒子防线', desc: '拦截伪造粒子洪泛，/lightning 服务端闪电广播入口（需总开关开启）' },
]

const activeCount = computed(() => subItems.filter(s => config.value[s.key]).length)
const masterOn = computed(() => config.value.enabled)

// ═══ 读写 ═══
const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await get('/api/anticheat/bugfix')
    const data = await res.json()
    const cfg = data?.config
    if (cfg && typeof cfg.enabled === 'boolean') {
      config.value.enabled = !!cfg.enabled
      config.value.loginFix = cfg.loginFix !== undefined ? !!cfg.loginFix : true
      config.value.chestFix = cfg.chestFix !== undefined ? !!cfg.chestFix : true
      config.value.minionLimit = cfg.minionLimit !== undefined ? !!cfg.minionLimit : true
      config.value.lightning = cfg.lightning !== undefined ? !!cfg.lightning : true
    } else {
      error.value = data?.error || '加载配置失败'
    }
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  } finally {
    loading.value = false
  }
}

const doSave = async () => {
  error.value = ''
  success.value = ''
  saving.value = true
  try {
    const payload = {
      enabled: config.value.enabled,
      loginFix: config.value.loginFix,
      chestFix: config.value.chestFix,
      minionLimit: config.value.minionLimit,
      lightning: config.value.lightning,
    }
    const res = await post('/api/anticheat/bugfix/set', payload)
    const data = await res.json()
    if (data.status === '200') {
      success.value = '已保存并即时生效'
      setTimeout(() => { success.value = '' }, 2500)
    } else {
      error.value = data.error || '保存失败'
    }
  } catch (err) {
    error.value = '保存失败: ' + err.message
  } finally {
    saving.value = false
  }
}

onMounted(fetchConfig)
</script>

<template>
  <div class="settings-page">
    <div v-if="loading" class="loading-state"><p>加载中...</p></div>

    <div v-else class="settings-content">
      <!-- ═══ 当前状态 ═══ -->
      <div class="section-card">
        <h3>反恶性 Bug 修复</h3>
        <div class="status-row">
          <span :class="['status-pill', masterOn ? 'pill-active' : 'pill-idle']">
            {{ masterOn ? `已启用（${activeCount} 项子功能开启）` : '未启用' }}
          </span>
          <span class="status-meta">总开关关闭时全部子功能不生效；子功能默认全开</span>
        </div>
        <p class="section-desc">
          修复 TShock / Terraria 原版核心的恶性漏洞利用面：登录连接层 Bug、宝箱数据包校验、召唤物数量洪泛、伪造粒子洪泛。
          配置保存在插件端 TSWeb/BugFixes.json，保存后即时生效（先卸载再按新配置挂载）。
        </p>
      </div>

      <!-- ═══ 总开关 ═══ -->
      <div class="section-card">
        <div class="toggle-row">
          <div class="toggle-label-wrap">
            <span class="toggle-label">启用反恶性 Bug 修复</span>
            <span class="toggle-hint">总开关。关闭后不加载任何修复子模块（/lightning 同时失效）</span>
          </div>
          <label class="switch" :title="config.enabled ? '点击停用全部修复' : '点击启用全部修复'">
            <input type="checkbox" v-model="config.enabled" />
            <span class="slider"></span>
          </label>
        </div>
      </div>

      <!-- ═══ 子功能开关 ═══ -->
      <div class="section-card">
        <div class="card-head">
          <h3>子功能开关</h3>
          <span v-if="!masterOn" class="off-tag">总开关关闭，暂不生效</span>
        </div>
        <div v-if="!subItems.length" class="empty">暂无子功能</div>
        <div v-for="item in subItems" :key="item.key" class="toggle-row"
          :class="{ dimmed: !masterOn }">
          <div class="toggle-label-wrap">
            <span class="toggle-label">{{ item.name }}</span>
            <span class="toggle-hint">{{ item.desc }}</span>
          </div>
          <label class="switch" :title="masterOn ? '点击切换' : '请先开启总开关'">
            <input type="checkbox" v-model="config[item.key]" :disabled="!masterOn" />
            <span class="slider"></span>
          </label>
        </div>
      </div>

      <!-- ═══ 保存 ═══ -->
      <div class="save-row">
        <button class="action-btn btn-primary" :disabled="saving" @click="doSave">
          {{ saving ? '保存中...' : '保存配置' }}
        </button>
      </div>

      <!-- Toast -->
      <Transition name="toast">
        <div v-if="success" class="toast toast-success"><span>{{ success }}</span></div>
      </Transition>
      <Transition name="toast">
        <div v-if="error" class="toast toast-error"><span>{{ error }}</span></div>
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
  max-width: 860px;
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
  margin-bottom: 20px;
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
  margin: 8px 0 0 0;
  color: var(--text-muted);
  font-size: 0.85rem;
  line-height: 1.5;
}

.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.off-tag {
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 10px;
  border-radius: 999px;
  background: rgba(148, 163, 184, 0.15);
  color: #94a3b8;
  border: 1px solid rgba(148, 163, 184, 0.3);
  white-space: nowrap;
}

/* ── 状态 ── */
.status-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 8px;
}

.status-pill {
  padding: 6px 14px;
  border-radius: 999px;
  font-size: 0.9rem;
  font-weight: 600;
}

.pill-active {
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.pill-idle {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
  border: 1px solid rgba(34, 197, 94, 0.3);
}

.status-meta {
  color: var(--text-muted);
  font-size: 0.85rem;
}

/* ── 开关行 ── */
.toggle-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px 0;
  border-bottom: 1px solid var(--border-light);
  transition: opacity 0.2s ease;
}

.toggle-row:last-child {
  border-bottom: none;
}

.toggle-row.dimmed {
  opacity: 0.55;
}

.toggle-label-wrap {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
  min-width: 0;
}

.toggle-label {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.95rem;
}

.toggle-hint {
  color: var(--text-muted);
  font-size: 0.8rem;
  line-height: 1.4;
}

/* ── 开关 ── */
.switch {
  position: relative;
  display: inline-block;
  width: 44px;
  height: 24px;
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
  border-radius: 24px;
  transition: all 0.3s ease;
}

.slider::before {
  content: '';
  position: absolute;
  height: 16px;
  width: 16px;
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
  transform: translateX(20px);
  background: white;
}

.switch input:disabled + .slider {
  cursor: not-allowed;
  opacity: 0.6;
}

.empty {
  color: var(--text-muted);
  font-size: 0.85rem;
  padding: 16px 0;
  text-align: center;
}

/* ── 保存 ── */
.save-row {
  display: flex;
  justify-content: flex-end;
}

.action-btn {
  padding: 9px 24px;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  border: none;
  transition: all 0.2s ease;
}

.action-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-primary {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  box-shadow: 0 2px 10px rgba(99, 102, 241, 0.3);
}

.btn-primary:hover:not(:disabled) {
  opacity: 0.9;
}

/* ── Toast ── */
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
</style>
