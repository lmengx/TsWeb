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

/* 前缀名称 → ID 对照表。
 * 与游戏数据对齐（PrefixID 1-84），名称取自 zh-Hans 的 Prefix 表。
 * 注意：同名不同 ID 是正常的（如 59/81 神级与传奇并不重名，但 74/76 都叫急速、75/77 都叫迅捷），
 * 反查时取第一个命中的 ID 即可。 */
const prefixList = [
  { id: 1, name: '大' }, { id: 2, name: '巨大' }, { id: 3, name: '危险' }, { id: 4, name: '凶残' },
  { id: 5, name: '锋利' }, { id: 6, name: '尖锐' }, { id: 7, name: '微小' }, { id: 8, name: '可怕' },
  { id: 9, name: '小' }, { id: 10, name: '钝' }, { id: 11, name: '倒霉' }, { id: 12, name: '笨重' },
  { id: 13, name: '可耻' }, { id: 14, name: '重' }, { id: 15, name: '轻' }, { id: 16, name: '精准' },
  { id: 17, name: '迅速' }, { id: 18, name: '急速' }, { id: 19, name: '恐怖' }, { id: 20, name: '致命' },
  { id: 21, name: '可靠' }, { id: 22, name: '讨厌' }, { id: 23, name: '无力' }, { id: 24, name: '粗笨' },
  { id: 25, name: '强大' }, { id: 26, name: '神秘' }, { id: 27, name: '精巧' }, { id: 28, name: '精湛' },
  { id: 29, name: '笨拙' }, { id: 30, name: '无知' }, { id: 31, name: '错乱' }, { id: 32, name: '威猛' },
  { id: 33, name: '禁忌' }, { id: 34, name: '天界' }, { id: 35, name: '狂怒' }, { id: 36, name: '锐利' },
  { id: 37, name: '高端' }, { id: 38, name: '强力' }, { id: 39, name: '碎裂' }, { id: 40, name: '破损' },
  { id: 41, name: '粗劣' }, { id: 42, name: '迅捷' }, { id: 43, name: '致命' }, { id: 44, name: '灵活' },
  { id: 45, name: '灵巧' }, { id: 46, name: '残暴' }, { id: 47, name: '缓慢' }, { id: 48, name: '迟钝' },
  { id: 49, name: '呆滞' }, { id: 50, name: '惹恼' }, { id: 51, name: '凶险' }, { id: 52, name: '狂躁' },
  { id: 53, name: '致伤' }, { id: 54, name: '强劲' }, { id: 55, name: '粗鲁' }, { id: 56, name: '虚弱' },
  { id: 57, name: '无情' }, { id: 58, name: '暴怒' }, { id: 59, name: '神级' }, { id: 60, name: '恶魔' },
  { id: 61, name: '狂热' }, { id: 62, name: '坚硬' }, { id: 63, name: '守护' }, { id: 64, name: '装甲' },
  { id: 65, name: '护佑' }, { id: 66, name: '奥秘' }, { id: 67, name: '精确' }, { id: 68, name: '幸运' },
  { id: 69, name: '锯齿' }, { id: 70, name: '尖刺' }, { id: 71, name: '愤怒' }, { id: 72, name: '险恶' },
  { id: 73, name: '轻快' }, { id: 74, name: '快速' }, { id: 75, name: '急速' }, { id: 76, name: '迅捷' },
  { id: 77, name: '狂野' }, { id: 78, name: '鲁莽' }, { id: 79, name: '勇猛' }, { id: 80, name: '暴力' },
  { id: 81, name: '传奇' }, { id: 82, name: '虚幻' }, { id: 83, name: '神话' }, { id: 84, name: '传奇' }
]

/* name → id：同名取**最小 ID**（84 传奇 与 81 传奇 同名，反查应得 81）。
 * 用 sort 保证与数组书写顺序无关，避免以后插项时反查结果漂移。 */
const prefixMap = prefixList
  .slice()
  .sort((a, b) => a.id - b.id)
  .reduce((map, { id, name }) => {
    if (map[name] === undefined) map[name] = id
    return map
  }, {})

/* id → name：查不到返回 null。 */
const prefixNameOf = (id) => (prefixList.find((p) => p.id === id)?.name ?? null)

/* 重名 ID 集合：游戏中确实存在同名不同 ID 的前缀（20/43 致命、18/75 急速、
 * 42/76 迅捷、81/84 传奇）。这类 ID 只显示名称会丢失信息——编辑一次就被降级成同名的最小 ID，
 * 所以输入框里改用纯数字 ID 回填，保证"打开再保存"不改变原值。 */
const duplicatePrefixNames = (() => {
  const count = {}
  prefixList.forEach(({ name }) => { count[name] = (count[name] || 0) + 1 })
  return new Set(Object.keys(count).filter((n) => count[n] > 1))
})()

/* 已存前缀 → 输入框显示值：唯一名显示中文，重名或未知则显示数字 ID。 */
const prefixInputValue = (id) => {
  if (!id) return ''
  const name = prefixNameOf(id)
  if (name && !duplicatePrefixNames.has(name)) return name
  return String(id)
}

watch(() => props.show, (newVal) => {
  if (newVal) {
    selectedItemId.value = props.initialItemId || 0
    itemSearchQuery.value = ''
    stack.value = props.initialStack || 1
    prefixId.value = prefixInputValue(props.initialPrefix || 0)
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

/* 输入为空时列出全部（聚焦即可见）；否则按中文名或数字 ID 过滤。 */
const filteredPrefixes = computed(() => {
  const input = prefixId.value.trim()
  if (!input) return prefixList

  const isNumericInput = /^\d+$/.test(input)
  return prefixList.filter(({ id, name }) =>
    isNumericInput ? String(id).includes(input) : name.includes(input)
  )
})

const selectPrefix = (prefix) => {
  prefixId.value = prefix.name
  showPrefixDropdown.value = false
}

const hidePrefixDropdown = () => {
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

const handleClear = () => {
  // 点击"清空"后表单立即清空，按确认按钮后生效
  selectedItemId.value = 0
  itemSearchQuery.value = ''
  stack.value = 0
  prefixId.value = ''
  error.value = ''
  warning.value = ''
  success.value = ''
  showItemDropdown.value = false
  showPrefixDropdown.value = false
}

const handleSubmit = () => {
  const prefixValue = getPrefixIdValue()
  if (prefixId.value.trim() && prefixValue === 0 && !/^\d+$/.test(prefixId.value.trim())) {
    warning.value = `前缀"${prefixId.value}"无法识别，将使用默认值0`
  }

  // itemId 为 0 表示清空该格子（表单已点"清空"或格子原本为空）
  emit('submit', {
    itemId: parseInt(selectedItemId.value) || 0,
    stack: parseInt(stack.value) || 0,
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
                <div class="search-input-line">
                  <input
                    v-model="itemSearchQuery"
                    type="text"
                    placeholder="搜索物品（名称或ID）..."
                    class="form-input"
                    @input="showItemDropdown = true"
                    @focus="showItemDropdown = true"
                    @blur="setTimeout(() => showItemDropdown = false, 150)"
                  />
                  <button @click="handleClear" class="clear-slot-btn" title="清空此格子">清空</button>
                </div>
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
                <div class="item-preview">
                  <img
                    v-if="currentImageUrl"
                    :src="currentImageUrl"
                    :alt="itemName"
                    class="item-image"
                    @error="handleImageError"
                  />
                </div>
                <div class="item-name">{{ itemName || '未选择物品' }}</div>
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
              @blur="hidePrefixDropdown"
            />
            <div v-if="showPrefixDropdown && filteredPrefixes.length > 0" class="prefix-dropdown">
              <div
                v-for="prefix in filteredPrefixes"
                :key="prefix.id"
                class="prefix-option"
                @mousedown.prevent
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

.search-input-line {
  display: flex;
  gap: 8px;
  align-items: stretch;
}

.search-input-line .form-input {
  flex: 1;
  min-width: 0;
}

.clear-slot-btn {
  flex-shrink: 0;
  padding: 0 18px;
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border: 2px solid rgba(239, 68, 68, 0.4);
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
  white-space: nowrap;
}

.clear-slot-btn:hover {
  background: var(--accent-error);
  color: white;
  border-color: var(--accent-error);
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