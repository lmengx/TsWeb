<script setup>
import { ref, onMounted } from 'vue'

const status = ref(null)
const loading = ref(true)

const checkStatus = async () => {
  loading.value = true
  try {
    const response = await fetch('/api/status')
    const result = await response.json()
    status.value = result
    if (result.connected) {
      window.location.href = '/login'
    }
  } catch (err) {
    status.value = { connected: false, message: '无法连接到服务器' }
  }
  loading.value = false
}

onMounted(() => {
  checkStatus()
  const interval = setInterval(() => {
    checkStatus()
  }, 5000)
})
</script>

<template>
  <div class="error-page">
    <div class="stars"></div>
    <div class="error-container">
      <div class="error-icon-wrapper">
        <div class="error-icon">
          <svg viewBox="0 0 120 120" class="icon-svg">
            <circle cx="60" cy="60" r="50" fill="none" stroke="#ff6b6b" stroke-width="4" class="icon-circle"/>
            <path d="M45 45 L75 75 M75 45 L45 75" stroke="#ff6b6b" stroke-width="4" stroke-linecap="round"/>
            <circle cx="60" cy="60" r="25" fill="#ff6b6b" class="icon-inner"/>
            <path d="M50 60 L55 65 L70 50" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" fill="none"/>
          </svg>
        </div>
        <div class="pulse-ring"></div>
      </div>
      
      <h1 class="error-title">服务器连接失败</h1>
      <p class="error-message">{{ status?.message || '服务器对接失败，请联系管理员配置后重启服务' }}</p>
      
      <div class="auto-check">
        <div class="spinner" v-if="loading">
          <div class="spinner-inner"></div>
        </div>
        <span class="auto-check-icon" v-else></span>
        <span>系统正在自动重试连接...</span>
      </div>
      
      <div class="footer">
        <p>TsWeb Admin Panel</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.error-page {
  min-height: 100vh;
  display: flex;
  justify-content: center;
  align-items: center;
  background: linear-gradient(135deg, #1a1a2e 0%, #16213e 50%, #0f3460 100%);
  padding: 20px;
  position: relative;
  overflow: hidden;
}

.stars {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-image: 
    radial-gradient(2px 2px at 20px 30px, #fff, transparent),
    radial-gradient(2px 2px at 40px 70px, rgba(255,255,255,0.8), transparent),
    radial-gradient(1px 1px at 90px 40px, #fff, transparent),
    radial-gradient(2px 2px at 160px 120px, rgba(255,255,255,0.9), transparent),
    radial-gradient(1px 1px at 230px 80px, #fff, transparent),
    radial-gradient(2px 2px at 300px 150px, rgba(255,255,255,0.7), transparent),
    radial-gradient(1px 1px at 370px 200px, #fff, transparent),
    radial-gradient(2px 2px at 450px 50px, rgba(255,255,255,0.8), transparent),
    radial-gradient(1px 1px at 520px 180px, #fff, transparent),
    radial-gradient(2px 2px at 600px 100px, rgba(255,255,255,0.9), transparent);
  background-size: 650px 250px;
  animation: twinkle 3s ease-in-out infinite;
}

@keyframes twinkle {
  0%, 100% { opacity: 0.4; }
  50% { opacity: 0.8; }
}

.error-container {
  background: rgba(255, 255, 255, 0.05);
  backdrop-filter: blur(20px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  padding: 50px 40px;
  border-radius: 24px;
  text-align: center;
  max-width: 480px;
  width: 100%;
  position: relative;
  z-index: 1;
  box-shadow: 
    0 8px 32px rgba(0, 0, 0, 0.3),
    inset 0 1px 0 rgba(255, 255, 255, 0.1);
}

.error-icon-wrapper {
  position: relative;
  width: 140px;
  height: 140px;
  margin: 0 auto 30px;
}

.error-icon {
  position: relative;
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2;
}

.icon-svg {
  width: 100px;
  height: 100px;
  animation: iconShake 0.5s ease-in-out;
}

@keyframes iconShake {
  0%, 100% { transform: translateX(0); }
  25% { transform: translateX(-5px); }
  75% { transform: translateX(5px); }
}

.icon-circle {
  animation: circlePulse 2s ease-in-out infinite;
}

@keyframes circlePulse {
  0%, 100% { stroke-width: 4; opacity: 1; }
  50% { stroke-width: 6; opacity: 0.6; }
}

.icon-inner {
  animation: innerPulse 2s ease-in-out infinite;
}

@keyframes innerPulse {
  0%, 100% { transform: scale(1); opacity: 1; }
  50% { transform: scale(1.1); opacity: 0.8; }
}

.pulse-ring {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  width: 120px;
  height: 120px;
  border: 3px solid #ff6b6b;
  border-radius: 50%;
  animation: ringPulse 2s ease-out infinite;
}

@keyframes ringPulse {
  0% {
    transform: translate(-50%, -50%) scale(0.8);
    opacity: 1;
  }
  100% {
    transform: translate(-50%, -50%) scale(1.5);
    opacity: 0;
  }
}

.error-title {
  margin: 0 0 16px 0;
  color: #fff;
  font-size: 1.9rem;
  font-weight: 600;
  letter-spacing: 1px;
}

.error-message {
  margin: 0 0 35px 0;
  color: rgba(255, 255, 255, 0.7);
  font-size: 1rem;
  line-height: 1.6;
}

.auto-check {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  padding: 20px;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 12px;
  margin-bottom: 20px;
  color: rgba(255, 255, 255, 0.6);
  font-size: 0.9rem;
}

.spinner {
  width: 20px;
  height: 20px;
  position: relative;
}

.spinner-inner {
  width: 100%;
  height: 100%;
  border: 2px solid rgba(255, 255, 255, 0.2);
  border-top-color: #4ecdc4;
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.auto-check-icon {
  width: 8px;
  height: 8px;
  background: #4ecdc4;
  border-radius: 50%;
  animation: blink 1.5s ease-in-out infinite;
}

@keyframes blink {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}

.footer {
  padding-top: 20px;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
}

.footer p {
  margin: 0;
  color: rgba(255, 255, 255, 0.3);
  font-size: 0.8rem;
}
</style>