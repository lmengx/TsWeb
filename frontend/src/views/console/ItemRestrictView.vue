<script setup>
import { ref, onMounted } from 'vue'
import { getItemConfig, saveItemConfig, clearItemCache, scanItems } from '../../api/antiCheatApi.js'
import { loadItemData } from '../../api/itemDataApi.js'

const itemConfig = ref(null)
const itemConfigEdit = ref(null)
const itemLoading = ref(false)
const itemSaving = ref(false)
const itemError = ref('')
const itemSuccess = ref('')
const itemData = ref({ list: [], dict: {} })
const imageErrorIds = ref(new Set())

// 物品搜索
const itemSearchQuery = ref('')
const itemSearchResults = ref([])
const showItemSearch = ref(false)
const itemSearchTarget = ref({ progress: '', index: -1 })

const levenshteinDistance = (a, b) => {
  const matrix = []
  for (let i = 0; i <= b.length; i++) matrix[i] = [i]
  for (let j = 0; j <= a.length; j++) matrix[0][j] = j
  for (let i = 1; i <= b.length; i++) {
    for (let j = 1; j <= a.length; j++) {
      if (b.charAt(i - 1) === a.charAt(j - 1)) {
        matrix[i][j] = matrix[i - 1][j - 1]
      } else {
        matrix[i][j] = Math.min(matrix[i - 1][j - 1] + 1, matrix[i][j - 1] + 1, matrix[i - 1][j] + 1)
      }
    }
  }
  return matrix[b.length][a.length]
}

const fuzzyMatchItem = (text, keyword) => {
  if (!text || !keyword) return false
  const t = text.replace(/\s+/g, '').toLowerCase()
  const k = keyword.replace(/\s+/g, '').toLowerCase()
  if (t.includes(k)) return true
  const lenDiff = Math.abs(t.length - k.length)
  if (lenDiff > 1) return false
  return levenshteinDistance(t, k) <= 1
}

const doItemSearch = () => {
  const query = itemSearchQuery.value.trim().toLowerCase()
  if (!query) {
    itemSearchResults.value = itemData.value.list.slice(0, 50)
    return
  }
  const keywords = query.split(/\s+/).filter(k => k.length > 0)
  const results = itemData.value.list.filter(item => {
    const chinese = (item.chinese || '').toLowerCase()
    const english = (item.english || '').toLowerCase()
    const id = item.id.toString()
    return keywords.every(k =>
      chinese.includes(k) || english.includes(k) || id.includes(k) ||
      fuzzyMatchItem(chinese, k) || fuzzyMatchItem(english, k) || fuzzyMatchItem(id, k)
    )
  })
  itemSearchResults.value = results.slice(0, 100)
}

const openItemSearch = (progress, index) => {
  itemSearchTarget.value = { progress, index }
  itemSearchQuery.value = ''
  itemSearchResults.value = itemData.value.list.slice(0, 50)
  showItemSearch.value = true
}

const selectItem = (item) => {
  const { progress, index } = itemSearchTarget.value
  if (progress && index >= 0 && itemConfigEdit.value?.restrictionsMap[progress]?.[index]) {
    itemConfigEdit.value.restrictionsMap[progress][index].id = item.id
  }
  showItemSearch.value = false
}

const scanLoading = ref(false)
const scanResult = ref(null)
const scanError = ref('')

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
  { label: '/banp "{playername}" "违规使用{itemname}"', desc: '封禁玩家' },
  { label: '/kick "{playername}" "违规使用{itemname}"', desc: '踢出玩家' },
  { label: '/bc "{playername}违规使用{itemname}"', desc: '广播公告' },
  { label: '/remove {playername} {itemid}', desc: '清除物品' }
]

const getProgressImageUrl = (progressName) => {
  const imageName = bossImageMap[progressName]
  return imageName ? `/assets/img/Boss/${imageName}` : null
}

const getWikiImageUrl = (itemId) => {
  const itemInfo = getItemInfo(itemId)
  if (!itemInfo || !itemInfo.english) return null
  const name = itemInfo.english.replace(/\s+/g, '_')
  return `https://terraria.wiki.gg/images/${name}.png`
}

const getItemIconUrl = (itemId) => {
  if (imageErrorIds.value.has(itemId)) {
    const wikiUrl = getWikiImageUrl(itemId)
    if (wikiUrl) return wikiUrl
  }
  return `/assets/img/img/Item_${itemId}.png`
}

const handleItemImageError = (itemId) => {
  imageErrorIds.value.add(itemId)
}

const getItemInfo = (itemId) => {
  return itemData.value.list.find(i => i.id === itemId)
}

const mapItemConfigToFrontend = (config) => {
  if (!config) return null

  const restrictionsMap = {}
  const restrictions = config['限制列表'] ?? config.restrictions ?? []
  restrictions.forEach(r => {
    const progress = r['进度'] ?? r.progress
    restrictionsMap[progress] = (r['限制物品'] ?? r.items ?? []).map(i => ({
      id: i.id ?? i.ID ?? 0,
      stack: i.stack ?? i.Stack ?? 1,
      method: i.method ?? 'log'
    }))
  })

  return {
    enabled: config['启用'] ?? config.enabled ?? true,
    autoScan: config['自动扫描'] ?? config.autoScan ?? true,
    autoScanInterval: config['扫描间隔'] ?? config.autoScanInterval ?? 600,
    restrictionsMap
  }
}

const mapItemConfigToBackend = (config) => {
  if (!config) return null

  const restrictionsList = []
  for (const progress of fixedProgressOrder) {
    const items = config.restrictionsMap[progress] || []
    if (items.length > 0) {
      restrictionsList.push({
        '进度': progress,
        '限制物品': items.map(i => ({
          id: i.id ?? 0,
          stack: i.stack ?? 1,
          method: i.method ?? 'log'
        }))
      })
    }
  }

  return {
    '启用': config.enabled ?? true,
    '自动扫描': config.autoScan ?? true,
    '扫描间隔': config.autoScanInterval ?? 600,
    '限制列表': restrictionsList
  }
}

const fetchItemConfig = async () => {
  itemLoading.value = true
  itemError.value = ''

  try {
    const [config, items] = await Promise.all([
      getItemConfig(),
      loadItemData()
    ])
    itemData.value = items
    if (config) {
      const mappedConfig = mapItemConfigToFrontend(config)
      itemConfig.value = mappedConfig
      itemConfigEdit.value = JSON.parse(JSON.stringify(mappedConfig))
    }
  } catch (err) {
    itemError.value = err.message
  }

  itemLoading.value = false
}

const handleSaveItemConfig = async () => {
  itemSaving.value = true
  itemError.value = ''
  itemSuccess.value = ''

  try {
    const backendConfig = mapItemConfigToBackend(itemConfigEdit.value)
    const result = await saveItemConfig(backendConfig)
    if (result.success) {
      itemSuccess.value = '保存成功'
      itemConfig.value = JSON.parse(JSON.stringify(itemConfigEdit.value))
      clearItemCache()
      setTimeout(() => {
        itemSuccess.value = ''
      }, 3000)
    } else {
      itemError.value = result.error || '保存失败'
    }
  } catch (err) {
    itemError.value = err.message
  }

  itemSaving.value = false
}

const addRestrictionItem = (progress) => {
  if (!itemConfigEdit.value) return
  if (!itemConfigEdit.value.restrictionsMap[progress]) {
    itemConfigEdit.value.restrictionsMap[progress] = []
  }
  itemConfigEdit.value.restrictionsMap[progress].push({
    id: 0,
    stack: 1,
    method: 'log'
  })
}

const removeRestrictionItem = (progress, index) => {
  if (!itemConfigEdit.value || !itemConfigEdit.value.restrictionsMap[progress]) return
  itemConfigEdit.value.restrictionsMap[progress].splice(index, 1)
}

const setMethod = (progress, index, method) => {
  if (!itemConfigEdit.value || !itemConfigEdit.value.restrictionsMap[progress]) return
  itemConfigEdit.value.restrictionsMap[progress][index].method = method
}

const insertQuickCommand = (progress, index, command) => {
  if (!itemConfigEdit.value || !itemConfigEdit.value.restrictionsMap[progress]) return
  const item = itemConfigEdit.value.restrictionsMap[progress][index]
  item.method = command
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

const handleScanItems = async () => {
  scanLoading.value = true
  scanError.value = ''
  scanResult.value = null

  try {
    const result = await scanItems()
    
    if (result.status === 500 || result.status === '500' || result.error) {
      scanError.value = result.error || '扫描失败'
    } else {
      scanResult.value = result
    }
  } catch (err) {
    scanError.value = err.message
  }

  scanLoading.value = false
}

const getPlayerItemName = (itemId) => {
  const itemInfo = getItemInfo(itemId)
  return itemInfo ? itemInfo.chinese || itemInfo.name : `物品ID: ${itemId}`
}

onMounted(() => {
  fetchItemConfig()
})
</script>

<template>
  <div class="proj-restrict-view">
    <div class="page-header">
      <h2>物品限制配置</h2>
      <p class="page-desc">配置不同游戏进度下的物品限制规则</p>
    </div>

    <div class="section">
      <div v-if="itemLoading" class="loading">
        <div class="loading-spinner"></div>
        <span>加载中...</span>
      </div>

      <div v-else-if="itemConfigEdit">
        <div class="scan-section">
          <button @click="handleScanItems" :disabled="scanLoading" class="scan-btn">
            <svg v-if="scanLoading" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spinner">
              <circle cx="12" cy="12" r="10"></circle>
            </svg>
            <svg v-else width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"></path>
            </svg>
            {{ scanLoading ? '扫描中...' : '手动扫描物品' }}
          </button>

          <div v-if="scanResult" class="scan-result">
            <h4>扫描结果</h4>
            <div v-if="scanResult.players && scanResult.players.length > 0">
              <div v-for="player in scanResult.players" :key="player.name" class="player-scan">
                <span class="player-name">{{ player.name }}</span>
                <div v-if="player.items && player.items.length > 0" class="player-items">
                  <div v-for="item in player.items" :key="item.id" class="scan-item">
                    <img :src="getItemIconUrl(item.id)" :alt="getPlayerItemName(item.id)" class="scan-item-icon" />
                    <span class="scan-item-info">{{ getPlayerItemName(item.id) }} x{{ item.stack }}</span>
                  </div>
                </div>
                <div v-else class="no-violation">未发现违规物品</div>
              </div>
            </div>
            <div v-else class="no-players">当前没有在线玩家</div>
          </div>

          <div v-if="scanError" class="scan-error">{{ scanError }}</div>
        </div>

        <div class="config-header">
          <div class="config-row">
            <label>启用检测</label>
            <label class="toggle">
              <input type="checkbox" v-model="itemConfigEdit.enabled">
              <span class="slider"></span>
            </label>
          </div>

          <div class="config-row">
            <label>自动扫描</label>
            <label class="toggle">
              <input type="checkbox" v-model="itemConfigEdit.autoScan">
              <span class="slider"></span>
            </label>
          </div>

          <div class="config-row">
            <label>扫描间隔(秒)</label>
            <input 
              type="number" 
              v-model.number="itemConfigEdit.autoScanInterval" 
              min="30" 
              max="3600"
              class="interval-input"
            />
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
                  {{ itemConfigEdit.restrictionsMap[progress]?.length || 0 }} 条限制
                </span>
              </div>
            </div>

            <div class="boss-card-body">
              <div class="restriction-list" v-if="itemConfigEdit.restrictionsMap[progress]?.length > 0">
                <div
                  v-for="(item, iIndex) in itemConfigEdit.restrictionsMap[progress]"
                  :key="iIndex"
                  class="restriction-item"
                  :class="{
                    'method-ban': item.method === 'ban',
                    'method-kick': item.method === 'kick',
                    'method-log': item.method === 'log',
                    'method-custom': !isQuickMethod(item.method)
                  }"
                >
                  <div class="restriction-info">
                      <div class="id-wrapper">
                        <div class="id-search-row">
                          <span class="id-label">ID</span>
                          <button @click="openItemSearch(progress, iIndex)" class="item-search-btn" title="搜索物品">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <path d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"></path>
                            </svg>
                          </button>
                        </div>
                        <input
                        type="number"
                        v-model.number="item.id"
                        class="restriction-id-input"
                        min="0"
                        placeholder="物品ID"
                      />
                      <div class="item-preview" v-if="item.id > 0">
                        <img 
                          :src="getItemIconUrl(item.id)" 
                          :alt="getItemInfo(item.id)?.chinese || 'Item'" 
                          class="item-icon"
                          :class="{ 'icon-placeholder': !getItemInfo(item.id) }"
                          @error="handleItemImageError(item.id)"
                        />
                        <span class="item-name">{{ getItemInfo(item.id)?.chinese || getItemInfo(item.id)?.name || '未知物品' }}</span>
                      </div>
                    </div>

                    <div class="stack-wrapper">
                      <span class="stack-label">数量</span>
                      <input
                        type="number"
                        v-model.number="item.stack"
                        class="stack-input"
                        min="0"
                        placeholder="最大数量"
                      />
                    </div>
                    
                    <div class="method-panel">
                      <div class="method-buttons">
                        <button
                          @click="setMethod(progress, iIndex, 'log')"
                          class="method-btn method-log-btn"
                          :class="{ active: item.method === 'log' }"
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
                          @click="setMethod(progress, iIndex, 'kick')"
                          class="method-btn method-kick-btn"
                          :class="{ active: item.method === 'kick' }"
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
                          @click="setMethod(progress, iIndex, 'ban')"
                          class="method-btn method-ban-btn"
                          :class="{ active: item.method === 'ban' }"
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
                          @click="setMethod(progress, iIndex, 'command')"
                          class="method-btn method-command-btn"
                          :class="{ active: !isQuickMethod(item.method) }"
                          title="执行自定义命令"
                        >
                          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <polyline points="4 17 10 11 4 5"></polyline>
                            <line x1="12" y1="19" x2="20" y2="19"></line>
                          </svg>
                          <span>命令</span>
                        </button>
                      </div>

                      <div v-if="!isQuickMethod(item.method)" class="custom-method-input">
                        <input
                          type="text"
                          v-model="item.method"
                          class="method-input-field"
                          placeholder="支持 {playername}、{itemname}、{itemid} 转义"
                        />
                        <div class="quick-commands">
                          <span class="quick-label">快速:</span>
                          <button
                            v-for="cmd in quickCommands"
                            :key="cmd.label"
                            @click="insertQuickCommand(progress, iIndex, cmd.label)"
                            class="quick-btn"
                            :title="cmd.desc"
                          >
                            {{ cmd.label }}
                          </button>
                        </div>
                      </div>
                    </div>
                  </div>
                  
                  <button @click="removeRestrictionItem(progress, iIndex)" class="delete-btn" title="移除">
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

              <button @click="addRestrictionItem(progress)" class="add-restriction-btn">
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

        <div v-if="itemError" class="error-message">{{ itemError }}</div>
        <div v-if="itemSuccess" class="success-message">{{ itemSuccess }}</div>

        <div class="actions">
          <button @click="handleSaveItemConfig" :disabled="itemSaving" class="save-btn">
            <svg v-if="itemSaving" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spinner">
              <circle cx="12" cy="12" r="10"></circle>
            </svg>
            {{ itemSaving ? '保存中...' : '保存配置' }}
          </button>
        </div>
      </div>
    </div>

    <!-- 物品搜索弹窗 -->
    <Teleport to="body">
      <div v-if="showItemSearch" class="search-modal-overlay" @click.self="showItemSearch = false">
        <div class="search-modal">
          <div class="search-modal-header">
            <h3>搜索物品</h3>
            <button @click="showItemSearch = false" class="close-btn">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>
          <div class="search-modal-body">
            <input
              v-model="itemSearchQuery"
              @input="doItemSearch"
              type="text"
              placeholder="搜索物品名称或ID（支持多词）..."
              class="search-input-lg"
              autofocus
            />
            <div class="search-hint">
              找到 {{ itemSearchResults.length }} 个匹配结果
            </div>
            <div class="item-grid">
              <div
                v-for="result in itemSearchResults"
                :key="result.id"
                class="item-card"
                @click="selectItem(result)"
              >
                <img
                  :src="`/assets/img/img/Item_${result.id}.png`"
                  :alt="result.chinese"
                  class="item-card-image"
                  @error="(e) => { e.target.src = `https://terraria.wiki.gg/images/${(result.english || '').replace(/\s+/g, '_')}.png` }"
                />
                <div class="item-card-info">
                  <span class="item-card-name">{{ result.chinese }}</span>
                  <span class="item-card-id">ID: {{ result.id }}</span>
                </div>
              </div>
              <div v-if="itemSearchResults.length === 0 && itemSearchQuery.trim()" class="no-search-results">
                未找到匹配的物品
              </div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>
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
  flex-wrap: wrap;
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

.interval-input {
  padding: 8px 12px;
  border: 1px solid var(--border-light);
  border-radius: 8px;
  background: var(--bg-input);
  color: var(--text-primary);
  width: 100px;
  font-size: 0.95rem;
  -moz-appearance: textfield;
}

.interval-input::-webkit-outer-spin-button,
.interval-input::-webkit-inner-spin-button {
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

.item-preview {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  margin-top: 8px;
}

.item-icon {
  width: 32px;
  height: 32px;
  border-radius: 6px;
  object-fit: cover;
  background: rgba(99, 102, 241, 0.1);
}

.item-icon.icon-placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.75rem;
  color: var(--text-muted);
}

.item-icon.icon-error {
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.75rem;
  color: var(--text-muted);
  content: '?';
}

.item-icon.icon-error::before {
  content: '?';
}

.item-name {
  font-size: 0.7rem;
  color: var(--text-muted);
  text-align: center;
  max-width: 64px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.stack-wrapper {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: 70px;
}

.stack-label {
  font-size: 0.7rem;
  color: var(--text-muted);
  margin-bottom: 6px;
}

.stack-input {
  width: 64px;
  padding: 10px 8px;
  background: rgba(34, 197, 94, 0.1);
  border: 1px solid rgba(34, 197, 94, 0.2);
  border-radius: 10px;
  color: var(--text-primary);
  font-family: 'SF Mono', 'Consolas', monospace;
  font-size: 0.9rem;
  font-weight: 600;
  text-align: center;
  transition: all 0.2s;
  -moz-appearance: textfield;
}

.stack-input::-webkit-outer-spin-button,
.stack-input::-webkit-inner-spin-button {
  -webkit-appearance: none;
  margin: 0;
}

.stack-input:focus {
  outline: none;
  border-color: #22c55e;
  background: rgba(34, 197, 94, 0.15);
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

.scan-section {
  margin-bottom: 24px;
  padding: 16px 20px;
  background: rgba(59, 130, 246, 0.05);
  border-radius: 12px;
  border: 1px solid rgba(59, 130, 246, 0.15);
}

/* 物品搜索 */
.id-search-row {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-bottom: 6px;
}

.item-search-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border: 1px solid var(--border-light);
  border-radius: 6px;
  background: transparent;
  cursor: pointer;
  color: var(--text-muted);
  transition: all 0.2s;
  padding: 0;
}

.item-search-btn:hover {
  background: rgba(99, 102, 241, 0.1);
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.search-modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.search-modal {
  background: var(--bg-card);
  border-radius: 20px;
  width: 90%;
  max-width: 600px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
}

.search-modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 20px 24px;
  border-bottom: 1px solid var(--border-light);
}

.search-modal-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.1rem;
}

.close-btn {
  background: none;
  border: none;
  color: var(--text-muted);
  cursor: pointer;
  padding: 4px;
  border-radius: 6px;
  transition: all 0.2s;
}

.close-btn:hover {
  color: var(--text-primary);
  background: rgba(0, 0, 0, 0.1);
}

.search-modal-body {
  padding: 20px 24px;
  overflow-y: auto;
  flex: 1;
}

.search-input-lg {
  width: 100%;
  padding: 14px 18px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 12px;
  color: var(--text-primary);
  font-size: 1rem;
  box-sizing: border-box;
  transition: all 0.25s ease;
}

.search-input-lg:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.search-hint {
  font-size: 0.85rem;
  color: var(--text-muted);
  margin: 8px 0 16px;
}

.item-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
  gap: 10px;
}

.item-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 12px;
  background: var(--bg-primary);
  border: 2px solid var(--border-light);
  border-radius: 12px;
  cursor: pointer;
  transition: all 0.25s ease;
}

.item-card:hover {
  border-color: var(--accent-primary);
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.15);
}

.item-card-image {
  width: 48px;
  height: 48px;
  object-fit: contain;
  image-rendering: pixelated;
}

.item-card-info {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 2px;
}

.item-card-name {
  color: var(--text-primary);
  font-size: 0.8rem;
  font-weight: 600;
  max-width: 110px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-card-id {
  color: var(--accent-primary);
  font-size: 0.7rem;
  font-weight: 600;
}

.no-search-results {
  grid-column: 1 / -1;
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}

.scan-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 24px;
  background: linear-gradient(135deg, #3b82f6, #2563eb);
  color: white;
  border: none;
  border-radius: 10px;
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
}

.scan-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 6px 16px rgba(59, 130, 246, 0.4);
}

.scan-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.scan-btn .spinner {
  animation: spin 1s linear infinite;
}

.scan-result {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid rgba(59, 130, 246, 0.2);
}

.scan-result h4 {
  margin: 0 0 12px 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text-primary);
}

.player-scan {
  margin-bottom: 12px;
  padding: 12px;
  background: var(--bg-card);
  border-radius: 8px;
}

.player-name {
  display: block;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: 8px;
}

.player-items {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.scan-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: rgba(239, 68, 68, 0.08);
  border-radius: 8px;
  border: 1px solid rgba(239, 68, 68, 0.15);
}

.scan-item-icon {
  width: 24px;
  height: 24px;
  border-radius: 4px;
  object-fit: cover;
  background: rgba(239, 68, 68, 0.1);
}

.scan-item-info {
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.no-violation {
  font-size: 0.85rem;
  color: #22c55e;
}

.no-players {
  font-size: 0.85rem;
  color: var(--text-muted);
}

.scan-error {
  margin-top: 12px;
  padding: 10px 14px;
  background: rgba(239, 68, 68, 0.1);
  color: var(--accent-error);
  border-radius: 8px;
  font-size: 0.85rem;
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