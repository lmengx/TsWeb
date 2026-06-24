<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { loadItemData } from '../api/itemDataApi.js'

const props = defineProps({
  show: {
    type: Boolean,
    default: false
  },
  mode: {
    type: String,
    default: 'give'
  },
  slotIndex: {
    type: Number,
    default: -1
  },
  initialItemId: {
    type: Number,
    default: 0
  },
  initialStack: {
    type: Number,
    default: 1
  },
  initialPrefix: {
    type: Number,
    default: 0
  },
  title: {
    type: String,
    default: '编辑物品'
  }
})

const emit = defineEmits(['close', 'submit'])

const itemSearchQuery = ref('')
const selectedItemId = ref(props.initialItemId || 0)
const stack = ref(props.initialStack || 1)
const prefixId = ref('')
const loading = ref(false)
const error = ref('')
const success = ref('')
const warning = ref('')
const showPrefixDropdown = ref(false)
const showItemDropdown = ref(false)
const itemData = ref({ list: [], dict: {} })

const prefixMap = {
  '大': 1, '巨大': 2, '危险': 3, '凶残': 4, '锋利': 5, '尖锐': 6, '微小': 7, '可怕': 8,
  '小': 9, '钝': 10, '倒霉': 11, '笨重': 12, '可耻': 13, '重': 14, '轻': 15, '精准': 16,
  '迅速': 17, '急速': 18, '恐怖': 19, '致命': 20, '可靠': 21, '讨厌': 22, '无力': 23,
  '粗笨': 24, '强大': 25, '神秘': 26, '精巧': 27, '精湛': 28, '笨拙': 29, '无知': 30,
  '错乱': 31, '威猛': 32, '禁忌': 33, '天界': 34, '狂怒': 35, '锐利': 36, '高端': 37,
  '强力': 38, '碎裂': 39, '破损': 40, '粗劣': 41, '迅捷': 42, '灵活': 43, '灵巧': 44,
  '残暴': 45, '缓慢': 46, '迟钝': 47, '呆滞': 48, '惹恼': 49, '凶险': 50, '狂躁': 51,
  '致伤': 52, '强劲': 53, '粗鲁': 54, '虚弱': 55, '无情': 56, '暴怒': 57, '神级': 58,
  '恶魔': 59, '狂热': 60, '坚硬': 61, '守护': 62, '装甲': 63, '护佑': 64, '奥秘': 65,
  '精确': 66, '幸运': 67, '锯齿': 68, '尖刺': 69, '愤怒': 70, '险恶': 71, '轻快': 72,
  '快速': 73, '弹道': 74, '虬结': 75
}

watch(() => props.show, (newVal) => {
  if (newVal) {
    selectedItemId.value = props.initialItemId || 0
    itemSearchQuery.value = ''
    stack.value = props.initialStack || 1
    prefixId.value = props.initialPrefix ? Object.keys(prefixMap).find(k => prefixMap[k] === props.initialPrefix) || '' : ''
    error.value = ''
    success.value = ''
    warning.value = ''
    showPrefixDropdown.value = false
    showItemDropdown.value = false
  }
})

const initItemData = async () => {
  itemData.value = await loadItemData()
}

const itemSearchResults = computed(() => {
  if (!itemSearchQuery.value.trim()) return []

  const query = itemSearchQuery.value.trim().toLowerCase()
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
    return exactResults.slice(0, 20)
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
    .slice(0, 20)
})

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

const selectedItemInfo = computed(() => {
  if (!selectedItemId.value || selectedItemId.value <= 0) return null
  return itemData.value.dict[selectedItemId.value.toString()]
})

const itemImageUrl = computed(() => {
  if (!selectedItemId.value || isNaN(parseInt(selectedItemId.value))) return null
  return `/assets/img/img/Item_${selectedItemId.value}.png`
})

const itemName = computed(() => {
  const info = selectedItemInfo.value
  return info ? info.chinese || '' : ''
})

const englishName = computed(() => {
  const info = selectedItemInfo.value
  return info ? info.english || '' : ''
})

const wikiImageUrl = computed(() => {
  if (!englishName.value) return ''
  const name = englishName.value.replace(/\s+/g, '_')
  return `https://terraria.wiki.gg/images/${name}.png`
})

const imageError = ref(false)

const handleImageError = () => {
  imageError.value = true
}

const currentImageUrl = computed(() => {
  if (imageError.value && wikiImageUrl.value) {
    return wikiImageUrl.value
  }
  return itemImageUrl.value
})

const selectItem = (item) => {
  selectedItemId.value = item.id
  itemSearchQuery.value = item.chinese
  showItemDropdown.value = false
}

const filteredPrefixes = computed(() => {
  const input = prefixId.value.trim()
  if (!input) return []
  const isNumericInput = /^\d+$/.test(input)
  return Object.entries(prefixMap)
    .filter(([name, id]) => {
      if (isNumericInput) {
        return String(id).includes(input)
      }
      return name.includes(input)
    })
    .map(([name, id]) => ({ name, id }))
})

const selectPrefix = (prefix) => {
  prefixId.value = prefix.name
  showPrefixDropdown.value = false
}

const getPrefixIdValue = () => {
  const input = prefixId.value.trim()
  if (!input) return 0
  if (prefixMap[input] !== undefined) {
    return prefixMap[input]
  }
  if (/^\d+$/.test(input)) {
    return parseInt(input)
  }
  return 0
}

const handleSubmit = () => {
  if (!selectedItemId.value || selectedItemId.value <= 0) {
    error.value = '请选择有效的物品'
    return
  }

  const prefixValue = getPrefixIdValue()
  if (prefixId.value.trim() && prefixValue === 0 && !/^\d+$/.test(prefixId.value.trim())) {
    warning.value = `前缀"${prefixId.value}"无法识别，将使用默认值0`
  }

  emit('submit', {
    itemId: parseInt(selectedItemId.value),
    stack: parseInt(stack.value),
    prefix: prefixValue,
    slotIndex: props.slotIndex
  })
}

const close = () => {
  emit('close')
}

onMounted(() => {
  initItemData()
})
</script>

<template>
  <div v-if="show" class="modal-overlay" @click.self="close">
    <div class="modal">
      <div class="modal-header">
        <h3>{{ title }}</h3>
        <button @click="close" class="close-btn">×</button>
      </div>
      <div class="modal-body">
        <div class="edit-form">
          <div class="form-row">
            <label>物品</label>
            <div class="item-input-row">
              <div class="search-input-wrapper">
                <input
                  v-model="itemSearchQuery"
                  type="text"
                  placeholder="搜索物品（名称或ID）..."
                  class="form-input"
                  @input="showItemDropdown = true"
                  @focus="showItemDropdown = true"
                  @blur="setTimeout(() => showItemDropdown = false, 150)"
                />
                <div v-if="showItemDropdown && itemSearchResults.length > 0" class="item-dropdown">
                  <div
                    v-for="item in itemSearchResults"
                    :key="item.id"
                    class="item-option"
                    @click="selectItem(item)"
                  >
                    <img :src="`/assets/img/img/Item_${item.id}.png`" :alt="item.chinese" class="option-image" @error="(e) => e.target.style.display = 'none'" />
                    <div class="option-info">
                      <span class="option-name">{{ item.chinese }}</span>
                      <span class="option-id">ID: {{ item.id }}</span>
                    </div>
                  </div>
                </div>
              </div>
              <div class="item-preview-wrapper">
                <div v-if="currentImageUrl" class="item-preview">
                  <img
                    :src="currentImageUrl"
                    :alt="itemName"
                    class="item-image"
                    @error="handleImageError"
                  />
                </div>
                <div v-if="itemName" class="item-name">
                  {{ itemName }}
                </div>
              </div>
            </div>
          </div>
          <div class="form-row">
            <label>堆叠数</label>
            <input
              v-model="stack"
              type="number"
              min="1"
              class="form-input"
            />
          </div>
          <div class="form-row">
            <label>前缀ID</label>
            <input
              v-model="prefixId"
              type="text"
              placeholder="可选，默认无，可输入中文搜索"
              class="form-input"
              @input="warning = ''; showPrefixDropdown = true"
              @focus="showPrefixDropdown = true"
              @blur="setTimeout(() => showPrefixDropdown = false, 150)"
            />
            <div v-if="showPrefixDropdown && filteredPrefixes.length > 0" class="prefix-dropdown">
              <div
                v-for="prefix in filteredPrefixes"
                :key="prefix.id"
                class="prefix-option"
                @click="selectPrefix(prefix)"
              >
                <span class="prefix-name">{{ prefix.name }}</span>
                <span class="prefix-id">ID: {{ prefix.id }}</span>
              </div>
            </div>
          </div>
        </div>

        <div v-if="error" class="edit-error">
          {{ error }}
        </div>
        <div v-if="warning" class="edit-warning">
          {{ warning }}
        </div>
        <div v-if="success" class="edit-success">
          {{ success }}
        </div>
      </div>
      <div class="modal-footer">
        <button @click="close" class="cancel-btn">取消</button>
        <button @click="handleSubmit" :disabled="loading" class="submit-btn">
          {{ loading ? '执行中...' : '确认' }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.7);
  backdrop-filter: blur(8px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  width: 90%;
  max-width: 500px;
  max-height: 85vh;
  overflow: hidden;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
  animation: modalIn 0.25s ease;
}

@keyframes modalIn {
  from {
    opacity: 0;
    transform: scale(0.95) translateY(-20px);
  }
  to {
    opacity: 1;
    transform: scale(1) translateY(0);
  }
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 20px;
  background: var(--bg-tertiary);
  border-bottom: 1px solid var(--border-light);
}

.modal-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.25rem;
  font-weight: 600;
}

.close-btn {
  background: var(--bg-hover);
  border: none;
  color: var(--text-secondary);
  font-size: 1.25rem;
  cursor: pointer;
  padding: 6px 10px;
  border-radius: var(--radius-sm);
  transition: all 0.2s ease;
  line-height: 1;
}

.close-btn:hover {
  color: var(--text-primary);
  background: var(--bg-card);
}

.modal-body {
  padding: 24px;
}

.edit-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 8px;
  position: relative;
}

.form-row label {
  color: var(--text-secondary);
  font-weight: 600;
  font-size: 0.9rem;
}

.item-input-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

.search-input-wrapper {
  flex: 1;
  position: relative;
}

.item-preview-wrapper {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
}

.item-name {
  font-size: 0.85rem;
  color: var(--accent-secondary);
  font-weight: 500;
  text-align: center;
  max-width: 80px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.form-input {
  width: 100%;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  transition: all 0.25s ease;
  box-sizing: border-box;
}

.form-input:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.form-input::placeholder {
  color: var(--text-muted);
}

.item-preview {
  width: 44px;
  height: 44px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  display: flex;
  align-items: center;
  justify-content: center;
}

.item-image {
  width: 85%;
  height: 85%;
  object-fit: contain;
  image-rendering: pixelated;
}

.item-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  background: var(--bg-card);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  max-height: 250px;
  overflow-y: auto;
  z-index: 20;
  box-shadow: var(--shadow-md);
}

.item-option {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  cursor: pointer;
  transition: background 0.2s ease;
  border-bottom: 1px solid var(--border-light);
}

.item-option:last-child {
  border-bottom: none;
}

.item-option:hover {
  background: var(--bg-hover);
}

.option-image {
  width: 32px;
  height: 32px;
  object-fit: contain;
  image-rendering: pixelated;
  border-radius: var(--radius-sm);
}

.option-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.option-name {
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.9rem;
}

.option-id {
  color: var(--text-muted);
  font-size: 0.75rem;
}

.prefix-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  background: var(--bg-card);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  margin-top: 4px;
  max-height: 200px;
  overflow-y: auto;
  z-index: 10;
  box-shadow: var(--shadow-md);
}

.prefix-option {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  cursor: pointer;
  transition: background 0.2s ease;
  border-bottom: 1px solid var(--border-light);
}

.prefix-option:last-child {
  border-bottom: none;
}

.prefix-option:hover {
  background: var(--bg-hover);
}

.prefix-name {
  color: var(--text-primary);
  font-weight: 500;
}

.prefix-id {
  color: var(--text-muted);
  font-size: 0.85rem;
}

.edit-error {
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border-radius: var(--radius-md);
  margin-top: 16px;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.edit-warning {
  padding: 12px 16px;
  background: rgba(234, 179, 8, 0.15);
  color: #ca8a04;
  border-radius: var(--radius-md);
  margin-top: 16px;
  border: 1px solid rgba(234, 179, 8, 0.3);
}

.edit-success {
  padding: 12px 16px;
  background: rgba(34, 197, 94, 0.15);
  color: var(--accent-secondary);
  border-radius: var(--radius-md);
  margin-top: 16px;
  border: 1px solid rgba(34, 197, 94, 0.3);
}

.modal-footer {
  padding: 16px 24px;
  background: var(--bg-tertiary);
  border-top: 1px solid var(--border-light);
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

.cancel-btn {
  padding: 12px 24px;
  background: var(--bg-hover);
  color: var(--text-primary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 500;
  transition: all 0.25s ease;
}

.cancel-btn:hover {
  background: var(--bg-card);
  border-color: var(--accent-primary);
}

.submit-btn {
  padding: 12px 24px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.submit-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: var(--shadow-md);
}

.submit-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>