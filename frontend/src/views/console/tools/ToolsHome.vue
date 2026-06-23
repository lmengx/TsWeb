<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { isAdmin } from '../../../utils/authHelper.js'

const router = useRouter()
const isAdminUser = ref(false)

onMounted(() => {
  isAdminUser.value = isAdmin()
})

const goToTool = (path) => {
  router.push(path)
}
</script>

<template>
  <div class="tools-home">
    <div class="bg-pattern"></div>
    <div class="bg-gradient"></div>
    
    <div class="content">
      <div class="logo-section">
        <div class="logo-icon">
          <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <polygon points="12 2 2 7 12 12 22 7 12 2"></polygon>
            <polyline points="2 17 12 22 22 17"></polyline>
            <polyline points="2 12 12 17 22 12"></polyline>
          </svg>
        </div>
        <h1 class="logo-text">T<span class="highlight">S</span>Web</h1>
      </div>
      
      <div class="tools-grid">
        <button class="tool-btn" @click="goToTool('/console/tools/item-search')">
          <div class="btn-icon">
            <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="11" cy="11" r="8"></circle>
              <line x1="21" x2="16.65" y1="21" y2="16.65"></line>
            </svg>
          </div>
          <span class="btn-text">物品查询</span>
          <span class="btn-sub">Item Search</span>
        </button>
        
        <button class="tool-btn" @click="goToTool('/console/tools/resources')">
          <div class="btn-icon">
            <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
              <polyline points="7 10 12 15 17 10"></polyline>
              <line x1="12" x2="12" y1="15" y2="3"></line>
            </svg>
          </div>
          <span class="btn-text">资源下载</span>
          <span class="btn-sub">Resources</span>
        </button>
        
        <button v-if="isAdminUser" class="tool-btn admin" @click="goToTool('/console/terminal')">
          <div class="btn-icon">
            <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <polyline points="4 17 10 11 4 5"></polyline>
              <line x1="12" x2="20" y1="19" y2="19"></line>
            </svg>
          </div>
          <span class="btn-text">控制台</span>
          <span class="btn-sub">Terminal</span>
        </button>
        
        <button v-if="isAdminUser" class="tool-btn admin" @click="goToTool('/console/players')">
          <div class="btn-icon">
            <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"></path>
              <circle cx="9" cy="7" r="4"></circle>
              <path d="M23 21v-2a4 4 0 0 0-3-3.87"></path>
              <path d="M16 3.13a4 4 0 0 1 0 7.75"></path>
            </svg>
          </div>
          <span class="btn-text">玩家管理</span>
          <span class="btn-sub">Players</span>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.tools-home {
  position: relative;
  min-height: calc(100vh - 60px);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.bg-pattern {
  position: absolute;
  inset: 0;
  background-image: 
    radial-gradient(circle at 25% 25%, rgba(99, 102, 241, 0.08) 0%, transparent 50%),
    radial-gradient(circle at 75% 75%, rgba(139, 92, 246, 0.08) 0%, transparent 50%),
    radial-gradient(circle at 50% 50%, rgba(236, 72, 153, 0.05) 0%, transparent 70%);
  background-size: 100% 100%;
}

.bg-gradient {
  position: absolute;
  inset: 0;
  background: 
    linear-gradient(135deg, transparent 0%, rgba(99, 102, 241, 0.03) 50%, transparent 100%),
    linear-gradient(225deg, transparent 0%, rgba(139, 92, 246, 0.03) 50%, transparent 100%);
  animation: gradientMove 15s ease-in-out infinite;
}

@keyframes gradientMove {
  0%, 100% {
    transform: translateX(0) translateY(0);
  }
  50% {
    transform: translateX(-20px) translateY(-10px);
  }
}

.content {
  position: relative;
  z-index: 10;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 60px;
  padding: 40px;
}

.logo-section {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
}

.logo-icon {
  width: 80px;
  height: 80px;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.2), rgba(139, 92, 246, 0.2));
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--accent-primary);
  backdrop-filter: blur(20px);
}

.logo-text {
  font-size: 2.5rem;
  font-weight: 800;
  color: var(--text-primary);
  margin: 0;
  letter-spacing: -1px;
}

.logo-text .highlight {
  background: linear-gradient(135deg, var(--accent-primary), #ec4899);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.tools-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 20px;
  max-width: 500px;
}

.tool-btn {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  width: 200px;
  height: 160px;
  padding: 24px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 20px;
  cursor: pointer;
  transition: all 0.4s cubic-bezier(0.4, 0, 0.2, 1);
  position: relative;
  overflow: hidden;
}

.tool-btn::before {
  content: '';
  position: absolute;
  inset: 0;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.1), rgba(139, 92, 246, 0.1));
  opacity: 0;
  transition: opacity 0.4s ease;
}

.tool-btn:hover::before {
  opacity: 1;
}

.tool-btn:hover {
  transform: translateY(-4px);
  border-color: rgba(99, 102, 241, 0.4);
  box-shadow: 
    0 20px 40px rgba(0, 0, 0, 0.3),
    0 0 60px rgba(99, 102, 241, 0.1);
}

.tool-btn.admin:hover {
  border-color: rgba(236, 72, 153, 0.4);
  box-shadow: 
    0 20px 40px rgba(0, 0, 0, 0.3),
    0 0 60px rgba(236, 72, 153, 0.1);
}

.tool-btn.admin::before {
  background: linear-gradient(135deg, rgba(236, 72, 153, 0.1), rgba(167, 139, 250, 0.1));
}

.btn-icon {
  width: 60px;
  height: 60px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  border-radius: 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: white;
  position: relative;
  z-index: 1;
  transition: transform 0.4s ease;
}

.tool-btn:hover .btn-icon {
  transform: scale(1.1);
}

.tool-btn.admin .btn-icon {
  background: linear-gradient(135deg, #ec4899, #a855f7);
}

.btn-text {
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
  position: relative;
  z-index: 1;
}

.btn-sub {
  font-size: 0.75rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 2px;
  position: relative;
  z-index: 1;
}

@media (max-width: 600px) {
  .tools-grid {
    grid-template-columns: 1fr;
    gap: 16px;
  }
  
  .tool-btn {
    width: 280px;
    height: 140px;
    flex-direction: row;
    justify-content: flex-start;
    padding: 20px 24px;
  }
  
  .btn-icon {
    width: 50px;
    height: 50px;
  }
  
  .logo-text {
    font-size: 2rem;
  }
}
</style>