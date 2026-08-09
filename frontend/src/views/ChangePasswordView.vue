<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { post } from '../utils/api.js'
import { getUserFromStorage } from '../utils/authHelper.js'

const route = useRoute()
const router = useRouter()

const forced = computed(() => route.query.forced === '1')
const currentUser = ref('')
const oldPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const loading = ref(false)
const error = ref('')
const success = ref('')
const showNew = ref(false)
const showConfirm = ref(false)

const passwordStrength = computed(() => {
  const p = newPassword.value
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

onMounted(() => {
  const user = getUserFromStorage()
  if (user?.username) {
    currentUser.value = user.username
  } else {
    router.push('/login')
  }
})

const submit = async () => {
  error.value = ''
  success.value = ''

  if (!forced.value && !oldPassword.value) {
    error.value = '请输入旧密码'
    return
  }
  if (newPassword.value.length < 8) {
    error.value = '新密码长度至少 8 位'
    return
  }
  if (newPassword.value !== confirmPassword.value) {
    error.value = '两次输入的新密码不一致'
    return
  }
  if (!forced.value && newPassword.value === oldPassword.value) {
    error.value = '新密码不能与旧密码相同'
    return
  }

  loading.value = true
  try {
    const body = { newPassword: newPassword.value }
    if (!forced.value) body.oldPassword = oldPassword.value

    const res = await post('/api/auth/change-password', body)
    const data = await res.json()

    if (res.ok) {
      success.value = '密码修改成功，请重新登录'
      setTimeout(() => {
        localStorage.removeItem('user')
        router.push('/login')
      }, 1500)
    } else {
      error.value = data.error || '修改失败'
    }
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

const cancel = () => {
  router.push('/console')
}
</script>

<template>
  <div class="cp-page">
    <div class="cp-card">
      <div class="cp-header">
        <div class="cp-logo">🔑</div>
        <h2>{{ forced ? '设置初始密码' : '修改密码' }}</h2>
        <p v-if="forced" class="cp-sub">首次登录或密码已被重置，请设置新密码后才能继续使用</p>
        <p v-else class="cp-sub">登录账户：{{ currentUser }}</p>
      </div>

      <div v-if="error" class="cp-msg error">{{ error }}</div>
      <div v-if="success" class="cp-msg success">{{ success }}</div>

      <div class="cp-form">
        <div v-if="!forced" class="cp-row">
          <label>旧密码</label>
          <input v-model="oldPassword" type="password" autocomplete="current-password" @keyup.enter="submit" />
        </div>

        <div class="cp-row">
          <label>新密码</label>
          <div class="pwd-input">
            <input v-model="newPassword" :type="showNew ? 'text' : 'password'" autocomplete="new-password" @keyup.enter="submit" />
            <span class="eye" @click="showNew = !showNew">{{ showNew ? '🙈' : '👁️' }}</span>
          </div>
          <div v-if="newPassword" class="strength" :style="{ color: passwordStrength.color }">
            强度：{{ passwordStrength.label }}
            <span class="strength-bars">
              <span v-for="i in 3" :key="i" class="bar" :class="{ filled: i <= passwordStrength.level }" :style="{ background: i <= passwordStrength.level ? passwordStrength.color : 'transparent' }"></span>
            </span>
          </div>
        </div>

        <div class="cp-row">
          <label>确认新密码</label>
          <div class="pwd-input">
            <input v-model="confirmPassword" :type="showConfirm ? 'text' : 'password'" autocomplete="new-password" @keyup.enter="submit" />
            <span class="eye" @click="showConfirm = !showConfirm">{{ showConfirm ? '🙈' : '👁️' }}</span>
          </div>
        </div>

        <button class="cp-submit" :disabled="loading" @click="submit">
          {{ loading ? '提交中...' : (forced ? '设置密码' : '确认修改') }}
        </button>
        <button v-if="!forced" class="cp-cancel" @click="cancel">暂不修改</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.cp-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--bg-primary, #0f0f1a);
  padding: 20px;
}
.cp-card {
  width: 400px;
  max-width: 94vw;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 16px;
  padding: 32px;
  box-shadow: var(--shadow-lg);
}
.cp-header { text-align: center; margin-bottom: 24px; }
.cp-logo { font-size: 2.4rem; margin-bottom: 8px; }
.cp-header h2 { margin: 0; color: var(--text-primary); font-size: 1.4rem; }
.cp-sub { margin-top: 8px; color: var(--text-muted); font-size: 0.85rem; }
.cp-msg { padding: 10px 12px; border-radius: 8px; font-size: 0.88rem; margin-bottom: 16px; }
.cp-msg.error { background: rgba(239,68,68,.12); color: #ef4444; }
.cp-msg.success { background: rgba(34,197,94,.12); color: #22c55e; }
.cp-row { margin-bottom: 16px; }
.cp-row label { display: block; font-size: 0.82rem; color: var(--text-muted); margin-bottom: 6px; }
.cp-row input {
  width: 100%; box-sizing: border-box;
  background: var(--bg-tertiary); border: 1px solid var(--border-color); color: var(--text-primary);
  padding: 10px 12px; border-radius: 9px; font-size: 0.95rem;
}
.cp-row input:focus { outline: none; border-color: var(--accent-primary); }
.pwd-input { position: relative; }
.pwd-input input { padding-right: 38px; }
.eye { position: absolute; right: 12px; top: 50%; transform: translateY(-50%); cursor: pointer; font-size: 0.9rem; }
.strength { margin-top: 6px; font-size: 0.78rem; display: flex; align-items: center; gap: 8px; }
.strength-bars { display: inline-flex; gap: 3px; }
.bar { width: 22px; height: 4px; border-radius: 2px; border: 1px solid var(--border-color); }
.cp-submit {
  width: 100%; padding: 12px; margin-top: 8px;
  background: var(--accent-primary); color: #fff; border: none; border-radius: 9px;
  font-size: 0.95rem; font-weight: 600; cursor: pointer;
}
.cp-submit:hover { opacity: 0.9; }
.cp-submit:disabled { opacity: 0.5; cursor: not-allowed; }
.cp-cancel {
  width: 100%; padding: 10px; margin-top: 8px;
  background: transparent; color: var(--text-muted); border: 1px solid var(--border-color);
  border-radius: 9px; font-size: 0.88rem; cursor: pointer;
}
.cp-cancel:hover { color: var(--text-primary); }
</style>
