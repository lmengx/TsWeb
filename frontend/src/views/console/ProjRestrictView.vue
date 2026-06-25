<script setup>
import { ref, onMounted } from 'vue'
import { getProjConfig, saveProjConfig, clearProjCache } from '../../api/antiCheatApi.js'

const projConfig = ref(null)
const projConfigEdit = ref(null)
const projLoading = ref(false)
const projSaving = ref(false)
const projError = ref('')
const projSuccess = ref('')

const fixedProgressOrder = [
  '始终生效',
  '哥布林入侵',
  '史莱姆王',
  '克苏鲁之眼',
  '世界吞噬者',
  '克苏鲁之脑',
  '蜂后',
  '骷髅王',
  '血肉墙',
  '史莱姆皇后',
  '毁灭者',
  '双子魔眼',
  '机械骷髅王',
  '世纪之花',
  '石巨人',
  '猪龙鱼公爵',
  '光之女皇',
  '拜月教教徒',
  '月亮领主'
]

const bossImageMap = {
  '史莱姆王': 'King_Slime.png',
  '克苏鲁之眼': 'Eye_of_Cthulhu.png',
  '世界吞噬者': 'Eater_of_Worlds.webp',
  '克苏鲁之脑': 'Brain_of_Cthulhu.png',
  '蜂后': 'QueenBee.png',
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
  '月亮领主': 'Moon_Lord.png',
  '哥布林入侵': 'Goblin.webp'
}

const quickCommands = [
  { label: '/banp "{playername}" "违规使用{projid}"', desc: '封禁玩家' },
  { label: '/kick "{playername}" "违规使用{projid}"', desc: '踢出玩家' },
  { label: '/bc "{playername}违规使用{projid}"', desc: '广播公告' }
]

const getProgressImageUrl = (progressName) => {
  const imageName = bossImageMap[progressName]
  return imageName ? `/assets/img/Boss/${imageName}` : null
}

const mapProjConfigToFrontend = (config) => {
  if (!config) return null

  const restrictionsMap = {}
  const restrictions = config['限制列表'] ?? config.restrictions ?? []
  restrictions.forEach(r => {
    const progress = r['进度'] ?? r.progress
    restrictionsMap[progress] = (r['限制弹幕'] ?? r.projectiles ?? []).map(p => ({
      id: p.id ?? 0,
      method: p.method ?? 'log'
    }))
  })

  return {
    enabled: config['启用'] ?? config.enabled ?? true,
    damageLimit: config['伤害上限'] ?? config.damageLimit ?? 20000,
    restrictionsMap
  }
}

const mapProjConfigToBackend = (config) => {
  if (!config) return null

  const restrictionsList = []
  for (const progress of fixedProgressOrder) {
    const projectiles = config.restrictionsMap[progress] || []
    if (projectiles.length > 0) {
      restrictionsList.push({
        '进度': progress,
        '限制弹幕': projectiles.map(p => ({
          id: p.id ?? 0,
          method: p.method ?? 'log'
        }))
      })
    }
  }

  return {
    '启用': config.enabled ?? true,
    '伤害上限': config.damageLimit ?? 20000,
    '限制列表': restrictionsList
  }
}

const fetchProjConfig = async () => {
  projLoading.value = true
  projError.value = ''

  try {
    const config = await getProjConfig()
    if (config) {
      const mappedConfig = mapProjConfigToFrontend(config)
      projConfig.value = mappedConfig
      projConfigEdit.value = JSON.parse(JSON.stringify(mappedConfig))
    }
  } catch (err) {
    projError.value = err.message
  }

  projLoading.value = false
}

const handleSaveProjConfig = async () => {
  projSaving.value = true
  projError.value = ''
  projSuccess.value = ''

  try {
    const backendConfig = mapProjConfigToBackend(projConfigEdit.value)
    const result = await saveProjConfig(backendConfig)
    if (result.success) {
      projSuccess.value = '保存成功'
      projConfig.value = JSON.parse(JSON.stringify(projConfigEdit.value))
      clearProjCache()
      setTimeout(() => {
        projSuccess.value = ''
      }, 3000)
    } else {
      projError.value = result.error || '保存失败'
    }
  } catch (err) {
    projError.value = err.message
  }

  projSaving.value = false
}

const addProjectile = (progress) => {
  if (!projConfigEdit.value) return
  if (!projConfigEdit.value.restrictionsMap[progress]) {
    projConfigEdit.value.restrictionsMap[progress] = []
  }
  projConfigEdit.value.restrictionsMap[progress].push({
    id: 0,
    method: 'log'
  })
}

const removeProjectile = (progress, index) => {
  if (!projConfigEdit.value || !projConfigEdit.value.restrictionsMap[progress]) return
  projConfigEdit.value.restrictionsMap[progress].splice(index, 1)
}

const setMethod = (progress, index, method) => {
  if (!projConfigEdit.value || !projConfigEdit.value.restrictionsMap[progress]) return
  projConfigEdit.value.restrictionsMap[progress][index].method = method
}

const insertQuickCommand = (progress, index, command) => {
  if (!projConfigEdit.value || !projConfigEdit.value.restrictionsMap[progress]) return
  const proj = projConfigEdit.value.restrictionsMap[progress][index]
  proj.method = command
}

const isQuickMethod = (method) => {
  return ['ban', 'kick', 'log'].includes(method)
}

const getMethodLabel = (method) => {
  const labels = {
    ban: '封禁',
    kick: '踢出',
    log: '记录'
  }
  return labels[method] || method || '命令'
}

const getMethodColor = (method) => {
  if (method === 'ban') return 'var(--color-ban)'
  if (method === 'kick') return 'var(--color-kick)'
  if (method === 'log') return 'var(--color-log)'
  return 'var(--color-custom)'
}

onMounted(() => {
  fetchProjConfig()
})
</script>

<template>
  <div class="proj-restrict-view">
    <div class="page-header">
      <h2>弹幕限制配置</h2>
      <p class="page-desc">配置不同游戏进度下的弹幕限制规则</p>
    </div>

    <div class="section">
      <div v-if="projLoading" class="loading">
        <div class="loading-spinner"></div>
        <span>加载中...</span>
      </div>

      <div v-else-if="projConfigEdit">
        <div class="config-header">
          <div class="config-row">
            <label>启用检测</label>
            <label class="toggle">
              <input type="checkbox" v-model="projConfigEdit.enabled">
              <span class="slider"></span>
            </label>
          </div>

          <div class="config-row">
            <label>伤害上限</label>
            <input type="number" v-model.number="projConfigEdit.damageLimit" min="0" class="damage-input">
          </div>
        </div>

        <div class="restrictions-container">
          <div v-for="progress in fixedProgressOrder" :key="progress" class="boss-card">
            <div class="boss-card-header">
              <div class="boss-avatar">
                <img
                  v-if="getProgressImageUrl(progress)"
                  :src="getProgressImageUrl(progress)"
                  :alt="progress"
                  class="boss-portrait"
                />
                <div v-else class="avatar-placeholder">
                  <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <circle cx="12" cy="12" r="10"></circle>
                    <path d="M12 8v4M12 16h.01"></path>
                  </svg>
                </div>
              </div>
              <div class="boss-meta">
                <h4 class="boss-title">{{ progress }}</h4>
                <span class="restriction-count">
                  {{ projConfigEdit.restrictionsMap[progress]?.length || 0 }} 条限制
                </span>
              </div>
            </div>

            <div class="boss-card-body">
              <div class="restriction-list" v-if="projConfigEdit.restrictionsMap[progress]?.length > 0">
                <div
                  v-for="(proj, pIndex) in projConfigEdit.restrictionsMap[progress]"
                  :key="pIndex"
                  class="restriction-item"
                  :class="{
                    'method-ban': proj.method === 'ban',
                    'method-kick': proj.method === 'kick',
                    'method-log': proj.method === 'log',
                    'method-custom': !isQuickMethod(proj.method)
                  }"
                >
                  <div class="restriction-info">
                    <div class="id-wrapper">
                      <span class="id-label">ID</span>
                      <input
                        type="number"
                        v-model.number="proj.id"
                        class="restriction-id-input"
                        min="0"
                        placeholder="弹幕ID"
                      />
                    </div>
                    
                    <div class="method-panel">
                      <div class="method-buttons">
                        <button
                          @click="setMethod(progress, pIndex, 'log')"
                          class="method-btn method-log-btn"
                          :class="{ active: proj.method === 'log' }"
                          title="仅记录日志"
                        >
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>
                            <polyline points="14 2 14 8 20 8"></polyline>
                            <line x1="16" y1="13" x2="8" y2="13"></line>
                            <line x1="16" y1="17" x2="8" y2="17"></line>
                          </svg>
                          <span>记录</span>
                        </button>
                        
                        <button
                          @click="setMethod(progress, pIndex, 'kick')"
                          class="method-btn method-kick-btn"
                          :class="{ active: proj.method === 'kick' }"
                          title="踢出玩家"
                        >
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M21 12a9 9 0 00-9-9 9.75 9.75 0 00-6.74 2.74L3 8"></path>
                            <path d="M16 3.13a9 9 0 010 17.74"></path>
                            <path d="M10 17l6-6"></path>
                          </svg>
                          <span>踢出</span>
                        </button>
                        
                        <button
                          @click="setMethod(progress, pIndex, 'ban')"
                          class="method-btn method-ban-btn"
                          :class="{ active: proj.method === 'ban' }"
                          title="封禁玩家"
                        >
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <circle cx="12" cy="12" r="10"></circle>
                            <line x1="15" y1="9" x2="9" y2="15"></line>
                            <line x1="9" y1="9" x2="15" y2="15"></line>
                          </svg>
                          <span>封禁</span>
                        </button>

                        <button
                          @click="setMethod(progress, pIndex, 'command')"
                          class="method-btn method-command-btn"
                          :class="{ active: !isQuickMethod(proj.method) }"
                          title="执行自定义命令"
                        >
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <polyline points="4 17 10 11 4 5"></polyline>
                            <line x1="12" y1="19" x2="20" y2="19"></line>
                          </svg>
                          <span>命令</span>
                        </button>
                      </div>

                      <div v-if="!isQuickMethod(proj.method)" class="custom-method-input">
                        <input
                          type="text"
                          v-model="proj.method"
                          class="method-input-field"
                          placeholder="支持 {playername}、{projid} 转义"
                        />
                        <div class="quick-commands">
                          <span class="quick-label">快速:</span>
                          <button
                            v-for="cmd in quickCommands"
                            :key="cmd.label"
                            @click="insertQuickCommand(progress, pIndex, cmd.label)"
                            class="quick-btn"
                            :title="cmd.desc"
                          >
                            {{ cmd.label }}
                          </button>
                        </div>
                      </div>
                    </div>
                  </div>
                  
                  <button @click="removeProjectile(progress, pIndex)" class="delete-btn" title="移除">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2"></path>
                      <line x1="10" y1="11" x2="10" y2="17"></line>
                      <line x1="14" y1="11" x2="14" y2="17"></line>
                    </svg>
                  </button>
                </div>
              </div>

              <div class="empty-restriction" v-else>
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                  <circle cx="12" cy="12" r="10"></circle>
                  <path d="M8 14s1.5 2 4 2 4-2 4-2M9 9h.01M15 9h.01"></path>
                </svg>
                <span>暂无限制</span>
              </div>

              <button @click="addProjectile(progress)" class="add-restriction-btn">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"></circle>
                  <line x1="12" y1="8" x2="12" y2="16"></line>
                  <line x1="8" y1="12" x2="16" y2="12"></line>
                </svg>
                添加限制
              </button>
            </div>
          </div>
        </div>

        <div v-if="projError" class="error-message">{{ projError }}</div>
        <div v-if="projSuccess" class="success-message">{{ projSuccess }}</div>

        <div class="actions">
          <button @click="handleSaveProjConfig" :disabled="projSaving" class="save-btn">
            <svg v-if="projSaving" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spinner">
              <circle cx="12" cy="12" r="10"></circle>
            </svg>
            {{ projSaving ? '保存中...' : '保存配置' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.proj-restrict-view {
  padding: 20px;
  width: 100%;
  --color-ban: #ef4444;
  --color-kick: #f97316;
  --color-log: #eab308;
  --color-custom: #8b5cf6;
}

.page-header {
  margin-bottom: 20px;
}

.page-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
}

.page-header .page-desc {
  margin: 6px 0 0 0;
  color: var(--text-muted);
  font-size: 0.9rem;
}

.section {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.config-header {
  display: flex;
  gap: 32px;
  margin-bottom: 24px;
  padding: 16px 20px;
  background: rgba(99, 102, 241, 0.05);
  border-radius: 12px;
}

.config-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.config-row label {
  color: var(--text-secondary);
  font-weight: 500;
}

.toggle {
  position: relative;
  display: inline-block;
  width: 48px;
  height: 26px;
}

.toggle input {
  opacity: 0;
  width: 0;
  height: 0;
}

.toggle .slider {
  position: absolute;
  cursor: pointer;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: var(--border-light);
  transition: 0.3s;
  border-radius: 26px;
}

.toggle .slider:before {
  position: absolute;
  content: "";
  height: 20px;
  width: 20px;
  left: 3px;
  bottom: 3px;
  background-color: white;
  transition: 0.3s;
  border-radius: 50%;
}

.toggle input:checked + .slider {
  background-color: var(--accent-primary);
}

.toggle input:checked + .slider:before {
  transform: translateX(22px);
}

.damage-input {
  padding: 8px 12px;
  border: 1px solid var(--border-light);
  border-radius: 8px;
  background: var(--bg-input);
  color: var(--text-primary);
  width: 120px;
  font-size: 0.95rem;
  -moz-appearance: textfield;
}

.damage-input::-webkit-outer-spin-button,
.damage-input::-webkit-inner-spin-button {
  -webkit-appearance: none;
  margin: 0;
}

.restrictions-container {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(400px, 1fr));
  gap: 20px;
}

.boss-card {
  background: var(--bg-primary);
  border: 1px solid var(--border-light);
  border-radius: 20px;
  overflow: hidden;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.boss-card:hover {
  border-color: rgba(99, 102, 241, 0.4);
  box-shadow: 0 8px 30px rgba(99, 102, 241, 0.12);
  transform: translateY(-2px);
}

.boss-card-header {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 20px;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.08) 0%, rgba(79, 70, 229, 0.03) 100%);
  border-bottom: 1px solid var(--border-light);
}

.boss-avatar {
  flex-shrink: 0;
}

.boss-portrait {
  width: 72px;
  height: 72px;
  border-radius: 16px;
  object-fit: cover;
  border: 2px solid rgba(99, 102, 241, 0.2);
  background: rgba(99, 102, 241, 0.1);
  transition: all 0.3s;
}

.boss-card:hover .boss-portrait {
  border-color: rgba(99, 102, 241, 0.4);
  transform: scale(1.02);
}

.avatar-placeholder {
  width: 72px;
  height: 72px;
  border-radius: 16px;
  background: rgba(99, 102, 241, 0.1);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--accent-primary);
}

.boss-meta {
  flex: 1;
  min-width: 0;
}

.boss-title {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--text-primary);
  line-height: 1.3;
}

.restriction-count {
  font-size: 0.8rem;
  color: var(--text-muted);
  margin-top: 4px;
  display: block;
}

.boss-card-body {
  padding: 16px;
}

.restriction-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
  margin-bottom: 16px;
  max-height: 350px;
  overflow-y: auto;
}

.restriction-list::-webkit-scrollbar {
  width: 6px;
}

.restriction-list::-webkit-scrollbar-track {
  background: transparent;
}

.restriction-list::-webkit-scrollbar-thumb {
  background: var(--border-light);
  border-radius: 3px;
}

.restriction-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px;
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 16px;
  transition: all 0.2s;
}

.restriction-item:hover {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.03);
}

.restriction-item.method-ban {
  border-left: 4px solid var(--color-ban);
}

.restriction-item.method-kick {
  border-left: 4px solid var(--color-kick);
}

.restriction-item.method-log {
  border-left: 4px solid var(--color-log);
}

.restriction-item.method-custom {
  border-left: 4px solid var(--color-custom);
}

.restriction-info {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  flex: 1;
  min-width: 0;
}

.id-wrapper {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: 70px;
}

.id-label {
  font-size: 0.7rem;
  color: var(--text-muted);
  margin-bottom: 6px;
}

.restriction-id-input {
  width: 64px;
  padding: 10px 8px;
  background: rgba(99, 102, 241, 0.1);
  border: 1px solid rgba(99, 102, 241, 0.2);
  border-radius: 10px;
  color: var(--text-primary);
  font-family: 'SF Mono', 'Consolas', monospace;
  font-size: 0.9rem;
  font-weight: 600;
  text-align: center;
  transition: all 0.2s;
  -moz-appearance: textfield;
}

.restriction-id-input::-webkit-outer-spin-button,
.restriction-id-input::-webkit-inner-spin-button {
  -webkit-appearance: none;
  margin: 0;
}

.restriction-id-input:focus {
  outline: none;
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.15);
}

.restriction-id-input::placeholder {
  color: var(--text-muted);
  font-weight: 400;
}

.method-panel {
  flex: 1;
  min-width: 0;
}

.method-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 12px;
}

.method-btn {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 10px 16px;
  border: 1px solid var(--border-light);
  border-radius: 12px;
  background: transparent;
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 500;
  transition: all 0.25s;
  color: var(--text-secondary);
  min-width: 70px;
  justify-content: center;
}

.method-btn:hover {
  background: rgba(99, 102, 241, 0.08);
  border-color: rgba(99, 102, 241, 0.3);
}

.method-btn.active {
  color: white;
  border-color: transparent;
  transform: scale(1.02);
}

.method-log-btn.active {
  background: var(--color-log);
}

.method-kick-btn.active {
  background: var(--color-kick);
}

.method-ban-btn.active {
  background: var(--color-ban);
}

.method-command-btn {
  color: var(--color-custom);
  border-color: rgba(139, 92, 246, 0.3);
}

.method-command-btn:hover {
  background: rgba(139, 92, 246, 0.1);
  border-color: rgba(139, 92, 246, 0.4);
}

.method-command-btn.active {
  background: var(--color-custom);
  color: white;
  border-color: transparent;
}

.custom-method-input {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.method-input-field {
  width: 100%;
  padding: 10px 14px;
  border: 1px solid var(--border-light);
  border-radius: 10px;
  background: rgba(139, 92, 246, 0.08);
  color: var(--text-primary);
  font-size: 0.85rem;
  font-family: 'SF Mono', 'Consolas', monospace;
  transition: all 0.2s;
}

.method-input-field:focus {
  outline: none;
  border-color: var(--color-custom);
  background: rgba(139, 92, 246, 0.12);
}

.method-input-field::placeholder {
  color: var(--text-muted);
}

.quick-commands {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.quick-label {
  font-size: 0.75rem;
  color: var(--text-muted);
  margin-right: 4px;
}

.quick-btn {
  padding: 5px 12px;
  background: rgba(99, 102, 241, 0.1);
  border: 1px solid rgba(99, 102, 241, 0.2);
  border-radius: 8px;
  font-size: 0.75rem;
  color: var(--accent-primary);
  cursor: pointer;
  transition: all 0.2s;
}

.quick-btn:hover {
  background: rgba(99, 102, 241, 0.2);
  border-color: var(--accent-primary);
}

.method-preview {
  display: flex;
  align-items: center;
  gap: 8px;
}

.preview-label {
  font-size: 0.75rem;
  color: var(--text-muted);
}

.preview-value {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--text-primary);
}

.delete-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  padding: 0;
  background: rgba(239, 68, 68, 0.08);
  border: 1px solid rgba(239, 68, 68, 0.15);
  border-radius: 10px;
  cursor: pointer;
  color: #ef4444;
  transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1);
  flex-shrink: 0;
}

.delete-btn:hover {
  background: rgba(239, 68, 68, 0.18);
  border-color: rgba(239, 68, 68, 0.3);
  transform: scale(1.08);
}

.delete-btn:active {
  transform: scale(0.95);
}

.empty-restriction {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 24px;
  color: var(--text-muted);
  font-size: 0.9rem;
}

.add-restriction-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  width: 100%;
  padding: 14px 20px;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.08), rgba(79, 70, 229, 0.05));
  color: var(--accent-primary);
  border: 1px dashed rgba(99, 102, 241, 0.3);
  border-radius: 12px;
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1);
}

.add-restriction-btn:hover {
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.15), rgba(79, 70, 229, 0.1));
  border-color: var(--accent-primary);
  transform: translateY(-1px);
}

.add-restriction-btn:active {
  transform: translateY(0);
}

.error-message {
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.1);
  color: var(--accent-error);
  border-radius: 8px;
  margin-top: 16px;
}

.success-message {
  padding: 12px 16px;
  background: rgba(34, 197, 94, 0.1);
  color: #22c55e;
  border-radius: 8px;
  margin-top: 16px;
}

.actions {
  margin-top: 20px;
  display: flex;
  justify-content: flex-end;
}

.save-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 28px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: 10px;
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 500;
  transition: all 0.25s ease;
}

.save-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 6px 16px rgba(99, 102, 241, 0.4);
}

.save-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.save-btn .spinner {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.loading {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 40px;
  color: var(--text-muted);
}

.loading-spinner {
  width: 40px;
  height: 40px;
  border: 3px solid rgba(99, 102, 241, 0.2);
  border-top-color: var(--accent-primary);
  border-radius: 50%;
  animation: spin 1s linear infinite;
}
</style>