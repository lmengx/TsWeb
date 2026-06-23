<script setup>import { ref, onMounted, computed } from 'vue';
import { get } from '../../utils/api.js';
const progressData = ref(null);
const isLoading = ref(true);
const error = ref(null);
const fetchProgress = async () => {
 isLoading.value = true;
 error.value = null;
 try {
 const response = await get('/api/tshock/boss/progress');
 const data = await response.json();
 progressData.value = data;
 }
 catch (err) {
 error.value = '获取进度数据失败';
 console.error(err);
 }
 finally {
 isLoading.value = false;
 }
};
const bossImageMap = {
 '史莱姆王': 'King_Slime.png',
 '克苏鲁之眼': 'Eye_of_Cthulhu.png',
 '世界吞噬者': 'Eater_of_Worlds.webp',
 '克苏鲁之脑': 'Brain_of_Cthulhu.png',
 '蜂后': 'QueenBee.png',
 '巨鹿': 'Deerclops.png',
 '骷髅王': 'Skeletron.png',
 '血肉墙': 'Wall_of_Flesh.png',
 '史莱姆皇后': 'Queen_Slime.png',
 '毁灭者': 'The_Destroyer.png',
 '机械骷髅王': 'Skeletron_Prime.png',
 '双子魔眼': 'The_Twins.png',
 '世纪之花': 'Plantera.png',
 '石巨人': 'Golem.png',
 '猪龙鱼公爵': 'Duke_Fishron.png',
 '光之女皇': 'Empress_of_Light.png',
 '拜月教教徒': 'Lunatic_Cultist.png',
 '月亮领主': 'Moon_Lord.png'
};
const eventImageMap = {
 '哥布林入侵': 'Goblin.webp',
 '海盗入侵': 'Flying_Dutchman.png',
 '日食': 'eclipse.webp',
 '火星人入侵': 'Martian_Saucer.png',
 '冰雪女王': 'Ice_Queen.png',
 '南瓜王': 'Pumpking.png'
};
const getBossImage = (name) => {
 const imageName = bossImageMap[name];
 return imageName ? `/assets/img/Boss/${imageName}` : null;
};
const getEventImage = (name) => {
 const imageName = eventImageMap[name];
 return imageName ? `/assets/img/Boss/${imageName}` : null;
};
const getStatusIcon = (completed) => {
 return completed ? '✓' : '✗';
};
const getStatusClass = (completed) => {
 return completed ? 'status-completed' : 'status-pending';
};
onMounted(() => {
 fetchProgress();
});
</script>

<template>
  <div class="progress-container">
    <div class="page-header">
      <h2>世界进度查询</h2>
      <button class="refresh-btn" @click="fetchProgress" :disabled="isLoading">
        <span v-if="isLoading">加载中...</span>
        <span v-else>刷新</span>
      </button>
    </div>

    <div v-if="error" class="error-box">
      {{ error }}
    </div>

    <div v-if="isLoading" class="loading-box">
      正在加载进度数据...
    </div>

    <div v-else-if="progressData" class="progress-content">
      <div class="boss-section">
        <div class="section-header">
          <h3>Boss击杀进度</h3>
          <span class="progress-badge success">{{ progressData.KilledCount }}/{{ progressData.TotalBossCount }}</span>
        </div>
        
        <div class="progress-bar-container">
          <div 
            class="progress-bar boss" 
            :style="{ width: progressData.BossProgressPercent + '%' }"
          ></div>
        </div>

        <div class="card-grid">
          <div 
            v-for="boss in progressData.Bosses" 
            :key="boss.NPCID" 
            class="boss-card"
            :class="{ completed: boss.IsKilled }"
          >
            <div class="card-image">
              <img 
                v-if="getBossImage(boss.Name)" 
                :src="getBossImage(boss.Name)" 
                :alt="boss.Name"
                @error="($event.target.style.display = 'none')"
              />
              <div v-else class="image-placeholder">
                <span>?</span>
              </div>
              <div class="status-badge" :class="getStatusClass(boss.IsKilled)">
                {{ getStatusIcon(boss.IsKilled) }}
              </div>
            </div>
            <div class="card-info">
              <span class="boss-name">{{ boss.Name }}</span>
              <span class="boss-count">{{ boss.KillCount }} 击杀</span>
            </div>
          </div>
        </div>
      </div>

      <div class="event-section">
        <div class="section-header">
          <h3>事件进度</h3>
          <span class="progress-badge warning">{{ progressData.CompletedEventCount }}/{{ progressData.TotalEventCount }}</span>
        </div>
        
        <div class="progress-bar-container">
          <div 
            class="progress-bar event" 
            :style="{ width: progressData.EventProgressPercent + '%' }"
          ></div>
        </div>

        <div class="card-grid">
          <div 
            v-for="eventItem in progressData.Events" 
            :key="eventItem.EventID" 
            class="boss-card"
            :class="{ completed: eventItem.IsCompleted }"
          >
            <div class="card-image">
              <img 
                v-if="getEventImage(eventItem.Name)" 
                :src="getEventImage(eventItem.Name)" 
                :alt="eventItem.Name"
                @error="($event.target.style.display = 'none')"
              />
              <div v-else class="image-placeholder">
                <span>?</span>
              </div>
              <div class="status-badge" :class="getStatusClass(eventItem.IsCompleted)">
                {{ getStatusIcon(eventItem.IsCompleted) }}
              </div>
            </div>
            <div class="card-info">
              <span class="boss-name">{{ eventItem.Name }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.progress-container {
  padding: 24px;
  max-width: 1200px;
  margin: 0 auto;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 24px;
}

.page-header h2 {
  font-size: 1.5rem;
  font-weight: 600;
  color: var(--text-primary);
}

.refresh-btn {
  padding: 8px 16px;
  background: var(--accent-primary);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  transition: all 0.2s ease;
}

.refresh-btn:hover:not(:disabled) {
  background: #4f46e5;
  transform: translateY(-2px);
}

.refresh-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.error-box {
  padding: 16px;
  background: rgba(239, 68, 68, 0.1);
  border: 1px solid rgba(239, 68, 68, 0.2);
  border-radius: var(--radius-md);
  color: #dc2626;
  margin-bottom: 16px;
}

.loading-box {
  padding: 40px;
  text-align: center;
  color: var(--text-secondary);
}

.progress-content {
  display: flex;
  flex-direction: column;
  gap: 32px;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.section-header h3 {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary);
}

.progress-badge {
  padding: 6px 14px;
  color: white;
  border-radius: 20px;
  font-size: 0.85rem;
  font-weight: 600;
}

.progress-badge.success {
  background: linear-gradient(135deg, #10b981, #34d399);
}

.progress-badge.warning {
  background: linear-gradient(135deg, #8b5cf6, #a78bfa);
}

.progress-bar-container {
  height: 8px;
  background: var(--bg-secondary);
  border-radius: var(--radius-sm);
  overflow: hidden;
  margin-bottom: 20px;
}

.progress-bar {
  height: 100%;
  border-radius: var(--radius-sm);
  transition: width 0.5s ease;
}

.progress-bar.boss {
  background: linear-gradient(90deg, #10b981, #34d399);
}

.progress-bar.event {
  background: linear-gradient(90deg, #8b5cf6, #a78bfa);
}

.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 16px;
}

.boss-card {
  background: var(--bg-card);
  border-radius: var(--radius-lg);
  border: 1px solid var(--border-light);
  overflow: hidden;
  transition: all 0.3s ease;
}

.boss-card:hover {
  transform: translateY(-4px);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  border-color: var(--accent-primary);
}

.boss-card.completed {
  border-color: rgba(16, 185, 129, 0.4);
}

.boss-card.completed:hover {
  border-color: rgba(16, 185, 129, 0.6);
}

.card-image {
  position: relative;
  width: 100%;
  height: 120px;
  background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.card-image img {
  width: 80%;
  height: 80%;
  object-fit: contain;
  filter: drop-shadow(0 4px 8px rgba(0, 0, 0, 0.3));
}

.image-placeholder {
  width: 80%;
  height: 80%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(255, 255, 255, 0.1);
  border-radius: var(--radius-md);
  color: var(--text-secondary);
  font-size: 2rem;
}

.status-badge {
  position: absolute;
  top: 8px;
  right: 8px;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.9rem;
  font-weight: bold;
  color: white;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
}

.status-badge.status-completed {
  background: linear-gradient(135deg, #10b981, #059669);
}

.status-badge.status-pending {
  background: linear-gradient(135deg, #ef4444, #dc2626);
}

.card-info {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.boss-name {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--text-primary);
  text-align: center;
}

.boss-count {
  font-size: 0.75rem;
  color: var(--text-secondary);
  text-align: center;
}

@media (max-width: 640px) {
  .card-grid {
    grid-template-columns: repeat(auto-fill, minmax(110px, 1fr));
    gap: 12px;
  }
  
  .card-image {
    height: 100px;
  }
}
</style>