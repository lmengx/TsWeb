<script setup>
import { ref, onMounted, computed } from 'vue'
import { get, post } from '../utils/api.js'
import { useRouter } from 'vue-router'
import { loadItemData } from '../api/itemDataApi.js'

const router = useRouter()
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const success = ref('')
const itemData = ref({ list: [], dict: {} })
const configItems = ref([])
const searchQuery = ref('')
const addSearchQuery = ref('')
const showAddModal = ref(false)
const imageErrors = ref({})

const filteredItems = computed(() => {
  if (!searchQuery.value.trim()) return configItems.value
  const query = searchQuery.value.toLowerCase()
  return configItems.value.filter(item => {
    const info = itemData.value.dict[item.id.toString()]
    if (!info) return item.id.toString().includes(query)
    return info.chinese.toLowerCase().includes(query) ||
           info.english.toLowerCase().includes(query) ||
           item.id.toString().includes(query)
  })
})

const searchResults = computed(() => {
  if (!addSearchQuery.value.trim()) return []
  const query = addSearchQuery.value.toLowerCase()
  const existingIds = configItems.value.map(i => i.id)
  return itemData.value.list
    .filter(item => {
      return item.chinese.toLowerCase().includes(query) ||
             item.english.toLowerCase().includes(query) ||
             item.id.toString().includes(query)
    })
    .map(item => ({
      ...item,
      isAdded: existingIds.includes(item.id)
    }))
    .slice(0, 20)
})

const getItemInfo = (id) => {
  return itemData.value.dict[id.toString()] || null
}

const getItemImage = (id) => {
  const hasError = imageErrors.value[id]
  if (hasError) {
    const info = getItemInfo(id)
    if (info && info.english) {
      const wikiName = info.english.replace(/\s+/g, '_')
      return `https://terraria.wiki.gg/images/${wikiName}.png`
    }
    return ''
  }
  return `/assets/img/img/Item_${id}.png`
}

const handleImageError = (id) => {
  imageErrors.value = { ...imageErrors.value, [id]: true }
}

const fetchConfig = async () => {
  loading.value = true
  error.value = ''
  try {
    const [items, configRes] = await Promise.all([
      loadItemData(),
      get(`/api/config/file?name=${encodeURIComponent('物品数量限制.json')}`)
    ])
    itemData.value = items
    
    const data = await configRes.json()
    if (data.content) {
      const parsed = JSON.parse(data.content)
      configItems.value = parsed.items || []
    }
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  loading.value = false
}

const increaseStack = (id) => {
  const item = configItems.value.find(i => i.id === id)
  if (item) {
    item.maxStack = Math.min(item.maxStack + 1, 9999)
  }
}

const decreaseStack = (id) => {
  const item = configItems.value.find(i => i.id === id)
  if (item && item.maxStack > 1) {
    item.maxStack = item.maxStack - 1
  }
}

const removeItem = (id) => {
  configItems.value = configItems.value.filter(i => i.id !== id)
}

const addItem = (id) => {
  if (!configItems.value.find(i => i.id === id)) {
    configItems.value.push({ id, maxStack: 999 })
  }
  addSearchQuery.value = ''
  showAddModal.value = false
}

const saveConfig = async () => {
  saving.value = true
  error.value = ''
  success.value = ''
  try {
    const content = JSON.stringify({ items: configItems.value }, null, 2)
    const response = await post('/api/config/file', {
      name: '物品数量限制.json',
      content
    })
    const result = await response.json()
    if (result.status === '200') {
      success.value = '配置保存成功'
      setTimeout(() => { success.value = '' }, 2000)
    } else {
      error.value = result.error || '保存失败'
    }
  } catch (err) {
    error.value = '保存失败: ' + err.message
  }
  saving.value = false
}

const goToAppConfig = () => {
  router.push('/console/settings/app')
}

onMounted(() => {
  fetchConfig()
})
</script>

<template>
  <div class="settings-page">
    <div class="page-header">
      <h2>设置</h2>
    </div>

    <div class="settings-content">
      <div class="main-section">
        <div class="section-card">
          <h3>物品数量限制</h3>
          
          <div v-if="loading" class="loading-state">
            <p>加载中...</p>
          </div>

          <div v-else class="config-editor">
            <div class="toolbar">
              <input
                v-model="searchQuery"
                type="text"
                placeholder="搜索物品（名称或ID）..."
                class="search-input"
              />
              <button @click="showAddModal = true" class="add-btn">
                + 添加物品
              </button>
            </div>

            <div class="items-list">
              <div v-if="filteredItems.length === 0" class="empty-list">
                暂无配置项
              </div>
              
              <div
                v-for="item in filteredItems"
                :key="item.id"
                class="item-row"
              >
                <img
                  :src="getItemImage(item.id)"
                  :alt="getItemInfo(item.id)?.chinese || ''"
                  class="item-icon"
                  @error="handleImageError(item.id)"
                />
                <div class="item-details">
                  <span class="item-name">
                    {{ getItemInfo(item.id)?.chinese || `物品(${item.id})` }}
                  </span>
                  <span class="item-id">ID: {{ item.id }}</span>
                </div>
                <div class="stack-control">
                  <button @click="decreaseStack(item.id)" class="stack-btn">-</button>
                  <input
                    type="number"
                    v-model.number="item.maxStack"
                    min="1"
                    max="9999"
                    class="stack-input"
                  />
                  <button @click="increaseStack(item.id)" class="stack-btn">+</button>
                </div>
                <button @click="removeItem(item.id)" class="remove-btn">×</button>
              </div>
            </div>

            <div class="editor-actions">
              <button
                @click="saveConfig"
                :disabled="saving"
                class="save-btn"
              >
                {{ saving ? '保存中...' : '保存配置' }}
              </button>
            </div>

            <div v-if="error" class="error-message">{{ error }}</div>
            <div v-if="success" class="success-message">{{ success }}</div>
          </div>
        </div>

        <div class="section-card">
          <h3>应用配置</h3>
          <p class="config-desc">管理服务器连接配置</p>
          <button @click="goToAppConfig" class="nav-btn">
            前往应用配置
          </button>
        </div>
      </div>
    </div>

    <div v-if="showAddModal" class="modal-overlay" @click.self="showAddModal = false">
      <div class="modal-content">
        <div class="modal-header">
          <h4>添加物品</h4>
          <button @click="showAddModal = false" class="modal-close">×</button>
        </div>
        <input
          v-model="addSearchQuery"
          type="text"
          placeholder="搜索物品（名称或ID）..."
          class="modal-search"
          autofocus
        />
        <div class="search-results">
          <div
            v-for="item in searchResults"
            :key="item.id"
            class="result-item"
            :class="{ 'result-item-added': item.isAdded }"
            @click="addItem(item.id)"
          >
            <img
              :src="`/assets/img/img/Item_${item.id}.png`"
              :alt="item.chinese"
              class="result-icon"
              @error="$event.target.style.display='none'"
            />
            <span class="result-name">{{ item.chinese }}</span>
            <span class="result-id">ID: {{ item.id }}</span>
            <span v-if="item.isAdded" class="result-status">已添加</span>
          </div>
          <div v-if="searchResults.length === 0 && addSearchQuery.trim()" class="no-results">
            未找到匹配的物品
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.settings-page {
  padding: 20px;
  width: 100%;
}

.page-header {
  margin-bottom: 20px;
}

.page-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
}

.settings-content {
  display: flex;
  gap: 20px;
}

.main-section {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.section-card {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.section-card h3 {
  margin: 0 0 16px 0;
  color: var(--text-primary);
  font-size: 1.1rem;
  font-weight: 600;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--border-light);
}

.config-editor {
  width: 100%;
}

.toolbar {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
}

.search-input {
  flex: 1;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.9rem;
  transition: all 0.25s ease;
}

.search-input:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.add-btn {
  padding: 10px 20px;
  background: var(--accent-primary);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
}

.add-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.4);
}

.items-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: 400px;
  overflow-y: auto;
  padding: 4px;
}

.empty-list {
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}

.item-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  transition: all 0.25s ease;
}

.item-row:hover {
  border-color: var(--accent-primary);
}

.item-icon {
  width: 32px;
  height: 32px;
  max-width: 32px;
  max-height: 32px;
  object-fit: contain;
  flex-shrink: 0;
  border-radius: 4px;
}

.item-details {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.item-name {
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 500;
}

.item-id {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.stack-control {
  display: flex;
  align-items: center;
  gap: 8px;
}

.stack-btn {
  width: 28px;
  height: 28px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 1rem;
  cursor: pointer;
  transition: all 0.2s ease;
}

.stack-btn:hover {
  background: var(--accent-primary);
  color: white;
}

.stack-input {
  width: 60px;
  padding: 4px 8px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 0.9rem;
  font-weight: 600;
  text-align: center;
}

.stack-input:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.stack-input::-webkit-inner-spin-button,
.stack-input::-webkit-outer-spin-button {
  -webkit-appearance: none;
  margin: 0;
}

.stack-input[type=number] {
  -moz-appearance: textfield;
}

.remove-btn {
  width: 28px;
  height: 28px;
  background: rgba(239, 68, 68, 0.1);
  border: 1px solid rgba(239, 68, 68, 0.3);
  border-radius: var(--radius-sm);
  color: #ef4444;
  font-size: 1.2rem;
  cursor: pointer;
  transition: all 0.2s ease;
}

.remove-btn:hover {
  background: #ef4444;
  color: white;
}

.editor-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}

.save-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
}

.save-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.4);
}

.save-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.config-desc {
  margin: 0 0 16px 0;
  color: var(--text-muted);
  font-size: 0.9rem;
}

.nav-btn {
  padding: 12px 24px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.25s ease;
}

.nav-btn:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.loading-state {
  text-align: center;
  padding: 60px;
  color: var(--text-muted);
}

.error-message {
  margin-top: 12px;
  padding: 10px 14px;
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.success-message {
  margin-top: 12px;
  padding: 10px 14px;
  background: rgba(34, 197, 94, 0.15);
  color: var(--accent-secondary);
  border-radius: var(--radius-md);
  font-size: 0.9rem;
  border: 1px solid rgba(34, 197, 94, 0.3);
}

.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-content {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  width: 400px;
  max-width: 90vw;
  box-shadow: var(--shadow-lg);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.modal-header h4 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.1rem;
}

.modal-close {
  width: 28px;
  height: 28px;
  background: var(--bg-tertiary);
  border: none;
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 1.2rem;
  cursor: pointer;
}

.modal-close:hover {
  background: var(--accent-error);
  color: white;
}

.modal-search {
  width: 100%;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.9rem;
  margin-bottom: 12px;
}

.modal-search:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.search-results {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: 300px;
  overflow-y: auto;
}

.result-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  cursor: pointer;
  transition: all 0.25s ease;
}

.result-item:hover {
  border-color: var(--accent-primary);
  background: var(--bg-secondary);
}

.result-icon {
  width: 24px;
  height: 24px;
  max-width: 24px;
  max-height: 24px;
  object-fit: contain;
  flex-shrink: 0;
}

.result-name {
  flex: 1;
  color: var(--text-primary);
  font-size: 0.9rem;
}

.result-id {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.result-status {
  padding: 2px 8px;
  background: rgba(34, 197, 94, 0.2);
  color: var(--accent-secondary);
  font-size: 0.75rem;
  font-weight: 600;
  border-radius: var(--radius-sm);
}

.result-item-added {
  opacity: 0.7;
}

.result-item-added:hover {
  opacity: 1;
}

.no-results {
  text-align: center;
  padding: 20px;
  color: var(--text-muted);
}
</style>