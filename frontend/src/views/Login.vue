<script setup>
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import forge from 'node-forge'

const router = useRouter()

const loading = ref(false)
const loginStatus = ref(null)

const loginForm = ref({
  username: '',
  password: ''
})

const statusMessage = computed(() => {
  switch (loginStatus.value) {
    case 'validation_error':
      return { type: 'error', text: '请输入用户名和密码' }
    case 'success':
      return { type: 'success', text: '登录成功，正在跳转...' }
    case 'server_error':
      return { type: 'error', text: '用户名或密码错误' }
    default:
      return null
  }
})

const saveUserToStorage = (user) => {
  localStorage.setItem('user', JSON.stringify(user))
}

const login = async () => {
  if (!loginForm.value.username || !loginForm.value.password) {
    loginStatus.value = 'validation_error'
    return
  }

  loading.value = true
  loginStatus.value = null
  
  try {
    const serverKeyResponse = await fetch('/api/auth/get-server-key')
    const serverKeyData = await serverKeyResponse.json()
    
    const clientKeys = forge.pki.rsa.generateKeyPair(2048)
    const clientPublicKeyPem = forge.pki.publicKeyToPem(clientKeys.publicKey)
    
    const serverPublicKey = forge.pki.publicKeyFromPem(serverKeyData.publicKey)
    const encryptedPassword = forge.util.encode64(serverPublicKey.encrypt(loginForm.value.password, 'RSA-OAEP'))
    
    const loginResponse = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username: loginForm.value.username,
        encryptedPassword,
        clientPublicKeyPem,
        keyId: serverKeyData.keyId
      })
    })
    
    const loginResult = await loginResponse.json()
    
    if (loginResult.redirect || loginResult.error === 'Server not connected') {
      router.push('/error/server')
      return
    }
    
    if (loginResult.success) {
      loginStatus.value = 'success'
      
      let token = loginResult.token
      if (loginResult.encryptedToken) {
        token = clientKeys.privateKey.decrypt(forge.util.decode64(loginResult.encryptedToken), 'RSA-OAEP')
      }
      
      const userData = {
        username: loginForm.value.username,
        usergroup: loginResult.userGroup || 'default',
        token
      }
      
      saveUserToStorage(userData)

      setTimeout(() => {
        router.push('/console')
      }, 1500)
    } else {
      loginStatus.value = 'server_error'
    }
  } catch (errorMsg) {
    console.error('Login error:', errorMsg)
    loginStatus.value = 'server_error'
  }
  
  loading.value = false
}

const goHome = () => { router.push('/') }
</script>

<template>
  <div class="login-page">
    <div class="tech-bg"></div>
    <div class="login-orb orb-1"></div>
    <div class="login-orb orb-2"></div>

    <div class="login-card glass">
      <div class="login-header">
        <div class="login-logo">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
            <line x1="3" y1="9" x2="21" y2="9"></line>
            <line x1="9" y1="21" x2="9" y2="9"></line>
          </svg>
        </div>
        <h1 class="text-gradient">TSWeb</h1>
        <p class="login-sub">管理面板登录</p>
      </div>

      <form @submit.prevent="login" class="login-form">
        <div class="form-group">
          <label class="form-label">用户名</label>
          <input
            v-model="loginForm.username"
            type="text"
            placeholder="请输入用户名"
            :disabled="loading"
            :class="{ error: loginStatus === 'user_not_found', success: loginStatus === 'success' }"
          />
        </div>

        <div class="form-group">
          <label class="form-label">密码</label>
          <input
            v-model="loginForm.password"
            type="password"
            placeholder="请输入密码"
            :disabled="loading"
            :class="{ error: loginStatus === 'wrong_password', success: loginStatus === 'success' }"
          />
        </div>

        <div v-if="loginStatus === 'validation_error'" class="status-message error">
          请填写用户名和密码
        </div>
        <div v-else-if="statusMessage" class="status-message" :class="statusMessage.type">
          {{ statusMessage.text }}
        </div>

        <button type="submit" class="login-btn" :disabled="loading">
          <span v-if="loading" class="btn-spinner"></span>
          <span>{{ loading ? '登录中...' : '登录' }}</span>
        </button>
      </form>

      <div class="login-footer">
        <button @click="goHome" class="back-btn">返回首页</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.login-page {
  position: relative;
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 20px;
  background-color: var(--bg-base);
  overflow: hidden;
}

/* 漂浮光球 */
.login-orb {
  position: fixed;
  border-radius: 50%;
  filter: blur(90px);
  pointer-events: none;
  z-index: 0;
  opacity: 0.55;
}
.orb-1 {
  width: 460px;
  height: 460px;
  top: -140px;
  right: -120px;
  background: radial-gradient(circle, rgba(99, 102, 241, 0.5), transparent 70%);
  animation: float 14s ease-in-out infinite;
}
.orb-2 {
  width: 420px;
  height: 420px;
  bottom: -160px;
  left: -140px;
  background: radial-gradient(circle, rgba(34, 211, 238, 0.35), transparent 70%);
  animation: float 18s ease-in-out infinite reverse;
}

.login-card {
  position: relative;
  z-index: 1;
  width: 100%;
  max-width: 420px;
  border-radius: var(--radius-xl);
  padding: 44px 40px;
  box-shadow: var(--shadow-lg);
  animation: fadeUp 0.6s var(--ease-out);
}

.login-header {
  text-align: center;
  margin-bottom: 32px;
}

.login-logo {
  width: 52px;
  height: 52px;
  margin: 0 auto 14px;
  border-radius: 14px;
  background: var(--gradient-primary);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: var(--glow-primary);
}

.login-header h1 {
  margin: 0 0 6px;
  font-size: 1.9rem;
  font-weight: 800;
  letter-spacing: 0.5px;
}

.login-sub {
  margin: 0;
  color: var(--text-muted);
  font-size: 0.9rem;
}

.login-form {
  display: flex;
  flex-direction: column;
}

.form-group {
  margin-bottom: 20px;
}

.form-label {
  display: block;
  color: var(--text-secondary);
  font-size: 0.85rem;
  font-weight: 600;
  margin-bottom: 8px;
}

.form-group input {
  width: 100%;
  padding: 12px 16px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 0.95rem;
  transition: all 0.25s var(--ease-out);
  box-sizing: border-box;
  outline: none;
}

.form-group input:focus {
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.18), var(--glow-primary);
}

.form-group input::placeholder {
  color: var(--text-muted);
}

.form-group input.error {
  border-color: var(--accent-error);
  background: rgba(244, 63, 94, 0.06);
}

.form-group input.success {
  border-color: var(--accent-secondary);
  background: rgba(16, 185, 129, 0.06);
}

.form-group input:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.status-message {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 10px 16px;
  border-radius: var(--radius-sm);
  margin-bottom: 16px;
  font-size: 0.85rem;
  font-weight: 500;
  animation: fadeUp 0.25s var(--ease-out);
}

.status-message.error {
  background: rgba(244, 63, 94, 0.1);
  color: #fda4af;
  border: 1px solid rgba(244, 63, 94, 0.25);
}

.status-message.success {
  background: rgba(16, 185, 129, 0.1);
  color: #6ee7b7;
  border: 1px solid rgba(16, 185, 129, 0.25);
}

.login-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  padding: 14px 24px;
  background: var(--gradient-primary);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  font-size: 1rem;
  font-weight: 700;
  cursor: pointer;
  transition: all 0.25s var(--ease-out);
  box-shadow: 0 4px 16px rgba(99, 102, 241, 0.35);
}

.login-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 8px 28px rgba(99, 102, 241, 0.5), var(--glow-primary);
}

.login-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
  box-shadow: none;
  transform: none;
}

.btn-spinner {
  width: 18px;
  height: 18px;
  border: 2px solid rgba(255, 255, 255, 0.3);
  border-top-color: #fff;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

.login-footer {
  text-align: center;
  margin-top: 24px;
}

.back-btn {
  background: none;
  border: none;
  color: var(--text-secondary);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  padding: 8px 16px;
  border-radius: var(--radius-sm);
  transition: all 0.2s ease;
}

.back-btn:hover {
  background: var(--bg-hover);
  color: var(--accent-primary);
}

/* 亮色主题适配 */
:global(.light) .status-message.error {
  color: #dc2626;
}
:global(.light) .status-message.success {
  color: #16a34a;
}
</style>
