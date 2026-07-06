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
    case 'user_not_found':
      return { type: 'error', text: '用户不存在，请检查用户名' }
    case 'wrong_password':
      return { type: 'error', text: '密码错误，请重新输入' }
    case 'success':
      return { type: 'success', text: '登录成功，正在跳转...' }
    case 'server_error':
      return { type: 'error', text: '服务器错误，请稍后重试' }
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
      if (loginResult.error === 'User not found') {
        loginStatus.value = 'user_not_found'
      } else if (loginResult.error === 'Wrong password') {
        loginStatus.value = 'wrong_password'
      } else {
        loginStatus.value = 'server_error'
      }
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
    <div class="login-card">
      <div class="login-header">
        <div class="login-logo">
          <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
            <line x1="3" y1="9" x2="21" y2="9"></line>
            <line x1="9" y1="21" x2="9" y2="9"></line>
          </svg>
        </div>
        <h1>TSWeb</h1>
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
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 40px 20px;
  background: linear-gradient(135deg, #e0e7ff, #c7d2fe, #a5b4fc, #c7d2fe, #e0e7ff);
  background-size: 400% 400%;
  animation: bgFlow 8s ease infinite;
}

@keyframes bgFlow {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

.login-card {
  width: 100%;
  max-width: 420px;
  background: rgba(255, 255, 255, 0.85);
  backdrop-filter: blur(20px);
  -webkit-backdrop-filter: blur(20px);
  border-radius: 24px;
  padding: 44px 40px;
  box-shadow: 0 8px 40px rgba(99, 102, 241, 0.12);
  border: 1px solid rgba(255, 255, 255, 0.6);
}

.login-header {
  text-align: center;
  margin-bottom: 32px;
}

.login-logo {
  color: #6366f1;
  display: flex;
  justify-content: center;
  margin-bottom: 12px;
}

.login-header h1 {
  margin: 0 0 4px;
  font-size: 1.8rem;
  font-weight: 800;
  background: linear-gradient(135deg, #4f46e5, #7c3aed);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.login-sub {
  margin: 0;
  color: #6b7280;
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
  color: #1e1b4b;
  font-size: 0.85rem;
  font-weight: 600;
  margin-bottom: 8px;
}

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

.form-group input:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.15);
}

.form-group input::placeholder {
  color: #9ca3af;
}

.form-group input.error {
  border-color: #ef4444;
  background: #fef2f2;
}

.form-group input.success {
  border-color: #22c55e;
  background: #f0fdf4;
}

.form-group input:disabled {
  background: #f3f4f6;
  cursor: not-allowed;
}

.status-message {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 10px 16px;
  border-radius: 10px;
  margin-bottom: 16px;
  font-size: 0.85rem;
  font-weight: 500;
  animation: slideIn 0.25s ease;
}

@keyframes slideIn {
  from { opacity: 0; transform: translateY(-8px); }
  to { opacity: 1; transform: translateY(0); }
}

.status-message.error {
  background: rgba(239, 68, 68, 0.1);
  color: #dc2626;
  border: 1px solid rgba(239, 68, 68, 0.2);
}

.status-message.success {
  background: rgba(22, 163, 74, 0.1);
  color: #16a34a;
  border: 1px solid rgba(22, 163, 74, 0.2);
}

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

.login-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 6px 20px rgba(99, 102, 241, 0.4);
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

@keyframes spin {
  to { transform: rotate(360deg); }
}

.login-footer {
  text-align: center;
  margin-top: 20px;
}

.back-btn {
  background: none;
  border: none;
  color: #6366f1;
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  padding: 8px 16px;
  border-radius: 8px;
  transition: all 0.2s ease;
}

.back-btn:hover {
  background: rgba(99, 102, 241, 0.08);
  color: #4f46e5;
}
</style>
