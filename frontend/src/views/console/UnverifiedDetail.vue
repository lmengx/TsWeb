<script setup>
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { get } from '../../utils/api.js'
import { getUnverifiedDetail, registerPlayer, forceLogin, kickUnverified, banUnverified } from '../../utils/unverifiedApi.js'

const route = useRoute()
const router = useRouter()

const player = ref(null)
const loading = ref(true)
const error = ref('')

// 注册
const showRegisterModal = ref(false)
const regPassword = ref('')
const regGroup = ref('')
const regLoading = ref(false)
const regError = ref('')
const regSuccess = ref('')
const showPasswordText = ref(false)
const availableGroups = ref([])
const showGroupDropdown = ref(false)
const dropdownStyle = ref({})

// 强制登录
const showForceLoginModal = ref(false)
const flLoading = ref(false)
const flError = ref('')
const flSuccess = ref('')

// 踢出
const showKickModal = ref(false)
const kickReason = ref('')
const kickLoading = ref(false)
const kickError = ref('')
const kickSuccess = ref('')

// 封禁
const showBanModal = ref(false)
const banReason = ref('不当行为')
const banLoading = ref(false)
const banError = ref('')
const banSuccess = ref('')

const fetchDetail = async () => {
  loading.value = true
  error.value = ''
  try {
    const res = await getUnverifiedDetail(route.params.nickname)
    const data = await res.json()
    if (data.error) {
      error.value = data.error
    } else {
      player.value = data
    }
  } catch (err) {
    error.value = err.message || '加载失败'
  }
  loading.value = false
}

const openRegisterModal = async () => {
  showRegisterModal.value = true
  regPassword.value = ''
  regGroup.value = ''
  regError.value = ''
  regSuccess.value = ''
  showPasswordText.value = false
  showGroupDropdown.value = false
  await fetchGroups()
}

const generateRandomPassword = () => {
  const chars = 'abcdefghijkmnopqrstuvwxyz0123456789'
  let password = ''
  for (let i = 0; i < 6; i++) {
    password += chars[Math.floor(Math.random() * chars.length)]
  }
  regPassword.value = password
  showPasswordText.value = true
}

const fetchGroups = async () => {
  try {
    const res = await get('/api/tshock/groups')
    const data = await res.json()
    if (data.groups) {
      availableGroups.value = data.groups.map(g => g.GroupName)
    }
  } catch {}
}

const toggleGroupDropdown = () => {
  showGroupDropdown.value = !showGroupDropdown.value
  if (showGroupDropdown.value) {
    const trigger = document.querySelector('.custom-select-trigger')
    if (trigger) {
      const rect = trigger.getBoundingClientRect()
      dropdownStyle.value = {
        position: 'fixed',
        top: `${rect.bottom + 4}px`,
        left: `${rect.left}px`,
        width: `${rect.width}px`,
        zIndex: 2000
      }
    }
  }
}

const selectGroup = (group) => {
  regGroup.value = group
  showGroupDropdown.value = false
}

const executeRegister = async () => {
  if (!regPassword.value.trim()) {
    regError.value = '请输入密码'
    return
  }
  regLoading.value = true
  regError.value = ''
  regSuccess.value = ''
  try {
    const res = await registerPlayer(route.params.nickname, regPassword.value, regGroup.value)
    const data = await res.json()
    if (data.error) {
      regError.value = data.error
    } else {
      regSuccess.value = data.message || '注册成功'
      player.value.hasAccount = true
      player.value.isLoggedIn = true
      setTimeout(() => { showRegisterModal.value = false }, 1500)
    }
  } catch (err) {
    regError.value = err.message || '注册失败'
  }
  regLoading.value = false
}

const openForceLoginModal = () => {
  showForceLoginModal.value = true
  flError.value = ''
  flSuccess.value = ''
}

const executeForceLogin = async () => {
  flLoading.value = true
  flError.value = ''
  flSuccess.value = ''
  try {
    const res = await forceLogin(route.params.nickname)
    const data = await res.json()
    if (data.error) {
      flError.value = data.error
    } else {
      flSuccess.value = data.message || '强制登录成功'
      player.value.isLoggedIn = true
      setTimeout(() => { showForceLoginModal.value = false }, 1500)
    }
  } catch (err) {
    flError.value = err.message || '操作失败'
  }
  flLoading.value = false
}

const openKickModal = () => {
  showKickModal.value = true
  kickReason.value = ''
  kickError.value = ''
  kickSuccess.value = ''
}

const executeKick = async () => {
  kickLoading.value = true
  kickError.value = ''
  kickSuccess.value = ''
  try {
    const res = await kickUnverified(route.params.nickname, kickReason.value || undefined)
    const data = await res.json()
    if (data.error) {
      kickError.value = data.error
    } else {
      kickSuccess.value = data.message || '已踢出'
      setTimeout(() => router.push('/console/players'), 1500)
    }
  } catch (err) {
    kickError.value = err.message || '操作失败'
  }
  kickLoading.value = false
}

const openBanModal = () => {
  showBanModal.value = true
  banReason.value = '不当行为'
  banError.value = ''
  banSuccess.value = ''
}

const executeBan = async () => {
  banLoading.value = true
  banError.value = ''
  banSuccess.value = ''
  try {
    const res = await banUnverified(route.params.nickname, banReason.value)
    const data = await res.json()
    if (data.error) {
      banError.value = data.error
    } else {
      banSuccess.value = data.message || '已封禁'
      setTimeout(() => router.push('/console/players'), 2000)
    }
  } catch (err) {
    banError.value = err.message || '操作失败'
  }
  banLoading.value = false
}

const goBack = () => {
  router.push('/console/players')
}

onMounted(fetchDetail)
</script>

<template>
  <div class="detail-page">
    <div class="page-header">
      <button @click="goBack" class="back-btn">← 返回玩家列表</button>
    </div>

    <div v-if="loading" class="loading-state">加载中...</div>
    <div v-else-if="error" class="error-state">{{ error }}</div>
    <div v-else-if="player" class="detail-content">
      <!-- 玩家信息卡片 -->
      <div class="info-card">
        <div class="player-name-row">
          <h2>{{ player.nickname }}</h2>
          <span :class="['status-badge', player.hasAccount ? 'verified' : 'unregistered']">
            {{ player.hasAccount ? '未验证' : '未注册' }}
          </span>
        </div>
        <div class="info-grid">
          <div class="info-item">
            <span class="label">IP</span>
            <span class="value">{{ player.ip || '未知' }}</span>
          </div>
          <div class="info-item">
            <span class="label">UUID</span>
            <span class="value mono">{{ player.uuid || '未知' }}</span>
          </div>
          <div class="info-item">
            <span class="label">连接状态</span>
            <span class="value">{{ player.stateText }} ({{ player.state }})</span>
          </div>
          <div class="info-item">
            <span class="label">当前用户组</span>
            <span class="value">{{ player.group }}</span>
          </div>
          <div v-if="player.hasAccount" class="info-item">
            <span class="label">已有账号</span>
            <span class="value">{{ player.accountName }}</span>
          </div>
        </div>
      </div>

      <!-- 操作区域 -->
      <div class="actions-card">
        <h3>管理操作</h3>
        <div class="action-buttons">
          <!-- 未注册 → 注册 -->
          <div v-if="!player.hasAccount" class="action-block">
            <button @click="openRegisterModal" class="action-btn register">注册账号</button>
            <p class="action-desc">为该玩家创建账号并自动登录</p>
          </div>

          <!-- 有账号未登录 → 强制登录 -->
          <div v-if="player.hasAccount && !player.isLoggedIn" class="action-block">
            <button @click="openForceLoginModal" class="action-btn force-login">强制登录</button>
            <p class="action-desc">强制登录并更新UUID绑定到当前设备</p>
          </div>

          <!-- 踢出 -->
          <div class="action-block">
            <button @click="openKickModal" class="action-btn kick">踢出</button>
            <p class="action-desc">将玩家踢出服务器</p>
          </div>

          <!-- 封禁 -->
          <div class="action-block">
            <button @click="openBanModal" class="action-btn ban">封禁</button>
            <p class="action-desc warn">⚠ 仅封禁IP+UUID，不封角色名</p>
          </div>
        </div>
      </div>
    </div>

    <!-- 注册模态框 -->
    <div v-if="showRegisterModal" class="modal-overlay" @click.self="showRegisterModal = false">
      <div class="modal">
        <div class="modal-header">
          <h3>注册账号 — {{ route.params.nickname }}</h3>
          <button @click="showRegisterModal = false" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="form-row">
            <label>密码</label>
            <div class="pwd-row">
              <div class="pwd-input-wrap">
                <input v-model="regPassword" :type="showPasswordText ? 'text' : 'password'" placeholder="输入密码" class="form-input" />
                <button class="pwd-toggle" @click="showPasswordText = !showPasswordText" type="button">
                  {{ showPasswordText ? '隐藏' : '显示' }}
                </button>
              </div>
              <span class="generate-badge" @click="generateRandomPassword">生成随机密码</span>
            </div>
          </div>
          <div class="form-row">
            <label>用户组（可选）</label>
            <div class="custom-select-wrapper">
              <div class="custom-select-trigger" @click="toggleGroupDropdown">
                <span class="select-value" :class="{ placeholder: !regGroup }">{{ regGroup || '请选择用户组' }}</span>
                <span class="select-arrow" :class="{ rotated: showGroupDropdown }">▼</span>
              </div>
            </div>
            <Teleport to="body">
              <div v-if="showGroupDropdown && showRegisterModal" class="custom-select-dropdown" :style="dropdownStyle">
                <div class="custom-select-option" :class="{ selected: !regGroup }" @click="selectGroup('')">默认权限组</div>
                <div v-for="g in availableGroups" :key="g" class="custom-select-option" :class="{ selected: regGroup === g }" @click="selectGroup(g)">{{ g }}</div>
              </div>
            </Teleport>
          </div>
          <div v-if="regError" class="msg error">{{ regError }}</div>
          <div v-if="regSuccess" class="msg success">{{ regSuccess }}</div>
        </div>
        <div class="modal-footer">
          <button @click="showRegisterModal = false" class="cancel-btn">取消</button>
          <button @click="executeRegister" :disabled="regLoading" class="submit-btn">
            {{ regLoading ? '注册中...' : '注册并登录' }}
          </button>
        </div>
      </div>
    </div>

    <!-- 强制登录模态框 -->
    <div v-if="showForceLoginModal" class="modal-overlay" @click.self="showForceLoginModal = false">
      <div class="modal">
        <div class="modal-header">
          <h3>强制登录 — {{ route.params.nickname }}</h3>
          <button @click="showForceLoginModal = false" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <p>将强制登录账号 <strong>{{ player?.accountName }}</strong> 并更新UUID到当前设备。</p>
          <div v-if="flError" class="msg error">{{ flError }}</div>
          <div v-if="flSuccess" class="msg success">{{ flSuccess }}</div>
        </div>
        <div class="modal-footer">
          <button @click="showForceLoginModal = false" class="cancel-btn">取消</button>
          <button @click="executeForceLogin" :disabled="flLoading" class="submit-btn">
            {{ flLoading ? '登录中...' : '确认强制登录' }}
          </button>
        </div>
      </div>
    </div>

    <!-- 踢出模态框 -->
    <div v-if="showKickModal" class="modal-overlay" @click.self="showKickModal = false">
      <div class="modal">
        <div class="modal-header">
          <h3>踢出 — {{ route.params.nickname }}</h3>
          <button @click="showKickModal = false" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="form-row">
            <label>原因（可选）</label>
            <input v-model="kickReason" type="text" placeholder="留空使用默认原因" class="form-input" />
          </div>
          <div v-if="kickError" class="msg error">{{ kickError }}</div>
          <div v-if="kickSuccess" class="msg success">{{ kickSuccess }}</div>
        </div>
        <div class="modal-footer">
          <button @click="showKickModal = false" class="cancel-btn">取消</button>
          <button @click="executeKick" :disabled="kickLoading" class="submit-btn">
            {{ kickLoading ? '踢出中...' : '确认踢出' }}
          </button>
        </div>
      </div>
    </div>

    <!-- 封禁模态框 -->
    <div v-if="showBanModal" class="modal-overlay" @click.self="showBanModal = false">
      <div class="modal">
        <div class="modal-header">
          <h3>封禁 — {{ route.params.nickname }}</h3>
          <button @click="showBanModal = false" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="form-row">
            <label>原因</label>
            <input v-model="banReason" type="text" class="form-input" />
          </div>
          <p class="warn-text">⚠ 仅封禁IP+UUID，不封角色名。该玩家将无法用当前IP和设备再次进入。</p>
          <div v-if="banError" class="msg error">{{ banError }}</div>
          <div v-if="banSuccess" class="msg success">{{ banSuccess }}</div>
        </div>
        <div class="modal-footer">
          <button @click="showBanModal = false" class="cancel-btn">取消</button>
          <button @click="executeBan" :disabled="banLoading" class="submit-btn danger">
            {{ banLoading ? '封禁中...' : '确认封禁' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.detail-page {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding: 0;
}

.page-header {
  padding: 16px 24px;
  display: flex;
  align-items: center;
  gap: 12px;
}

.back-btn {
  padding: 8px 16px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  cursor: pointer;
  font-size: 0.9rem;
  transition: all 0.2s;
}

.back-btn:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.loading-state, .error-state {
  text-align: center;
  padding: 60px 20px;
  color: var(--text-muted);
}

.error-state { color: var(--accent-error); }

.detail-content {
  flex: 1;
  overflow-y: auto;
  padding: 0 24px 24px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.info-card {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  border: 1px solid var(--border-light);
  box-shadow: var(--shadow-lg);
}

.player-name-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 20px;
}

.player-name-row h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.4rem;
}

.status-badge {
  padding: 4px 12px;
  border-radius: 20px;
  font-size: 0.8rem;
  font-weight: 600;
}

.status-badge.unregistered {
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.status-badge.verified {
  background: rgba(245, 158, 11, 0.15);
  color: #f59e0b;
  border: 1px solid rgba(245, 158, 11, 0.3);
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 12px;
}

.info-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
}

.info-item .label {
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-weight: 600;
}

.info-item .value {
  color: var(--text-primary);
  font-size: 0.95rem;
}

.info-item .value.mono {
  font-family: monospace;
  font-size: 0.85rem;
  word-break: break-all;
}

.actions-card {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  border: 1px solid var(--border-light);
  box-shadow: var(--shadow-lg);
}

.actions-card h3 {
  margin: 0 0 16px;
  color: var(--text-primary);
  font-size: 1.1rem;
}

.action-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
}

.action-block {
  flex: 1;
  min-width: 180px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.action-btn {
  padding: 12px 20px;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 600;
  transition: all 0.25s;
  color: white;
}

.action-btn:hover {
  transform: translateY(-1px);
  box-shadow: var(--shadow-md);
}

.action-btn.register { background: linear-gradient(135deg, #10b981, #059669); }
.action-btn.force-login { background: linear-gradient(135deg, #3b82f6, #2563eb); }
.action-btn.kick { background: linear-gradient(135deg, #f59e0b, #d97706); }
.action-btn.ban { background: linear-gradient(135deg, #dc2626, #b91c1c); }

.action-desc {
  margin: 0;
  color: var(--text-muted);
  font-size: 0.8rem;
}

.action-desc.warn {
  color: #ef4444;
  font-size: 0.8rem;
}

/* 模态框 */
.modal-overlay {
  position: fixed;
  z-index: 1000;
  top: 0; left: 0; right: 0; bottom: 0;
  background: rgba(0,0,0,0.6);
  display: flex;
  align-items: center;
  justify-content: center;
}

.modal {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  width: 90%;
  max-width: 460px;
  box-shadow: 0 20px 60px rgba(0,0,0,0.4);
  border: 1px solid var(--border-light);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 18px 24px;
  border-bottom: 1px solid var(--border-light);
}

.modal-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.05rem;
}

.close-btn {
  background: none;
  border: none;
  color: var(--text-muted);
  font-size: 1.5rem;
  cursor: pointer;
}

.modal-body {
  padding: 24px;
}

.form-row {
  margin-bottom: 16px;
}

.form-row label {
  display: block;
  color: var(--text-primary);
  font-size: 0.9rem;
  font-weight: 600;
  margin-bottom: 6px;
}

.form-input {
  width: 100%;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  box-sizing: border-box;
}

.form-input:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.warn-text {
  color: #ef4444;
  font-size: 0.85rem;
  margin: 8px 0;
}

/* 密码行 */
.pwd-row {
  display: flex;
  align-items: center;
  gap: 10px;
}

.pwd-input-wrap {
  position: relative;
  flex: 1;
}

.pwd-input-wrap .form-input {
  width: 100%;
  padding-right: 52px;
  box-sizing: border-box;
}

.pwd-toggle {
  position: absolute;
  right: 4px;
  top: 50%;
  transform: translateY(-50%);
  background: none;
  border: none;
  color: var(--accent-primary);
  cursor: pointer;
  font-size: 0.78rem;
  padding: 4px 8px;
  white-space: nowrap;
}

.pwd-toggle:hover {
  text-decoration: underline;
}

.generate-badge {
  flex-shrink: 0;
  padding: 5px 12px;
  background: rgba(139, 92, 246, 0.12);
  color: #a78bfa;
  border: 1px solid rgba(139, 92, 246, 0.3);
  border-radius: 20px;
  cursor: pointer;
  font-size: 0.78rem;
  font-weight: 500;
  white-space: nowrap;
  user-select: none;
  transition: all 0.2s;
}

.generate-badge:hover {
  background: rgba(139, 92, 246, 0.25);
  border-color: rgba(139, 92, 246, 0.6);
}

/* 自定义选择器 */
.custom-select-wrapper {
  position: relative;
}

.custom-select-trigger {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  cursor: pointer;
  color: var(--text-primary);
  font-size: 0.95rem;
  transition: border-color 0.2s;
}

.custom-select-trigger:hover {
  border-color: var(--accent-primary);
}

.select-value { color: var(--text-primary); }
.select-value.placeholder { color: var(--text-muted); }

.select-arrow {
  color: var(--text-muted);
  font-size: 0.75rem;
  transition: transform 0.2s;
}

.select-arrow.rotated { transform: rotate(180deg); }

.custom-select-dropdown {
  position: fixed;
  z-index: 2000;
  background: var(--bg-card);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  max-height: 200px;
  overflow-y: auto;
  box-shadow: var(--shadow-lg);
}

.custom-select-option {
  padding: 10px 16px;
  cursor: pointer;
  color: var(--text-primary);
  font-size: 0.9rem;
  transition: all 0.15s;
  border-bottom: 1px solid var(--border-light);
}

.custom-select-option:last-child { border-bottom: none; }

.custom-select-option:hover {
  background: rgba(99, 102, 241, 0.15);
  color: var(--accent-primary);
}

.custom-select-option.selected {
  background: rgba(99, 102, 241, 0.2);
  color: var(--accent-primary);
  font-weight: 600;
}

.msg {
  padding: 10px 14px;
  border-radius: var(--radius-md);
  font-size: 0.85rem;
  margin-top: 4px;
}

.msg.error {
  background: rgba(239,68,68,0.1);
  color: #ef4444;
  border: 1px solid rgba(239,68,68,0.3);
}

.msg.success {
  background: rgba(16,185,129,0.1);
  color: #10b981;
  border: 1px solid rgba(16,185,129,0.3);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 24px;
  border-top: 1px solid var(--border-light);
}

.cancel-btn, .submit-btn {
  padding: 10px 24px;
  border: none;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.cancel-btn {
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
}

.submit-btn {
  background: var(--accent-primary);
  color: white;
}

.submit-btn.danger {
  background: #dc2626;
}

.submit-btn:disabled, .cancel-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
</style>
