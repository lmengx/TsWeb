<script setup>
import { ref, computed, watch } from 'vue'
import { loadItemData } from '../api/itemDataApi.js'

const props = defineProps({
  show: Boolean,
  mode: { type: String, default: 'restrict' }
})

const emit = defineEmits(['select', 'close'])

const searchQuery = ref('')
const itemData = ref({ list: [], dict: {} })
const imageErrors = ref({})
const dataLoaded = ref(false)

const searchResults = computed(() => {
  if (!searchQuery.value.trim()) {
    return itemData.value.list
      .filter(item => item.id >= 1 && item.id <= 100)
      .sort((a, b) => a.id - b.id)
  }

  const query = searchQuery.value.trim().toLowerCase()
  const keywords = query.split(/\s+/).filter(k => k.length > 0)

  const exactResults = itemData.value.list
    .filter(item => {
      const chinese = (item.chinese || '').toLowerCase()
      const english = (item.english || '').toLowerCase()
      const id = item.id.toString()

      return keywords.every(keyword => {
        return chinese.includes(keyword) ||
               english.includes(keyword) ||
               id.includes(keyword)
      })
    })

  if (exactResults.length > 0) {
    return exactResults.slice(0, 100)
  }

  return itemData.value.list
    .filter(item => {
      const chinese = (item.chinese || '').toLowerCase()
      const english = (item.english || '').toLowerCase()
      const id = item.id.toString()

      return keywords.every(keyword => {
        return fuzzyMatchOneMistake(chinese, keyword) ||
               fuzzyMatchOneMistake(english, keyword) ||
               fuzzyMatchOneMistake(id, keyword)
      })
    })
    .slice(0, 100)
})

const levenshteinDistance = (a, b) => {
  const matrix = []
  for (let i = 0; i <= b.length; i++) matrix[i] = [i]
  for (let j = 0; j <= a.length; j++) matrix[0][j] = j
  for (let i = 1; i <= b.length; i++) {
    for (let j = 1; j <= a.length; j++) {
      if (b.charAt(i - 1) === a.charAt(j - 1)) {
        matrix[i][j] = matrix[i - 1][j - 1]
      } else {
        matrix[i][j] = Math.min(
          matrix[i - 1][j - 1] + 1,
          matrix[i][j - 1] + 1,
          matrix[i - 1][j] + 1
        )
      }
    }
  }
  return matrix[b.length][a.length]
}

const fuzzyMatchOneMistake = (text, keyword) => {
  if (!text || !keyword) return false
  const t = text.replace(/\s+/g, '')
  const k = keyword.replace(/\s+/g, '')
  if (t.includes(k)) return true
  const lenDiff = Math.abs(t.length - k.length)
  if (lenDiff > 1) return false
  return levenshteinDistance(t, k) <= 1
}

const getItemImage = (id) => {
  const hasError = imageErrors.value[id]
  if (hasError) {
    const info = itemData.value.dict[id.toString()]
    if (info && info.english) {
      return `https://terraria.wiki.gg/images/${info.english.replace(/\s+/g, '_')}.png`
    }
    return ''
  }
  return `/assets/img/img/Item_${id}.png`
}

const handleImageError = (id) => {
  imageErrors.value = { ...imageErrors.value, [id]: true }
}

const initItemData = async () => {
  itemData.value = await loadItemData()
  dataLoaded.value = true
}

watch(() => props.show, (val) => {
  if (val) {
    searchQuery.value = ''
    imageErrors.value = {}
    if (!dataLoaded.value) {
      initItemData()
    }
  }
})
</script>

<template>
  <Teleport to="body">
    <div v-if="show" class="search-dialog-overlay" @click.self="emit('close')">
      <div class="search-dialog">
        <div class="search-dialog-header">
          <h3>{{ mode === 'scan' ? '扫描持有者 - 选择物品' : '选择物品' }}</h3>
          <button @click="emit('close')" class="close-btn">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>
        <div class="search-dialog-body">
          <input
            v-model="searchQuery"
            type="text"
            placeholder="搜索物品名称或ID（支持多词）..."
            class="search-input"
            autofocus
          />
          <div class="search-hint">
            找到 {{ searchResults.length }} 个匹配结果
          </div>
          <div class="results-grid">
            <div
              v-for="item in searchResults"
              :key="item.id"
              class="item-card"
              @click="emit('select', item)"
            >
              <div class="item-image-wrapper">
                <img
                  :src="getItemImage(item.id)"
                  :alt="item.chinese"
                  class="item-image"
                  @error="handleImageError(item.id)"
                />
              </div>
              <div class="item-info">
                <span class="item-name">{{ item.chinese }}</span>
                <span class="item-id">ID: {{ item.id }}</span>
                <span class="item-english">{{ item.english }}</span>
              </div>
            </div>
            <div v-if="searchResults.length === 0 && searchQuery.trim()" class="no-results">
              未找到匹配的物品
            </div>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.search-dialog-overlay {
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

.search-dialog {
  background: var(--bg-card);
  border-radius: 20px;
  width: 90%;
  max-width: 620px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
}

.search-dialog-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 20px 24px;
  border-bottom: 1px solid var(--border-light);
}

.search-dialog-header h3 {
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

.search-dialog-body {
  padding: 20px 24px;
  overflow-y: auto;
  flex: 1;
}

.search-input {
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

.search-input:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.search-hint {
  font-size: 0.85rem;
  color: var(--text-muted);
  margin: 8px 0 16px;
}

.results-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
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

.item-image-wrapper {
  width: 48px;
  height: 48px;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.item-image {
  width: 90%;
  height: 90%;
  object-fit: contain;
  image-rendering: pixelated;
}

.item-info {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 2px;
}

.item-name {
  color: var(--text-primary);
  font-size: 0.8rem;
  font-weight: 600;
  max-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-id {
  color: var(--accent-primary);
  font-size: 0.7rem;
  font-weight: 600;
}

.item-english {
  color: var(--text-muted);
  font-size: 0.65rem;
  max-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.no-results {
  grid-column: 1 / -1;
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}
</style>
