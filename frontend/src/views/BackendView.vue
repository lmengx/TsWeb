<script setup>
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()
const setupToken = ref(route.query.token || '')

const loading = ref(true)

onMounted(async () => {
  try {
    // 检测当前是否已登录
    const user = localStorage.getItem('user')
    if (user) {
      // 已登录 → 直接进后台服务器管理
      router.replace('/console/servers')
      return
    }

    // 未登录：有 setup token → 校验
    if (setupToken.value) {
      const res = await fetch('/api/setup/check?token=' + encodeURIComponent(setupToken.value))
      const data = await res.json()
      if (!data.needToken && data.setupToken) {
        if (data.hasAccounts === false) {
          // 无账户 → 引导设置管理员密码
          router.replace('/backend/init?token=' + encodeURIComponent(setupToken.value))
          return
        }
        // 已有账户 → 登录页
        router.replace('/login')
        return
      }
    }

    // 无 token / token 无效 → 登录页
    router.replace('/login')
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="backend-redirect-page">
    <div v-if="loading" class="loading-box">
      <div class="spinner"></div>
      <p>正在跳转...</p>
    </div>
  </div>
</template>

<style scoped>
.backend-redirect-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #0f0c29, #1a1740, #242150);
  color: #94a3b8;
}
.loading-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
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
</style>
