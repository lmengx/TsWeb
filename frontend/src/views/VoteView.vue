<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import forge from 'node-forge'
import { playerGet } from '../utils/playerApi.js'

const router = useRouter()

const loading = ref(false)
const loginError = ref('')
const me = ref(null)

const loginForm = ref({
  account: '',
  password: ''
})

const isLoggedIn = computed(() => !!me.value)

// ── 我的信息 ──
const loadMe = async () => {
  if (!localStorage.getItem('user_player')) return
  try {
    const res = await playerGet('/api/auth/player/me')
    const data = await res.json()
    if (data && data.username) {
      me.value = data
    } else {
      me.value = null
    }
  } catch (e) {
    me.value = null
  }
}

onMounted(loadMe)

// ── 登录（与管理端同构：RSA-OAEP 挑战，token 存 user_player，只查后端本地台账）──
const login = async () => {
  if (!loginForm.value.account.trim() || !loginForm.value.password) {
    loginError.value = '请输入 QQ 号 / 角色名和密码'
    return
  }

  loading.value = true
  loginError.value = ''

  try {
    const serverKeyResponse = await fetch('/api/auth/get-server-key')
    const serverKeyData = await serverKeyResponse.json()

    const clientKeys = forge.pki.rsa.generateKeyPair(2048)
    const clientPublicKeyPem = forge.pki.publicKeyToPem(clientKeys.publicKey)

    const serverPublicKey = forge.pki.publicKeyFromPem(serverKeyData.publicKey)
    const encryptedPassword = forge.util.encode64(serverPublicKey.encrypt(loginForm.value.password, 'RSA-OAEP'))

    const loginResponse = await fetch('/api/auth/player-login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        account: loginForm.value.account.trim(),
        encryptedPassword,
        clientPublicKeyPem,
        keyId: serverKeyData.keyId
      })
    })

    const loginResult = await loginResponse.json()

    if (!loginResult.success) {
      loginError.value = loginResult.error || '登录失败，请检查 QQ 号/角色名和密码'
      return
    }

    let token = loginResult.token
    if (loginResult.encryptedToken) {
      token = clientKeys.privateKey.decrypt(forge.util.decode64(loginResult.encryptedToken), 'RSA-OAEP')
    }

    // 玩家登录态只写 user_player，绝不触碰管理端 'user'（防覆盖/防越权）
    localStorage.setItem('user_player', JSON.stringify({
      username: loginResult.player?.username || loginForm.value.account.trim(),
      qq: loginResult.player?.qq || '',
      usergroup: loginResult.userGroup || 'player',
      token
    }))

    me.value = loginResult.player || { username: loginForm.value.account.trim() }
    loginForm.value.password = ''
  } catch (e) {
    console.error('Player login error:', e)
    loginError.value = '登录失败，请重试'
  } finally {
    loading.value = false
  }
}

const logout = () => {
  localStorage.removeItem('user_player')
  me.value = null
  loginForm.value = { account: '', password: '' }
}

const goHome = () => router.push('/')
</script>

<template>
  <div class="vote-page">
    <div class="vote-card">
      <div class="vote-header">
        <div class="vote-logo">
          <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 2l2.4 4.9 5.4.8-3.9 3.8.9 5.4L12 14.3 7.2 16.9l.9-5.4L4.2 7.7l5.4-.8L12 2z"></path>
          </svg>
        </div>
        <h1>玩家投票中心</h1>
        <p class="vote-sub">QQ 账号登录 · 仅查询本地绑定数据</p>
      </div>

      <!-- ═══ 未登录：登录表单 ═══ -->
      <form v-if="!isLoggedIn" @submit.prevent="login" class="vote-form">
        <div class="form-group">
          <label class="form-label">QQ 号 / 角色名</label>
          <input
            v-model="loginForm.account"
            type="text"
            placeholder="请输入绑定的 QQ 号或角色名"
            :disabled="loading"
            autocomplete="username"
          />
        </div>
        <div class="form-group">
          <label class="form-label">密码</label>
          <input
            v-model="loginForm.password"
            type="password"
            placeholder="请输入账号密码"
            :disabled="loading"
            autocomplete="current-password"
          />
        </div>

        <div v-if="loginError" class="status-message error">{{ loginError }}</div>

        <button type="submit" class="login-btn" :disabled="loading">
          <span v-if="loading" class="btn-spinner"></span>
          <span>{{ loading ? '登录中...' : '登录' }}</span>
        </button>
        <button type="button" class="back-btn" @click="goHome">返回首页</button>
      </form>

      <!-- ═══ 已登录：我的信息门户（初始页面） ═══ -->
      <div v-else class="me-panel">
        <div class="me-header">
          <div class="me-avatar">{{ (me.username || '?').slice(0, 1).toUpperCase() }}</div>
          <div class="me-identity">
            <div class="me-name">{{ me.username }}</div>
            <div class="me-qq">QQ：{{ me.qq || '—' }}</div>
          </div>
        </div>

        <div class="info-grid">
          <div class="info-item">
            <div class="info-label">累计游玩时长</div>
            <div class="info-value">
              <span class="num">{{ me.playtimeHours }}</span>
              <span class="unit">小时</span>
            </div>
          </div>
          <div class="info-item">
            <div class="info-label">当前投票权重</div>
            <div class="info-value">
              <span class="num">{{ me.weight }}</span>
              <span class="unit">票</span>
            </div>
          </div>
        </div>

        <div class="weight-rule">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="12" cy="12" r="10"></circle>
            <line x1="12" y1="8" x2="12" y2="12"></line>
            <line x1="12" y1="16" x2="12.01" y2="16"></line>
          </svg>
          权重规则：基础 1 票，累计游玩 ≥ {{ me.thresholdHours }} 小时加成 1 票（当前权重 {{ me.weight }} 票）
        </div>

        <div class="vote-placeholder">
          <div class="placeholder-icon">🗳️</div>
          <p class="placeholder-title">周期主题投票即将上线</p>
          <p class="placeholder-desc">下一次地图类型投票将在这里展示，敬请期待</p>
        </div>

        <button type="button" class="logout-btn" @click="logout">退出登录</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.vote-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 20px;
  background: linear-gradient(135deg, #e0e7ff, #c7d2fe, #a5b4fc, #c7d2fe, #e0e7ff);
  background-size: 400% 400%;
  animation: bgFlow 8s ease infinite;
  box-sizing: border-box;
}

@keyframes bgFlow {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

.vote-card {
  width: 100%;
  max-width: 440px;
  background: rgba(255, 255, 255, 0.88);
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  border-radius: 24px;
  padding: 40px 36px;
  box-shadow: 0 8px 40px rgba(99, 102, 241, 0.12);
  border: 1px solid rgba(255, 255, 255, 0.6);
  box-sizing: border-box;
}

.vote-header { text-align: center; margin-bottom: 28px; }
.vote-logo { color: #6366f1; display: flex; justify-content: center; margin-bottom: 12px; }
.vote-header h1 {
  margin: 0 0 6px;
  font-size: 1.6rem;
  font-weight: 800;
  background: linear-gradient(135deg, #4f46e5, #7c3aed);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}
.vote-sub { margin: 0; color: #6b7280; font-size: 0.85rem; }

.vote-form { display: flex; flex-direction: column; }
.form-group { margin-bottom: 18px; }
.form-label { display: block; color: #1e1b4b; font-size: 0.85rem; font-weight: 600; margin-bottom: 8px; }
.form-group input {
  width: 100%;
  padding: 12px 16px;
  background: white;
  border: 2px solid rgba(0, 0, 0, 0.1);
  border-radius: 10px;
  color: #0f0a3a;
  font-size: 0.95rem;
  transition: all 0.25s ease;
  box-sizing: border-box;
  outline: none;
}
.form-group input:focus { border-color: #6366f1; box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15); }
.form-group input:disabled { background: #f3f4f6; cursor: not-allowed; }

.status-message {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 10px 16px;
  border-radius: 10px;
  margin-bottom: 14px;
  font-size: 0.85rem;
  font-weight: 500;
}
.status-message.error { background: rgba(239, 68, 68, 0.1); color: #dc2626; border: 1px solid rgba(239, 68, 68, 0.2); }

.login-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  padding: 14px 24px;
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
  border: none;
  border-radius: 10px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.25);
}
.login-btn:hover:not(:disabled) { transform: translateY(-2px); box-shadow: 0 6px 20px rgba(99, 102, 241, 0.4); }
.login-btn:disabled { opacity: 0.6; cursor: not-allowed; }
.btn-spinner {
  width: 18px; height: 18px;
  border: 2px solid rgba(255, 255, 255, 0.3);
  border-top-color: #fff;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }

.back-btn {
  background: none;
  border: none;
  color: #6366f1;
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  padding: 10px 16px;
  border-radius: 8px;
  transition: all 0.2s ease;
  margin-top: 6px;
}
.back-btn:hover { background: rgba(99, 102, 241, 0.08); color: #4f46e5; }

/* ── 已登录信息面板 ── */
.me-panel { display: flex; flex-direction: column; }

.me-header {
  display: flex;
  align-items: center;
  gap: 14px;
  padding-bottom: 20px;
  border-bottom: 1px solid rgba(0, 0, 0, 0.06);
  margin-bottom: 20px;
}
.me-avatar {
  width: 52px; height: 52px;
  border-radius: 14px;
  background: linear-gradient(135deg, #6366f1, #8b5cf6);
  color: white;
  font-size: 1.4rem;
  font-weight: 800;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.me-name { font-size: 1.15rem; font-weight: 700; color: #0f0a3a; }
.me-qq { font-size: 0.82rem; color: #6b7280; margin-top: 3px; font-family: monospace; }

.info-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-bottom: 14px;
}
.info-item {
  background: rgba(99, 102, 241, 0.06);
  border: 1px solid rgba(99, 102, 241, 0.12);
  border-radius: 14px;
  padding: 16px;
  text-align: center;
}
.info-label { font-size: 0.78rem; color: #6b7280; font-weight: 500; margin-bottom: 8px; }
.info-value { color: #0f0a3a; }
.info-value .num {
  font-size: 1.7rem;
  font-weight: 800;
  background: linear-gradient(135deg, #6366f1, #8b5cf6);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  font-variant-numeric: tabular-nums;
}
.info-value .unit { font-size: 0.8rem; color: #6b7280; margin-left: 4px; }

.weight-rule {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 10px 12px;
  background: rgba(139, 92, 246, 0.06);
  border-radius: 10px;
  color: #7c3aed;
  font-size: 0.8rem;
  line-height: 1.5;
  margin-bottom: 16px;
}
.weight-rule svg { flex-shrink: 0; margin-top: 2px; }

.vote-placeholder {
  text-align: center;
  padding: 28px 16px;
  border: 2px dashed rgba(99, 102, 241, 0.2);
  border-radius: 14px;
  margin-bottom: 18px;
}
.placeholder-icon { font-size: 1.8rem; margin-bottom: 8px; }
.placeholder-title { margin: 0 0 6px; font-size: 0.95rem; font-weight: 700; color: #0f0a3a; }
.placeholder-desc { margin: 0; font-size: 0.82rem; color: #6b7280; }

.logout-btn {
  width: 100%;
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.06);
  border: 1px solid rgba(239, 68, 68, 0.2);
  border-radius: 10px;
  color: #dc2626;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
}
.logout-btn:hover { background: rgba(239, 68, 68, 0.12); }
</style>
