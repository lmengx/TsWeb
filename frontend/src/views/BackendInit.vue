<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const setupToken = ref(route.query.token || '')

// ═══════════════════════════════════════════════
// 状态
// ═══════════════════════════════════════════════
const loading = ref(true)
const tokenOk = ref(false)
const hasAccounts = ref(false)
const error = ref('')
const success = ref('')

// Token 认证（无 token / token 无效时输入）
const tokenMissing = ref(false)
const manualToken = ref('')
const authError = ref('')

// 设置密码表单
const username = ref('admin')          // 全局唯一 admin
const password = ref('')
const showPwd = ref(false)
const submitting = ref(false)

// ═══════════════════════════════════════════════
// 初始化
// ═══════════════════════════════════════════════
onMounted(async () => {
  if (!setupToken.value) {
    tokenMissing.value = true
    loading.value = false
    return
  }
  await checkToken(setupToken.value)
})

async function checkToken(t) {
  loading.value = true
  tokenMissing.value = false
  try {
    const res = await fetch('/api/setup/check?token=' + encodeURIComponent(t))
    const data = await res.json()
    if (data.needToken || !data.setupToken) {
      tokenMissing.value = true
      loading.value = false
      return
    }
    tokenOk.value = true
    hasAccounts.value = !!data.hasAccounts
    // 已存在账户：说明已初始化，引导去登录页
    if (hasAccounts.value) {
      router.replace('/login')
      return
    }
    loading.value = false
  } catch (err) {
    tokenMissing.value = true
    loading.value = false
  }
}

async function submitTokenAuth() {
  const t = manualToken.value.trim()
  if (!t) return
  authError.value = ''
  try {
    const res = await fetch('/api/setup/check?token=' + encodeURIComponent(t))
    const data = await res.json()
    if (data.needToken || !data.setupToken) {
      authError.value = 'Token 无效'
      return
    }
    setupToken.value = t
    const url = new URL(window.location.href)
    url.searchParams.set('token', t)
    window.location.href = url.toString()
  } catch (err) {
    authError.value = err.message
  }
}

// ═══════════════════════════════════════════════
// 密码强度
// ═══════════════════════════════════════════════
const passwordStrength = computed(() => {
  const p = password.value
  if (!p) return { level: 0, label: '', color: '' }
  let score = 0
  if (p.length >= 8) score++
  if (p.length >= 12) score++
  if (/[a-z]/.test(p) && /[A-Z]/.test(p)) score++
  if (/\d/.test(p)) score++
  if (/[^a-zA-Z0-9]/.test(p)) score++
  if (score <= 2) return { level: 1, label: '弱', color: '#ef4444' }
  if (score <= 3) return { level: 2, label: '中', color: '#f59e0b' }
  return { level: 3, label: '强', color: '#22c55e' }
})

// ═══════════════════════════════════════════════
// 创建 admin
// ═══════════════════════════════════════════════
async function submitCreateAdmin() {
  error.value = ''
  success.value = ''

  if (password.value.length < 8) {
    error.value = '密码长度至少 8 位'
    return
  }

  submitting.value = true
  try {
    const res = await fetch('/api/setup/create-admin', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        token: setupToken.value,
        username: username.value,
        password: password.value
      })
    })
    const data = await res.json()
    if (!data.success) {
      error.value = data.error || '创建失败'
      return
    }
    success.value = '管理员创建成功'

    // 后端已签发 JWT → 自动登录进入服务器管理页（用户选 C：设置密码→引导跳服务器管理页）
    if (data.token) {
      localStorage.setItem('user', JSON.stringify({
        username: data.username || 'admin',
        usergroup: 'admin',
        token: data.token
      }))
      setTimeout(() => {
        router.push('/console/servers')
      }, 800)
    } else {
      // 无 token（异常兜底）→ 跳登录页手动登录
      setTimeout(() => {
        router.push('/login')
      }, 1200)
    }
  } catch (err) {
    error.value = '请求失败: ' + err.message
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="init-page">
    <div class="init-container">

      <!-- 加载中 -->
      <div v-if="loading" class="loading-overlay">
        <div class="spinner"></div>
        <p>正在校验授权...</p>
      </div>

      <!-- Token 缺失 -->
      <div v-else-if="tokenMissing" class="token-auth-overlay">
        <div class="token-auth-card">
          <h2>需要授权</h2>
          <p class="auth-desc">请输入服务端控制台提供的 Token 以进行初始化配置</p>
          <div class="auth-input-row">
            <input v-model="manualToken" type="text" class="auth-input"
              placeholder="输入 Token..."
              @keydown="e => e.key === 'Enter' && submitTokenAuth()" />
            <button class="auth-btn" @click="submitTokenAuth" :disabled="!manualToken.trim()">
              验证
            </button>
          </div>
          <p v-if="authError" class="auth-error">{{ authError }}</p>
        </div>
      </div>

      <!-- Token 有效 & 无账户 → 设置管理员密码 -->
      <template v-else>
        <div class="init-header">
          <h1 class="init-title">初始化后台管理员</h1>
          <p class="init-subtitle">首次使用，请为唯一管理员账户设置登录密码</p>
        </div>

        <div v-if="error" class="msg-box error">{{ error }}</div>
        <div v-if="success" class="msg-box success">{{ success }}</div>

        <div class="section-card">
          <div class="form-row">
            <div class="form-group">
              <label class="form-label">管理员用户名</label>
              <input :value="username" type="text" class="form-input" disabled readonly />
            </div>
          </div>

          <div class="form-group">
            <label class="form-label">密码</label>
            <div class="input-with-btn">
              <input v-model="password" :type="showPwd ? 'text' : 'password'"
                class="form-input" autocomplete="new-password" placeholder="至少 8 位"
                @keydown="e => e.key === 'Enter' && submitCreateAdmin()" />
              <button class="toggle-btn" @click="showPwd = !showPwd" type="button">{{ showPwd ? '隐藏' : '显示' }}</button>
            </div>
            <div v-if="password" class="strength" :style="{ color: passwordStrength.color }">
              强度：{{ passwordStrength.label }}
              <span class="strength-bars">
                <span v-for="i in 3" :key="i" class="bar"
                  :class="{ filled: i <= passwordStrength.level }"
                  :style="{ background: i <= passwordStrength.level ? passwordStrength.color : 'transparent' }"></span>
              </span>
            </div>
          </div>

          <button class="btn primary" @click="submitCreateAdmin" :disabled="submitting">
            {{ submitting ? '创建中...' : '创建管理员并进入后台' }}
          </button>
          <p class="done-hint">创建成功后会自动登录，并引导你添加服务器</p>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
/* ═══════════════════════════════════════════════
   布局
   ═══════════════════════════════════════════════ */
.init-page {
  min-height: 100vh;
  background: linear-gradient(135deg, #0f0c29, #1a1740, #242150);
  color: #e2e8f0;
  display: flex;
  justify-content: center;
}

.init-container {
  max-width: 520px;
  width: 100%;
  padding: 48px 24px 80px;
}

/* ═══ 加载中 ═══ */
.loading-overlay {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 60vh;
  gap: 16px;
  color: #94a3b8;
  font-size: 0.9rem;
}
.spinner {
  width: 40px;
  height: 40px;
  border: 3px solid rgba(99, 102, 241, 0.2);
  border-top-color: #6366f1;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }

/* ═══ 顶部标题 ═══ */
.init-header {
  text-align: center;
  margin-bottom: 32px;
}
.init-title {
  margin: 0 0 8px;
  font-size: 1.5rem;
  font-weight: 700;
  color: #c7d2fe;
  letter-spacing: 1px;
}
.init-subtitle {
  margin: 0;
  font-size: 0.9rem;
  color: #64748b;
}

/* ═══ 卡片 ═══ */
.section-card {
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 16px;
  padding: 28px 24px;
}

.form-row { margin-bottom: 14px; }
.form-group { margin-bottom: 18px; }
.form-group:last-child { margin-bottom: 0; }

.form-label {
  display: block;
  font-size: 0.82rem;
  font-weight: 600;
  color: #94a3b8;
  margin-bottom: 6px;
}

.form-input {
  width: 100%;
  padding: 11px 14px;
  background: rgba(0, 0, 0, 0.3);
  border: 1.5px solid rgba(255, 255, 255, 0.1);
  border-radius: 10px;
  color: #e2e8f0;
  font-size: 0.92rem;
  outline: none;
  transition: border-color 0.25s ease;
  box-sizing: border-box;
}
.form-input:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15);
}
.form-input::placeholder { color: #475569; }
.form-input:disabled { opacity: 0.7; cursor: default; }

.field-hint {
  margin: 6px 0 0;
  font-size: 0.78rem;
  color: #64748b;
}
.field-hint code {
  background: rgba(99, 102, 241, 0.15);
  padding: 1px 6px;
  border-radius: 5px;
  color: #a5b4fc;
  font-size: 0.78rem;
}

.input-with-btn { display: flex; gap: 8px; }
.input-with-btn .form-input { flex: 1; }

.toggle-btn {
  padding: 8px 14px;
  background: rgba(255, 255, 255, 0.06);
  border: 1.5px solid rgba(255, 255, 255, 0.1);
  border-radius: 10px;
  color: #94a3b8;
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 500;
  white-space: nowrap;
  transition: all 0.2s ease;
}
.toggle-btn:hover { border-color: #6366f1; color: #a5b4fc; }

/* ═══ 密码强度 ═══ */
.strength {
  margin-top: 8px;
  font-size: 0.78rem;
  display: flex;
  align-items: center;
  gap: 8px;
}
.strength-bars { display: inline-flex; gap: 3px; }
.bar {
  width: 26px;
  height: 4px;
  border-radius: 2px;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

/* ═══ 按钮 ═══ */
.btn {
  width: 100%;
  padding: 12px 22px;
  border: none;
  border-radius: 10px;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
  margin-top: 8px;
}
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn.primary {
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: white;
}
.btn.primary:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.35);
}

.done-hint {
  text-align: center;
  margin: 14px 0 0;
  font-size: 0.8rem;
  color: #64748b;
}

/* ═══ 消息框 ═══ */
.msg-box {
  padding: 10px 14px;
  border-radius: 10px;
  font-size: 0.85rem;
  margin-bottom: 20px;
  line-height: 1.5;
}
.msg-box.error {
  background: rgba(239, 68, 68, 0.12);
  border: 1px solid rgba(239, 68, 68, 0.2);
  color: #f87171;
}
.msg-box.success {
  background: rgba(34, 197, 94, 0.12);
  border: 1px solid rgba(34, 197, 94, 0.2);
  color: #4ade80;
}

/* ═══ Token 认证 ═══ */
.token-auth-overlay {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 60vh;
}
.token-auth-card {
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 20px;
  padding: 48px 40px;
  text-align: center;
  max-width: 420px;
  width: 100%;
}
.token-auth-card h2 { color: #c7d2fe; margin: 0 0 8px; font-size: 1.3rem; }
.auth-desc {
  color: #64748b;
  margin: 0 0 24px;
  font-size: 0.9rem;
  line-height: 1.5;
}
.auth-input-row { display: flex; gap: 8px; }
.auth-input {
  flex: 1;
  padding: 12px 16px;
  background: rgba(0, 0, 0, 0.3);
  border: 1.5px solid rgba(255, 255, 255, 0.1);
  border-radius: 10px;
  color: #e2e8f0;
  font-size: 0.95rem;
  font-family: 'SF Mono', 'Cascadia Code', 'Consolas', monospace;
  outline: none;
  transition: border-color 0.25s ease;
}
.auth-input:focus { border-color: #6366f1; box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15); }
.auth-btn {
  padding: 12px 24px;
  border: none;
  border-radius: 10px;
  background: linear-gradient(135deg, #6366f1, #4f46e5);
  color: #fff;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
  transition: all 0.2s ease;
}
.auth-btn:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 4px 16px rgba(99, 102, 241, 0.3); }
.auth-btn:disabled { opacity: 0.4; cursor: default; }
.auth-error { color: #f87171; margin-top: 12px; font-size: 0.85rem; }
</style>
