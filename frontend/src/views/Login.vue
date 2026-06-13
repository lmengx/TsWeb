<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import forge from 'node-forge'

const router = useRouter()

const loading = ref(false)
const error = ref('')

const loginForm = ref({
  username: '',
  password: ''
})

const saveUserToStorage = (user) => {
  localStorage.setItem('user', JSON.stringify(user))
}

const login = async () => {
  if (!loginForm.value.username || !loginForm.value.password) {
    error.value = '请输入用户名和密码'
    return
  }

  loading.value = true
  error.value = ''
  
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
    
    if (loginResult.success) {
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
      router.push('/console')
    } else {
      error.value = loginResult.error || '登录失败'
    }
  } catch (errorMsg) {
    console.error('Login error:', errorMsg)
    error.value = '登录失败: ' + errorMsg.message
  }
  
  loading.value = false
}

const goHome = () => {
  router.push('/')
}
</script>

<template>
  <div class="login-page">
    <div class="login-container">
      <div class="login-header">
        <h1>🔐 登录</h1>
        <p>请输入您的账号信息</p>
      </div>
      
      <form @submit.prevent="login" class="login-form">
        <div class="form-group">
          <label for="username">用户名</label>
          <input
            id="username"
            v-model="loginForm.username"
            type="text"
            placeholder="请输入用户名"
            :disabled="loading"
          />
        </div>
        
        <div class="form-group">
          <label for="password">密码</label>
          <input
            id="password"
            v-model="loginForm.password"
            type="password"
            placeholder="请输入密码"
            :disabled="loading"
          />
        </div>
        
        <div v-if="error" class="error-message">
          {{ error }}
        </div>
        
        <button type="submit" class="login-btn" :disabled="loading">
          {{ loading ? '登录中...' : '登录' }}
        </button>
      </form>
      
      <div class="login-footer">
        <button @click="goHome" class="back-btn">返回首页</button>
      </div>
      
      <div class="security-info">
        <p>🔒 密码采用 RSA-OAEP 加密传输</p>
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
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  padding: 20px;
}

.login-container {
  background: white;
  padding: 40px;
  border-radius: 15px;
  box-shadow: 0 10px 40px rgba(0,0,0,0.2);
  width: 100%;
  max-width: 400px;
}

.login-header {
  text-align: center;
  margin-bottom: 30px;
}

.login-header h1 {
  margin: 0 0 10px 0;
  color: #333;
}

.login-header p {
  margin: 0;
  color: #666;
}

.login-form {
  display: flex;
  flex-direction: column;
}

.form-group {
  margin-bottom: 20px;
}

.form-group label {
  display: block;
  margin-bottom: 8px;
  color: #333;
  font-weight: 500;
}

.form-group input {
  width: 100%;
  padding: 12px;
  border: 2px solid #ddd;
  border-radius: 8px;
  font-size: 1rem;
  box-sizing: border-box;
  transition: border-color 0.3s;
}

.form-group input:focus {
  outline: none;
  border-color: #42b883;
}

.form-group input:disabled {
  background: #f5f5f5;
}

.error-message {
  color: #dc3545;
  padding: 10px;
  background: #f8d7da;
  border-radius: 6px;
  margin-bottom: 15px;
  text-align: center;
}

.login-btn {
  padding: 14px;
  background: linear-gradient(135deg, #42b883 0%, #379a6b 100%);
  color: white;
  border: none;
  border-radius: 8px;
  font-size: 1rem;
  font-weight: 500;
  cursor: pointer;
  transition: transform 0.2s, box-shadow 0.2s;
}

.login-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 5px 15px rgba(66, 184, 131, 0.4);
}

.login-btn:disabled {
  background: #ccc;
  cursor: not-allowed;
}

.login-footer {
  text-align: center;
  margin-top: 20px;
}

.back-btn {
  background: none;
  border: none;
  color: #42b883;
  cursor: pointer;
  font-size: 0.95rem;
  text-decoration: underline;
}

.back-btn:hover {
  color: #379a6b;
}

.security-info {
  text-align: center;
  margin-top: 25px;
  padding-top: 20px;
  border-top: 1px solid #eee;
}

.security-info p {
  margin: 0;
  color: #666;
  font-size: 0.85rem;
}
</style>
