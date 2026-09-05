<script setup>
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { apiRequest, post, del } from '../utils/api.js'
import { getUserFromStorage } from '../utils/authHelper.js'

const systemSettings = ref({ server: { port: 3000, host: '0.0.0.0' } })
const singleLogin = ref({ enabled: false })   // 禁止多服登录（全局）
const accounts = ref([])
const linkableAccounts = ref([])              // QQ 绑定中可选取为管理员的用户
const settingsTab = ref('listen')   // 'listen' 监听设置 | 'accounts' 账户管理
const showAddAccount = ref(false)
const addMode = ref('linked')       // 'linked' 从QQ绑定中选取 | 'manual' 手动用户名+密码
const linkedSearch = ref('')        // 绑定用户搜索（名称 / QQ号）
const pickerOpen = ref(false)       // 可搜索下拉展开状态
const accountForm = ref({ username: '', password: '', role: 'subadmin', linkedTo: '' })
const resetResult = ref(null)

// 输入搜索时，若与已选用户不一致则清除选中（需重新选择）
const onLinkedSearchInput = () => {
  if (accountForm.value.linkedTo && accountForm.value.linkedTo !== linkedSearch.value) {
    accountForm.value.linkedTo = ''
  }
}
const selectLinkable = (u) => {
  accountForm.value.linkedTo = u.username
  linkedSearch.value = u.username
  pickerOpen.value = false
}
// 点击选项用 mousedown.prevent，blur 延迟关闭避免误关
const onLinkedBlur = () => {
  setTimeout(() => { pickerOpen.value = false }, 120)
}

// 当前登录用户（自己不可被删除/改角色）
const currentUser = getUserFromStorage() || {}
const isSelf = (a) => String(a.username || '').toLowerCase() === String(currentUser.username || '').toLowerCase()

// 按名称或 QQ 号过滤可选绑定用户
const filteredLinkable = computed(() => {
  const q = linkedSearch.value.trim().toLowerCase()
  if (!q) return linkableAccounts.value
  return linkableAccounts.value.filter(u =>
    String(u.username || '').toLowerCase().includes(q) ||
    String(u.qq || '').includes(q))
})

// ═══ admin 授予确认弹窗（警告 + 3 秒倒计时）═══
const showAdminConfirm = ref(false)
const confirmCountdown = ref(0)
const pendingAction = ref(null)   // { type: 'create' } | { type: 'role', account, role }
let countdownTimer = null

const startCountdown = (n) => {
  confirmCountdown.value = n
  clearInterval(countdownTimer)
  countdownTimer = setInterval(() => {
    confirmCountdown.value--
    if (confirmCountdown.value <= 0) {
      clearInterval(countdownTimer)
      countdownTimer = null
    }
  }, 1000)
}

const openAdminConfirm = (action) => {
  pendingAction.value = action
  showAdminConfirm.value = true
  startCountdown(3)
}

const cancelAdminConfirm = async () => {
  const wasRole = pendingAction.value?.type === 'role'
  showAdminConfirm.value = false
  pendingAction.value = null
  clearInterval(countdownTimer)
  countdownTimer = null
  confirmCountdown.value = 0
  // 改角色被取消 → 刷新列表回滚下拉显示
  if (wasRole) await load()
}

const confirmAdminAction = () => {
  if (confirmCountdown.value > 0) return
  const action = pendingAction.value
  // 关闭确认弹窗（动作执行后自带 load 刷新，不走取消的回滚逻辑）
  showAdminConfirm.value = false
  pendingAction.value = null
  clearInterval(countdownTimer)
  countdownTimer = null
  confirmCountdown.value = 0
  if (!action) return
  if (action.type === 'create') doCreateAccount()
  else if (action.type === 'role') doChangeRole(action.account, action.role)
}

// 倒计时归零瞬间给确认按钮一个脉冲动画（消除跳变感）
const pulseFlag = ref(false)
watch(confirmCountdown, (v, old) => {
  if (old === 1 && v === 0) {
    pulseFlag.value = false
    requestAnimationFrame(() => { pulseFlag.value = true })
    setTimeout(() => { pulseFlag.value = false }, 650)
  }
})

onBeforeUnmount(() => { clearInterval(countdownTimer) })

// ═══ 账户列表分页（显示全部账户，含多个 admin）═══
const PAGE_SIZE = 10
const accountPage = ref(1)
const accountTotalPages = computed(() => Math.max(1, Math.ceil(accounts.value.length / PAGE_SIZE)))
const pagedAccounts = computed(() => {
  const start = (accountPage.value - 1) * PAGE_SIZE
  return accounts.value.slice(start, start + PAGE_SIZE)
})
const accountPrev = () => { if (accountPage.value > 1) accountPage.value-- }
const accountNext = () => { if (accountPage.value < accountTotalPages.value) accountPage.value++ }

const error = ref('')
const success = ref('')

const flash = (msg, type = 'success') => {
  if (type === 'success') { success.value = msg; error.value = '' }
  else { error.value = msg; success.value = '' }
  setTimeout(() => { success.value = ''; error.value = '' }, 3000)
}

const load = async () => {
  try {
    const res = await apiRequest('/api/config/listen', { method: 'GET' })
    if (res.ok) {
      const data = await res.json()
      if (data.server) systemSettings.value.server = { port: data.server.port, host: data.server.host }
    }
  } catch { /* 静默 */ }
  try {
    const res = await apiRequest('/api/config/single-login', { method: 'GET' })
    if (res.ok) {
      const data = await res.json()
      if (data.singleLogin) singleLogin.value = { enabled: data.singleLogin.enabled === true }
    }
  } catch { /* 静默 */ }
  try {
    const res = await apiRequest('/api/auth/accounts', { method: 'GET' })
    if (res.ok) {
      accounts.value = (await res.json()).accounts || []
      // 删除/变更后可能超出有效页，回退到最后一页
      const maxPage = Math.max(1, Math.ceil(accounts.value.length / PAGE_SIZE))
      if (accountPage.value > maxPage) accountPage.value = maxPage
    }
  } catch { /* 静默 */ }
  try {
    const res = await apiRequest('/api/auth/accounts/linkable', { method: 'GET' })
    if (res.ok) linkableAccounts.value = (await res.json()).accounts || []
  } catch { /* 静默 */ }
}

const openAddModal = () => {
  showAddAccount.value = true
  addMode.value = 'linked'
  linkedSearch.value = ''
  pickerOpen.value = false
  accountForm.value = { username: '', password: '', role: 'subadmin', linkedTo: '' }
}

const saveListenCfg = async () => {
  try {
    const res = await post('/api/config/listen', { server: systemSettings.value.server })
    if (res.ok) flash('监听配置已保存（重启后生效）')
    else {
      const data = await res.json().catch(() => ({}))
      flash(data.error || '保存失败', 'error')
    }
  } catch (e) { flash(e.message, 'error') }
}

const saveSingleLogin = async () => {
  try {
    const res = await post('/api/config/single-login', { enabled: singleLogin.value.enabled })
    if (res.ok) flash('禁止多服登录设置已保存')
    else {
      const data = await res.json().catch(() => ({}))
      flash(data.error || '保存失败', 'error')
      // 保存失败回滚
      const r2 = await apiRequest('/api/config/single-login', { method: 'GET' })
      if (r2.ok) singleLogin.value = { enabled: (await r2.json()).singleLogin?.enabled === true }
    }
  } catch (e) { flash(e.message, 'error') }
}

// 创建入口：选择 admin 时先弹确认（警告 + 3 秒倒计时）
const createAccount = () => {
  if (addMode.value === 'manual' && (!accountForm.value.username || !accountForm.value.password)) {
    return flash('用户名和密码为必填', 'error')
  }
  if (addMode.value === 'linked' && !accountForm.value.linkedTo) {
    return flash('请选择要关联的 QQ 绑定用户', 'error')
  }
  if (accountForm.value.role === 'admin') {
    openAdminConfirm({ type: 'create' })
    return
  }
  doCreateAccount()
}

const doCreateAccount = async () => {
  const payload = {
    username: addMode.value === 'linked' ? '' : accountForm.value.username,
    password: addMode.value === 'linked' ? undefined : accountForm.value.password,
    role: accountForm.value.role,
    linkedTo: addMode.value === 'linked' ? accountForm.value.linkedTo : undefined
  }
  try {
    const res = await post('/api/auth/accounts', payload)
    if (res.ok) {
      flash('账户已创建')
      showAddAccount.value = false
      accountForm.value = { username: '', password: '', role: 'subadmin', linkedTo: '' }
      await load()
    } else {
      const data = await res.json().catch(() => ({}))
      flash(data.error || '创建失败', 'error')
    }
  } catch (e) { flash(e.message, 'error') }
}

const removeAccount = async (a) => {
  if (!confirm(`确定删除账户「${a.username}」？`)) return
  try {
    const res = await del(`/api/auth/accounts/${a.username}`)
    if (res.ok) { flash('账户已删除'); await load() }
    else {
      const data = await res.json().catch(() => ({}))
      flash(data.error || '删除失败', 'error')
    }
  } catch (e) { flash(e.message, 'error') }
}

const resetAccount = async (a) => {
  if (!confirm(`确定为「${a.username}」重置密码？重置后需使用新密码登录。`)) return
  try {
    const res = await post(`/api/auth/accounts/${a.username}/reset-password`, {})
    const data = await res.json()
    if (res.ok) {
      resetResult.value = { username: data.username, password: data.plainPassword }
      setTimeout(() => { resetResult.value = null }, 60000)
    } else flash(data.error || '重置失败', 'error')
  } catch (e) { flash(e.message, 'error') }
}

// 改角色入口：提升为 admin 时先弹确认
const changeRole = (a, role) => {
  if (role === 'admin') {
    openAdminConfirm({ type: 'role', account: a, role })
    return
  }
  doChangeRole(a, role)
}

const doChangeRole = async (a, role) => {
  try {
    const res = await post(`/api/auth/accounts/${a.username}/role`, { role })
    if (res.ok) { flash('角色已更新'); await load() }
    else {
      const data = await res.json().catch(() => ({}))
      flash(data.error || '更新失败', 'error')
    }
  } catch (e) { flash(e.message, 'error') }
}

onMounted(load)
</script>

<template>
  <div>
    <div v-if="success" class="flash success">{{ success }}</div>
    <div v-if="error" class="flash error">{{ error }}</div>

    <!-- ═══ 分页：监听设置 / 账户管理 ═══ -->
    <div class="settings-tabs">
      <button class="settings-tab" :class="{ active: settingsTab === 'listen' }" @click="settingsTab = 'listen'">监听设置</button>
      <button class="settings-tab" :class="{ active: settingsTab === 'accounts' }" @click="settingsTab = 'accounts'">账户管理</button>
    </div>

    <!-- Tab1 监听设置 -->
    <div v-if="settingsTab === 'listen'" class="sys-grid">
      <div class="sys-card">
        <h3>后端监听设置</h3>
        <div class="form-row">
          <label>监听端口</label>
          <input v-model.number="systemSettings.server.port" type="number" min="1" max="65535" />
        </div>
        <div class="form-row">
          <label>监听地址</label>
          <input v-model="systemSettings.server.host" placeholder="0.0.0.0" />
        </div>
        <p class="hint">监听端口/地址修改后需重启后端生效。服务器日志已由插件 SSE 常连实时回传，无需额外配置。</p>
        <button class="save-btn" @click="saveListenCfg">保存（重启生效）</button>
      </div>

      <div class="sys-card">
        <h3>禁止多服登录</h3>
        <p class="hint">启用后，玩家在某台服务器登录，将自动踢出其他启用了「上传与接收uuid」的服务器上同名的在线角色（未开启 uuid 同步的服务器不参与）。</p>
        <label class="switch-row">
          <span class="switch-label">启用</span>
          <input type="checkbox" class="switch-check" v-model="singleLogin.enabled" @change="saveSingleLogin" />
          <span class="switch-switch"></span>
        </label>
      </div>
    </div>

    <!-- Tab2 账户管理 -->
    <div v-else class="sys-grid">
      <div class="sys-card">
        <h3>后端账户管理</h3>
        <p class="hint">管理员可用用户名或 QQ 号 + 密码登录。可从现有 QQ 绑定中选取用户（密码与游戏同源），也可手动创建独立账号。</p>
        <div class="card-toolbar">
          <button class="add-btn small" @click="openAddModal">添加管理员</button>
        </div>
        <div class="account-table-wrap">
          <table class="account-table">
            <thead>
              <tr><th>用户名</th><th>角色</th><th>QQ</th><th>操作</th></tr>
            </thead>
            <tbody>
              <tr v-if="accounts.length === 0">
                <td colspan="4" class="account-empty">暂无账户</td>
              </tr>
              <tr v-for="a in pagedAccounts" :key="a.username">
                <td class="user-cell">
                  {{ a.username }}
                  <span v-if="a.linkedTo" class="tag-qq" title="密码由 QQ 绑定数据托管">QQ 关联</span>
                  <span v-if="isSelf(a)" class="tag-self">当前</span>
                </td>
                <td>
                  <select
                    class="role-select"
                    :class="a.role"
                    :value="a.role"
                    :disabled="isSelf(a)"
                    @change="changeRole(a, $event.target.value)"
                  >
                    <option value="admin">admin</option>
                    <option value="subadmin">subadmin</option>
                  </select>
                </td>
                <td class="qq-cell">{{ a.qq || '—' }}</td>
                <td class="op-cell">
                  <button
                    class="mini-btn"
                    :disabled="isSelf(a) || !!a.linkedTo"
                    :title="isSelf(a) ? '当前登录账户' : (a.linkedTo ? '密码由 QQ 绑定数据托管，请通过游戏内渠道修改' : '')"
                    @click="resetAccount(a)"
                  >重置密码</button>
                  <button
                    class="mini-btn danger"
                    :disabled="isSelf(a)"
                    :title="isSelf(a) ? '当前登录账户' : ''"
                    @click="removeAccount(a)"
                  >删除</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <!-- 分页 -->
        <div v-if="accounts.length > 0" class="account-pagination">
          <button @click="accountPrev" :disabled="accountPage <= 1">← 上一页</button>
          <span class="account-page-info">第 {{ accountPage }} / {{ accountTotalPages }} 页（共 {{ accounts.length }} 个账户）</span>
          <button @click="accountNext" :disabled="accountPage >= accountTotalPages">下一页 →</button>
        </div>
      </div>
    </div>

    <!-- 重置密码结果显示（一次） -->
    <div v-if="resetResult" class="reset-result">
      <h4>密码已重置（仅显示一次，请立即保存）</h4>
      <p>用户名：{{ resetResult.username }}</p>
      <p class="pwd">{{ resetResult.password }}</p>
      <button class="mini-btn" @click="resetResult = null">我记住了，关闭</button>
    </div>

    <!-- 添加账户弹窗 -->
    <div v-if="showAddAccount" class="modal-mask" @click.self="showAddAccount = false">
      <div class="modal">
        <div class="modal-head">
          <h3>添加管理员</h3>
          <button class="close-btn" @click="showAddAccount = false">✕</button>
        </div>
        <div class="modal-body">
          <!-- 添加方式 -->
          <div class="mode-tabs">
            <button class="mode-tab" :class="{ active: addMode === 'linked' }" @click="addMode = 'linked'">从 QQ 绑定中选取</button>
            <button class="mode-tab" :class="{ active: addMode === 'manual' }" @click="addMode = 'manual'">手动创建</button>
          </div>

          <div v-if="addMode === 'linked'" class="form-row">
            <label>从 QQ 绑定中选取（用所选用户的 QQ/角色名 + 密码登录，与游戏同源）</label>
            <div class="link-picker">
              <input
                v-model="linkedSearch"
                class="link-picker-input"
                type="text"
                placeholder="搜索名称或 QQ 号…"
                @focus="pickerOpen = true"
                @input="onLinkedSearchInput"
                @blur="onLinkedBlur"
              />
              <div v-if="pickerOpen" class="link-dropdown">
                <div
                  v-for="u in filteredLinkable"
                  :key="u.username"
                  class="link-option"
                  :class="{ selected: u.username === accountForm.linkedTo }"
                  @mousedown.prevent="selectLinkable(u)"
                >
                  <span class="link-name">{{ u.username }}</span>
                  <span v-if="u.qq" class="link-qq">{{ u.qq }}</span>
                </div>
                <div v-if="filteredLinkable.length === 0" class="link-empty">无匹配用户</div>
              </div>
            </div>
          </div>

          <template v-else>
            <div class="form-row">
              <label>用户名</label>
              <input v-model="accountForm.username" />
            </div>
            <div class="form-row">
              <label>初始密码（至少 8 位）</label>
              <input v-model="accountForm.password" type="password" />
            </div>
          </template>

          <div class="form-row">
            <label>角色</label>
            <select v-model="accountForm.role">
              <option value="subadmin">subadmin（服务器内操作，不含文件 / 后端配置 / 账户管理）</option>
              <option value="admin">admin（全部权限：账户管理 / 后端配置 / 文件 / 服务器）</option>
            </select>
          </div>

          <p v-if="addMode === 'linked' && linkableAccounts.length === 0" class="hint warn">暂无可选取的 QQ 绑定用户（全部已被授予管理身份）。</p>
          <p v-else class="hint">admin 可执行全部操作；subadmin 仅服务器内操作。被关联的绑定用户改密后，管理端登录密码随之同步。</p>

          <div class="modal-actions">
            <button class="mini-btn" @click="showAddAccount = false">取消</button>
            <button class="save-btn" @click="createAccount">创建</button>
          </div>
        </div>
      </div>
    </div>

    <!-- 授予 admin 确认弹窗（警告 + 3 秒倒计时） -->
    <div v-if="showAdminConfirm" class="modal-mask" @click.self="cancelAdminConfirm">
      <div class="modal confirm-modal">
        <div class="modal-head">
          <h3>授予管理员权限</h3>
          <button class="close-btn" @click="cancelAdminConfirm">✕</button>
        </div>
        <div class="modal-body">
          <p class="confirm-warn">
            将授予
            <b>{{ pendingAction?.type === 'role' ? pendingAction.account.username : (accountForm.username || '所选用户') }}</b>
            管理员（admin）角色：可管理后端账户、后端配置、文件与全部服务器权限。
          </p>
          <p class="hint">请确认该用户可信后再授予。确认按钮将在倒计时结束后可用。</p>
          <div class="modal-actions">
            <button class="mini-btn" @click="cancelAdminConfirm">取消</button>
            <button
              class="save-btn danger-confirm"
              :class="{ pulse: pulseFlag }"
              :disabled="confirmCountdown > 0"
              @click="confirmAdminAction"
            >
              <Transition name="count" mode="out-in">
                <span :key="confirmCountdown">{{ confirmCountdown > 0 ? `确认（${confirmCountdown}s）` : '确认' }}</span>
              </Transition>
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.flash { padding: 10px 14px; border-radius: 8px; margin-bottom: 12px; font-size: 0.9rem; }
.flash.success { background: rgba(34,197,94,.12); color: #22c55e; }
.flash.error { background: rgba(239,68,68,.12); color: #ef4444; }

.sys-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 14px; }
.settings-tabs { display: flex; gap: 8px; margin-bottom: 16px; }
.settings-tab {
  padding: 8px 18px; border-radius: 9px; border: 1px solid var(--border-color);
  background: var(--bg-card); color: var(--text-secondary); cursor: pointer; font-size: .88rem; font-weight: 600;
}
.settings-tab.active { background: linear-gradient(135deg, var(--accent-primary), #4f46e5); color: #fff; border-color: transparent; }
.sys-card { background: var(--bg-card); border: 1px solid var(--border-color); border-radius: 14px; padding: 20px; box-shadow: var(--shadow-sm); }
.sys-card h3 { margin: 0 0 14px; color: var(--text-primary); font-size: 1rem; }
.form-row { display: flex; flex-direction: column; gap: 5px; margin-bottom: 12px; }
.form-row label { font-size: .82rem; color: var(--text-muted); }
.form-row input, .form-row select {
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 8px 10px; border-radius: 8px; font-size: .9rem;
}
.form-row input:focus, .form-row select:focus { outline: none; border-color: var(--accent-primary); }
.save-btn {
  background: var(--accent-primary); color: #fff; border: none;
  padding: 8px 16px; border-radius: 8px; cursor: pointer; font-size: .88rem; font-weight: 600;
}
.save-btn:hover { opacity: .9; }
.save-btn:disabled { opacity: .5; cursor: not-allowed; }
.hint { font-size: .78rem; color: var(--text-muted); margin: 4px 0 10px; line-height: 1.5; }

/* 开关（禁止多服登录） */
.switch-row { display: flex; align-items: center; justify-content: space-between; gap: 8px; cursor: pointer; user-select: none; }
.switch-label { font-size: .88rem; color: var(--text-primary); font-weight: 600; }
.switch-check { position: absolute; opacity: 0; width: 0; height: 0; }
.switch-switch {
  position: relative; flex-shrink: 0;
  width: 38px; height: 21px; border-radius: 20px;
  background: var(--border-color); transition: background .2s ease;
}
.switch-switch::after {
  content: ''; position: absolute; top: 2px; left: 2px;
  width: 17px; height: 17px; border-radius: 50%;
  background: #fff; transition: transform .2s ease;
  box-shadow: 0 1px 3px rgba(0,0,0,.25);
}
.switch-check:checked + .switch-switch { background: var(--accent-primary); }
.switch-check:checked + .switch-switch::after { transform: translateX(17px); }
.add-btn { background: var(--accent-primary); color: #fff; border: none; padding: 10px 18px; border-radius: 9px; cursor: pointer; font-size: .92rem; font-weight: 600; }
.add-btn.small { font-size: 0.8rem; padding: 6px 12px; margin-bottom: 12px; }
.card-toolbar { display: flex; justify-content: flex-end; margin-bottom: 4px; }
.card-toolbar .add-btn.small { margin-bottom: 0; }

/* ═══ 账户表格美化 ═══ */
.account-table-wrap {
  border: 1px solid var(--border-color);
  border-radius: 12px;
  overflow: hidden;
  margin-top: 8px;
  background: var(--bg-card);
}
.account-table { width: 100%; border-collapse: collapse; font-size: .85rem; }
.account-table thead th {
  background: var(--bg-tertiary);
  color: var(--text-secondary);
  font-weight: 600;
  text-align: left;
  padding: 10px 14px;
  border-bottom: 1px solid var(--border-color);
  font-size: .76rem;
  letter-spacing: .03em;
  white-space: nowrap;
}
.account-table tbody td {
  padding: 10px 14px;
  border-bottom: 1px solid var(--border-color);
  vertical-align: middle;
}
.account-table tbody tr:last-child td { border-bottom: none; }
.account-table tbody tr:hover { background: rgba(99, 102, 241, .05); }
.account-table .user-cell { font-weight: 600; color: var(--text-primary); white-space: nowrap; }
.account-table .qq-cell { font-family: Consolas, Menlo, monospace; color: var(--text-secondary); }
.account-table select.role-select {
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  color: var(--text-primary);
  padding: 5px 10px;
  border-radius: 8px;
  font-size: .8rem;
  font-weight: 600;
  cursor: pointer;
  transition: border-color .2s ease;
}
.account-table select.role-select:disabled { opacity: .55; cursor: not-allowed; }
.account-table select.role-select.admin {
  border-color: rgba(239, 68, 68, .45);
  color: #f87171;
  background: rgba(239, 68, 68, .08);
}
.account-table select.role-select.subadmin {
  border-color: rgba(59, 130, 246, .45);
  color: #60a5fa;
  background: rgba(59, 130, 246, .08);
}
.account-empty { text-align: center; color: var(--text-muted); padding: 22px 0; }
.account-pagination { display: flex; align-items: center; justify-content: center; gap: 12px; margin-top: 14px; }
.account-pagination button {
  background: var(--accent-primary); color: #fff; border: none;
  padding: 6px 14px; border-radius: 8px; cursor: pointer; font-size: .8rem;
  transition: opacity .2s ease;
}
.account-pagination button:hover:not(:disabled) { opacity: .88; }
.account-pagination button:disabled { opacity: .45; cursor: not-allowed; }
.account-page-info { color: var(--text-muted); font-size: .8rem; }
.op-cell { display: flex; gap: 6px; white-space: nowrap; align-items: center; }
.mini-btn:disabled { opacity: .35; cursor: not-allowed; }
.mini-btn:disabled:hover { border-color: var(--border-color); color: var(--text-primary); }
.mini-btn.danger:disabled:hover { border-color: var(--border-color); color: var(--text-primary); }
.mini-btn {
  border: 1px solid var(--border-color); background: var(--bg-tertiary); color: var(--text-primary);
  padding: 5px 10px; border-radius: 7px; cursor: pointer; font-size: .8rem;
  transition: all .2s ease;
}
.mini-btn:hover { border-color: var(--accent-primary); color: var(--accent-primary); }
.mini-btn.danger:hover { border-color: #ef4444; color: #ef4444; }

/* 添加方式切换 */
.mode-tabs { display: flex; gap: 8px; margin-bottom: 14px; }
.mode-tab {
  flex: 1; padding: 8px 10px; border-radius: 9px; border: 1px solid var(--border-color);
  background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; font-size: .85rem; font-weight: 600;
  transition: all .2s ease;
}
.mode-tab.active { background: linear-gradient(135deg, var(--accent-primary), #4f46e5); color: #fff; border-color: transparent; }

/* ═══ 可搜索下拉（一个框）═══ */
.link-picker { position: relative; }
.link-picker-input { width: 100%; }
.link-dropdown {
  position: absolute; top: calc(100% + 4px); left: 0; right: 0; z-index: 60;
  max-height: 190px; overflow: auto;
  background: var(--bg-card); border: 1px solid var(--border-color);
  border-radius: 10px; box-shadow: var(--shadow-lg);
}
.link-option {
  display: flex; justify-content: space-between; align-items: center;
  padding: 8px 12px; cursor: pointer; font-size: .85rem; color: var(--text-primary);
}
.link-option:hover { background: var(--bg-hover); }
.link-option.selected { background: rgba(99, 102, 241, .12); color: var(--accent-primary); }
.link-name { font-weight: 600; }
.link-qq { color: var(--text-muted); font-family: Consolas, Menlo, monospace; font-size: .8rem; }
.link-empty { padding: 12px; text-align: center; color: var(--text-muted); font-size: .82rem; }
.tag-qq {
  display: inline-block; margin-left: 6px; padding: 1px 6px; border-radius: 6px;
  background: rgba(34, 211, 238, .14); color: #22d3ee; font-size: .7rem; vertical-align: middle;
}
.tag-self {
  display: inline-block; margin-left: 6px; padding: 1px 6px; border-radius: 6px;
  background: rgba(99, 102, 241, .14); color: var(--accent-primary); font-size: .7rem; vertical-align: middle;
}
.hint.warn { color: #f59e0b; }

/* ═══ admin 授予确认弹窗 ═══ */
.confirm-modal { width: 420px; }
.confirm-warn {
  margin: 0 0 10px;
  padding: 12px 14px;
  border-radius: 10px;
  background: rgba(239, 68, 68, .08);
  border: 1px solid rgba(239, 68, 68, .3);
  color: var(--text-primary);
  font-size: .9rem;
  line-height: 1.6;
}
.confirm-warn b { color: #f87171; }
.save-btn.danger-confirm { background: linear-gradient(135deg, #ef4444, #dc2626); transition: background .3s ease, opacity .3s ease, transform .3s ease; }
.save-btn.danger-confirm:disabled { opacity: .5; cursor: not-allowed; }

/* ═══ 倒计时数字过渡 + 归零脉冲 ═══ */
.count-enter-active, .count-leave-active { transition: all .25s ease; }
.count-enter-from { opacity: 0; transform: translateY(-6px); }
.count-leave-to { opacity: 0; transform: translateY(6px); }
.pulse { animation: pulse .55s ease; }
@keyframes pulse {
  0% { transform: scale(1); box-shadow: 0 0 0 0 rgba(239, 68, 68, .55); }
  50% { transform: scale(1.07); box-shadow: 0 0 0 9px rgba(239, 68, 68, 0); }
  100% { transform: scale(1); box-shadow: 0 0 0 0 rgba(239, 68, 68, 0); }
}

.reset-result {
  margin-top: 16px; background: rgba(99,102,241,.08); border: 1px solid var(--accent-primary);
  border-radius: 12px; padding: 18px;
}
.reset-result h4 { margin: 0 0 8px; color: var(--accent-primary); }
.reset-result p { margin: 4px 0; color: var(--text-primary); }
.reset-result .pwd { font-family: monospace; font-size: 1.1rem; color: var(--accent-primary); font-weight: 700; }

.modal-mask { position: fixed; inset: 0; background: rgba(0,0,0,.5); display: flex; align-items: center; justify-content: center; z-index: 200; }
.modal { background: var(--bg-card); border-radius: 14px; width: 440px; max-width: 92vw; max-height: 88vh; overflow: auto; box-shadow: var(--shadow-lg); }
.modal-head { display: flex; justify-content: space-between; align-items: center; padding: 16px 20px; border-bottom: 1px solid var(--border-color); }
.modal-head h3 { margin: 0; color: var(--text-primary); }
.close-btn { background: none; border: none; color: var(--text-muted); font-size: 1.1rem; cursor: pointer; }
.modal-body { padding: 20px; }
.modal-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 18px; }
</style>
