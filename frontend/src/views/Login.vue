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
      return { type: 'error', icon: '👤', text: '用户不存在，请检查用户名' }
    case 'wrong_password':
      return { type: 'error', icon: '🔒', text: '密码错误，请重新输入' }
    case 'success':
      return { type: 'success', icon: '✅', text: '登录成功，正在跳转...' }
    case 'server_error':
      return { type: 'error', icon: '⚠️', text: '服务器错误，请稍后重试' }
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
      headers: {
        'Content-Type': 'application/json'
      },
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

const goHome = () => {
  router.push('/')
}
</script>

<template>
  <div class="login-page">
    <div class="bg-decoration">
      <div class="circle circle-1"></div>
      <div class="circle circle-2"></div>
      <div class="circle circle-3"></div>
      <div class="wave wave-1"></div>
      <div class="wave wave-2"></div>
    </div>
    
    <div class="login-container">
      <div class="login-header">
        <div class="logo-wrapper">
          <div class="logo-icon">⚡</div>
        </div>
        <h1>TsWeb 管理面板</h1>
        <p>请登录您的账号</p>
      </div>
      
      <form @submit.prevent="login" class="login-form">
        <div class="form-group">
          <label for="username">
            <span class="label-icon">👤</span>
            用户名
          </label>
          <div class="input-wrapper">
            <input
              id="username"
              v-model="loginForm.username"
              type="text"
              placeholder="请输入用户名"
              :disabled="loading"
              :class="{ 'error': loginStatus === 'user_not_found', 'success': loginStatus === 'success' }"
            />
            <span class="input-icon">👤</span>
          </div>
        </div>
        
        <div class="form-group">
          <label for="password">
            <span class="label-icon">🔒</span>
            密码
          </label>
          <div class="input-wrapper">
            <input
              id="password"
              v-model="loginForm.password"
              type="password"
              placeholder="请输入密码"
              :disabled="loading"
              :class="{ 'error': loginStatus === 'wrong_password', 'success': loginStatus === 'success' }"
            />
            <span class="input-icon">🔑</span>
          </div>
        </div>
        
        <div v-if="loginStatus === 'validation_error'" class="status-message error">
          <span class="status-icon">⚠️</span>
          <span>请填写用户名和密码</span>
        </div>
        
        <div v-else-if="statusMessage" class="status-message" :class="statusMessage.type">
          <span class="status-icon">{{ statusMessage.icon }}</span>
          <span>{{ statusMessage.text }}</span>
        </div>
        
        <button type="submit" class="login-btn" :disabled="loading">
          <span v-if="loading" class="btn-spinner"></span>
          <span>{{ loading ? '登录中...' : '登 录' }}</span>
        </button>
      </form>
      
      <div class="login-footer">
        <button @click="goHome" class="back-btn">
          <span>🏠</span>
          返回首页
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.login-page {
  min-height: 100vh;
  display: flex;
  justify-content: center;
  align-items: center;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 50%, #f093fb 100%);
  padding: 20px;
  position: relative;
  overflow: hidden;
}

.bg-decoration {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  pointer-events: none;
}

.circle {
  position: absolute;
  border-radius: 50%;
  opacity: 0.15;
}

.circle-1 {
  width: 400px;
  height: 400px;
  background: #fff;
  top: -100px;
  right: -100px;
  animation: float 6s ease-in-out infinite;
}

.circle-2 {
  width: 300px;
  height: 300px;
  background: #4ecdc4;
  bottom: -50px;
  left: -50px;
  animation: float 8s ease-in-out infinite reverse;
}

.circle-3 {
  width: 200px;
  height: 200px;
  background: #ffe66d;
  top: 50%;
  left: 20%;
  animation: float 5s ease-in-out infinite;
}

@keyframes float {
  0%, 100% { transform: translateY(0) scale(1); }
  50% { transform: translateY(-20px) scale(1.05); }
}

.wave {
  position: absolute;
  bottom: 0;
  left: 0;
  width: 200%;
  height: 100px;
  background: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1200 120' preserveAspectRatio='none'%3E%3Cpath d='M321.39,56.44c58-10.79,114.16-30.13,172-41.86,82.39-16.72,168.19-17.73,250.45-.39C823.78,31,906.67,72,985.66,92.83c70.05,18.48,146.53,26.09,214.34,3V120H0V95.8C66.12,105.76,136.09,106.5,206.38,91.63c60.26-14.87,116.26-30.33,166.49-49.49z' fill='%23ffffff' fill-opacity='0.1'/%3E%3C/svg%3E");
  background-repeat: repeat-x;
  animation: wave 10s linear infinite;
}

.wave-1 {
  animation-delay: 0s;
}

.wave-2 {
  animation-delay: -5s;
  opacity: 0.5;
}

@keyframes wave {
  0% { transform: translateX(0); }
  100% { transform: translateX(-50%); }
}

.login-container {
  background: rgba(255, 255, 255, 0.95);
  backdrop-filter: blur(10px);
  padding: 50px 45px;
  border-radius: 24px;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.15);
  width: 100%;
  max-width: 420px;
  position: relative;
  z-index: 1;
}

.login-header {
  text-align: center;
  margin-bottom: 35px;
}

.logo-wrapper {
  width: 80px;
  height: 80px;
  margin: 0 auto 20px;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  border-radius: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 8px 25px rgba(102, 126, 234, 0.4);
}

.logo-icon {
  font-size: 36px;
}

.login-header h1 {
  margin: 0 0 8px 0;
  color: #1a1a2e;
  font-size: 1.6rem;
  font-weight: 700;
}

.login-header p {
  margin: 0;
  color: #666;
  font-size: 0.95rem;
}

.login-form {
  display: flex;
  flex-direction: column;
}

.form-group {
  margin-bottom: 22px;
}

.form-group label {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;
  color: #333;
  font-weight: 600;
  font-size: 0.9rem;
}

.label-icon {
  font-size: 1rem;
}

.input-wrapper {
  position: relative;
}

.form-group input {
  width: 100%;
  padding: 15px 15px 15px 45px;
  border: 2px solid #e0e0e0;
  border-radius: 12px;
  font-size: 1rem;
  box-sizing: border-box;
  transition: all 0.3s ease;
  background: #fafafa;
}

.form-group input:focus {
  outline: none;
  border-color: #667eea;
  background: #fff;
  box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.1);
}

.form-group input.error {
  border-color: #e74c3c;
  background: #fef5f5;
}

.form-group input.success {
  border-color: #27ae60;
  background: #f0fdf4;
}

.form-group input:disabled {
  background: #f0f0f0;
  cursor: not-allowed;
}

.input-icon {
  position: absolute;
  left: 15px;
  top: 50%;
  transform: translateY(-50%);
  color: #999;
  font-size: 1rem;
  pointer-events: none;
}

.status-message {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 12px 16px;
  border-radius: 10px;
  margin-bottom: 20px;
  font-size: 0.9rem;
  animation: slideIn 0.3s ease;
}

@keyframes slideIn {
  from {
    opacity: 0;
    transform: translateY(-10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.status-message.error {
  background: #fef2f2;
  color: #dc2626;
  border: 1px solid #fee2e2;
}

.status-message.success {
  background: #f0fdf4;
  color: #16a34a;
  border: 1px solid #bbf7d0;
}

.status-icon {
  font-size: 1.1rem;
}

.login-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  padding: 16px;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  border: none;
  border-radius: 12px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.3s ease;
  box-shadow: 0 4px 15px rgba(102, 126, 234, 0.4);
}

.login-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 6px 20px rgba(102, 126, 234, 0.5);
}

.login-btn:disabled {
  background: #ccc;
  cursor: not-allowed;
  box-shadow: none;
}

.btn-spinner {
  width: 20px;
  height: 20px;
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
  margin-top: 25px;
}

.back-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: none;
  border: none;
  color: #667eea;
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 500;
  padding: 10px 20px;
  border-radius: 8px;
  transition: all 0.3s ease;
}

.back-btn:hover {
  background: rgba(102, 126, 234, 0.1);
  color: #5a6fd6;
}
</style>