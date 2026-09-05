<script setup>
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Loading from '../components/Loading.vue'

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
    <Loading v-if="loading" text="正在跳转..." />
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
</style>
