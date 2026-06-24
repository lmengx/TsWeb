<script setup>
import { ref, computed, onMounted } from 'vue'
import { loadItemData } from '../../../api/itemDataApi.js'

const searchQuery = ref('')
const itemData = ref({ list: [], dict: {} })
const imageErrors = ref({})

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
      const chinese = item.chinese.toLowerCase()
      const english = item.english.toLowerCase()
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
      const chinese = item.chinese.toLowerCase()
      const english = item.english.toLowerCase()
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
  for (let i = 0; i <= b.length; i++) {
    matrix[i] = [i]
  }
  for (let j = 0; j <= a.length; j++) {
    matrix[0][j] = j
  }
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

  const textWithoutSpaces = text.replace(/\s+/g, '')
  const keywordWithoutSpaces = keyword.replace(/\s+/g, '')

  if (textWithoutSpaces.includes(keywordWithoutSpaces)) return true

  const lenDiff = Math.abs(textWithoutSpaces.length - keywordWithoutSpaces.length)
  if (lenDiff > 1) return false

  const distance = levenshteinDistance(textWithoutSpaces, keywordWithoutSpaces)
  return distance <= 1
}

const getItemImage = (id) => {
  const hasError = imageErrors.value[id]
  if (hasError) {
    const info = itemData.value.dict[id.toString()]
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

const initItemData = async () => {
  itemData.value = await loadItemData()
}

onMounted(() => {
  initItemData()
})
</script>

<template>
  <div class="item-search-page">
    <div class="page-header">
      <h2>物品查询</h2>
    </div>

    <div class="search-container">
      <div class="search-box">
        <input
          v-model="searchQuery"
          type="text"
          placeholder="搜索物品（支持多词搜索，如：剑 魔法）..."
          class="search-input"
          autofocus
        />
        <div v-if="searchResults.length > 0" class="search-hint">
          找到 {{ searchResults.length }} 个匹配结果
        </div>
      </div>

      <div class="results-grid">
        <div
          v-for="item in searchResults"
          :key="item.id"
          class="item-card"
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
</template>

<style scoped>
.item-search-page {
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

.search-container {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.search-box {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.search-input {
  padding: 14px 18px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-lg);
  color: var(--text-primary);
  font-size: 1rem;
  transition: all 0.25s ease;
}

.search-input:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.search-hint {
  font-size: 0.85rem;
  color: var(--text-muted);
}

.results-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 12px;
}

.item-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-lg);
  transition: all 0.25s ease;
  cursor: pointer;
}

.item-card:hover {
  border-color: var(--accent-primary);
  transform: translateY(-2px);
  box-shadow: var(--shadow-md);
}

.item-image-wrapper {
  width: 64px;
  height: 64px;
  background: var(--bg-secondary);
  border-radius: var(--radius-md);
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
  gap: 2px;
  width: 100%;
  text-align: center;
}

.item-name {
  color: var(--text-primary);
  font-size: 0.9rem;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
}

.item-id {
  color: var(--accent-primary);
  font-size: 0.75rem;
  font-weight: 600;
}

.item-english {
  color: var(--text-muted);
  font-size: 0.7rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
}

.no-results {
  grid-column: 1 / -1;
  text-align: center;
  padding: 40px;
  color: var(--text-muted);
}

.empty-state {
  grid-column: 1 / -1;
  text-align: center;
  padding: 60px;
  color: var(--text-muted);
}

.empty-state p {
  margin: 0;
}

.empty-hint {
  font-size: 0.9rem;
  opacity: 0.7;
  margin-top: 8px !important;
}
</style>