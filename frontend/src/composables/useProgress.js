import { ref } from 'vue'

// 全局路由进度状态（模块级单例，路由守卫与进度条组件共享）
const visible = ref(false)
const progress = ref(0)
let timer = null

export function useProgress() {
  const start = () => {
    visible.value = true
    progress.value = 8
    clearInterval(timer)
    // 渐近式推进：越来越慢地逼近 90%，避免跳动
    timer = setInterval(() => {
      progress.value = Math.min(90, progress.value + (90 - progress.value) * 0.12)
    }, 120)
  }

  const done = () => {
    clearInterval(timer)
    timer = null
    progress.value = 100
    // 停在 100% 短暂片刻再淡出，让用户感知"完成了"
    setTimeout(() => {
      visible.value = false
      progress.value = 0
    }, 350)
  }

  return { visible, progress, start, done }
}
