<script setup>
import { ref, watch, onMounted, computed } from 'vue'
import ItemEditModal from './ItemEditModal.vue'
import { loadItemData } from '../api/itemDataApi.js'

const props = defineProps({
  inventory: {
    type: Array,
    default: () => []
  },
  loading: {
    type: Boolean,
    default: false
  },
  error: {
    type: String,
    default: ''
  },
  initialUsername: {
    type: String,
    default: ''
  },
  readonly: {
    type: Boolean,
    default: false
  },
  showHeader: {
    type: Boolean,
    default: true
  }
})

const emit = defineEmits(['fetch', 'editItem'])

const username = ref('lmx')

const showModal = ref(false)
const editingSlot = ref(-1)
const editingItem = ref(null)

const itemData = ref({ list: [], dict: {} })
const imageErrors = ref({})

watch(() => props.initialUsername, (newVal) => {
  if (newVal) {
    username.value = newVal
  }
}, { immediate: true })

const initItemData = async () => {
  itemData.value = await loadItemData()
}

const getItemInfo = (netID) => {
  if (!netID || netID <= 0) return null
  return itemData.value.dict[netID.toString()]
}

const getItemEnglishName = (netID) => {
  const info = getItemInfo(netID)
  return info ? info.english || '' : ''
}

const getWikiImageUrl = (netID) => {
  const englishName = getItemEnglishName(netID)
  if (!englishName) return ''
  const name = englishName.replace(/\s+/g, '_')
  return `https://terraria.wiki.gg/images/${name}.png`
}

const getItemImage = (netID) => {
  const hasError = imageErrors.value[netID]
  if (hasError) {
    const wikiUrl = getWikiImageUrl(netID)
    if (wikiUrl) return wikiUrl
  }
  return `/assets/img/img/Item_${netID}.png`
}

const handleImageError = (netID) => {
  imageErrors.value = { ...imageErrors.value, [netID]: true }
}

onMounted(() => {
  initItemData()
})

const handleFetch = () => {
  emit('fetch', username.value)
}

const handleCellClick = (slotIndex) => {
  if (props.readonly) return
  
  const item = props.inventory[slotIndex]
  editingSlot.value = slotIndex
  editingItem.value = item
  showModal.value = true
}

const handleEditSubmit = async (data) => {
  emit('editItem', {
    ...data,
    player: username.value
  })
  showModal.value = false
}

const handleModalClose = () => {
  showModal.value = false
}
</script>

<template>
  <div class="invsee-content">
    <div v-if="showHeader" class="section-header">
      <h2>背包查询</h2>
    </div>
    
    <div v-if="showHeader" class="invsee-input">
      <input
        v-model="username"
        type="text"
        placeholder="输入用户名"
        :disabled="loading"
        class="invsee-username-input"
      />
      <button @click="handleFetch" :disabled="loading" class="invsee-btn">
        {{ loading ? '查询中...' : '查询背包' }}
      </button>
    </div>
    
    <div v-if="error" class="invsee-error">
      {{ error }}
    </div>
    
    <div v-else-if="loading" class="loading-state">
      <p>加载中...</p>
    </div>
    
    <div v-else class="inventory-container">
      <div class="main-content-row">
        <div class="main-inventory-area">
          <div class="inventory-section main-bag-section">
            <div class="section-title">
              <img :src="getItemImage(5343)" alt="背包" class="title-icon" />
              <span>背包</span>
            </div>
            <div class="bag-with-extras">
              <div class="bag-grid">
                <div 
                  v-for="(item, index) in inventory.slice(0, 50)" 
                  :key="'inv-' + index" 
                  class="inventory-item clickable"
                  :class="{ empty: item.netID === 0 }"
                  :title="'序号: ' + index + ' (点击编辑)'"
                  @click="handleCellClick(index)"
                >
                  <img 
                    v-if="item.netID > 0"
                    :src="getItemImage(item.netID)" 
                    :alt="`Item ${item.netID}`"
                    class="item-image"
                    @error="(e) => { handleImageError(item.netID); }"
                  />
                  <div v-if="item.stack > 1" class="item-stack">{{ item.stack }}</div>
                  <div v-if="item.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ index }}</div>
                </div>
              </div>
              
              <div class="extra-column">
                <div 
                  class="extra-cell clickable" 
                  v-for="moneyIdx in [50, 51, 52, 53]" 
                  :key="'money-' + moneyIdx"
                  :title="'序号: ' + moneyIdx + ' (点击编辑)'"
                  @click="handleCellClick(moneyIdx)"
                >
                  <img 
                    v-if="inventory[moneyIdx]?.netID > 0"
                    :src="getItemImage(inventory[moneyIdx]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[moneyIdx]?.netID); }"
                  />
                  <div v-if="inventory[moneyIdx]?.stack > 1" class="item-stack">{{ inventory[moneyIdx]?.stack }}</div>
                  <div class="item-index">{{ moneyIdx }}</div>
                </div>
              </div>
              
              <div class="ammo-column">
                <div 
                  class="extra-cell clickable" 
                  v-for="ammoIdx in [54, 55, 56, 57]" 
                  :key="'ammo-' + ammoIdx"
                  :title="'序号: ' + ammoIdx + ' (点击编辑)'"
                  @click="handleCellClick(ammoIdx)"
                >
                  <img 
                    v-if="inventory[ammoIdx]?.netID > 0"
                    :src="getItemImage(inventory[ammoIdx]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[ammoIdx]?.netID); }"
                  />
                  <div v-if="inventory[ammoIdx]?.stack > 1" class="item-stack">{{ inventory[ammoIdx]?.stack }}</div>
                  <div class="item-index">{{ ammoIdx }}</div>
                </div>
              </div>
            </div>
          </div>
        </div>
        
        <div class="equipment-groups">
          <div class="inventory-section equipment-group">
            <div class="section-title">
              <img :src="getItemImage(5000)" alt="装备" class="title-icon" />
              <span>装备组1</span>
            </div>
            <div class="equipment-table">
              <div 
                v-for="index in 10" 
                :key="'row-' + index"
                class="equipment-row"
              >
                <div 
                  class="equipment-cell dye-cell clickable" 
                  :title="'序号: ' + (79 + index - 1) + ' (点击编辑)'"
                  @click="handleCellClick(79 + index - 1)"
                >
                  <img 
                    v-if="inventory[79 + index - 1]?.netID > 0"
                    :src="getItemImage(inventory[79 + index - 1]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[79 + index - 1]?.netID); }"
                  />
                  <div v-if="inventory[79 + index - 1]?.stack > 1" class="item-stack">{{ inventory[79 + index - 1]?.stack }}</div>
                  <div v-if="inventory[79 + index - 1]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 79 + index - 1 }}</div>
                </div>
                
                <div 
                  class="equipment-cell cosmetic-cell clickable" 
                  :title="'序号: ' + (69 + index - 1) + ' (点击编辑)'"
                  @click="handleCellClick(69 + index - 1)"
                >
                  <img 
                    v-if="inventory[69 + index - 1]?.netID > 0"
                    :src="getItemImage(inventory[69 + index - 1]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[69 + index - 1]?.netID); }"
                  />
                  <div v-if="inventory[69 + index - 1]?.stack > 1" class="item-stack">{{ inventory[69 + index - 1]?.stack }}</div>
                  <div v-if="inventory[69 + index - 1]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 69 + index - 1 }}</div>
                </div>
                
                <div 
                  class="equipment-cell armor-cell clickable" 
                  :title="'序号: ' + (59 + index - 1) + ' (点击编辑)'"
                  @click="handleCellClick(59 + index - 1)"
                >
                  <img 
                    v-if="inventory[59 + index - 1]?.netID > 0"
                    :src="getItemImage(inventory[59 + index - 1]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[59 + index - 1]?.netID); }"
                  />
                  <div v-if="inventory[59 + index - 1]?.stack > 1" class="item-stack">{{ inventory[59 + index - 1]?.stack }}</div>
                  <div v-if="inventory[59 + index - 1]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 59 + index - 1 }}</div>
                </div>
              </div>
            </div>
          </div>
          
          <div class="inventory-section equipment-group">
            <div class="section-title">
              <img :src="getItemImage(3623)" alt="副装备" class="title-icon" />
              <span>副装备</span>
            </div>
            <div class="equipment-table">
              <div 
                v-for="index in 5" 
                :key="'sub-' + index"
                class="equipment-row"
              >
                <div 
                  class="equipment-cell clickable" 
                  :title="'序号: ' + (94 + index - 1) + ' (点击编辑)'"
                  @click="handleCellClick(94 + index - 1)"
                >
                  <img 
                    v-if="inventory[94 + index - 1]?.netID > 0"
                    :src="getItemImage(inventory[94 + index - 1]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[94 + index - 1]?.netID); }"
                  />
                  <div v-if="inventory[94 + index - 1]?.stack > 1" class="item-stack">{{ inventory[94 + index - 1]?.stack }}</div>
                  <div v-if="inventory[94 + index - 1]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 94 + index - 1 }}</div>
                </div>
                
                <div 
                  class="equipment-cell clickable" 
                  :title="'序号: ' + (89 + index - 1) + ' (点击编辑)'"
                  @click="handleCellClick(89 + index - 1)"
                >
                  <img 
                    v-if="inventory[89 + index - 1]?.netID > 0"
                    :src="getItemImage(inventory[89 + index - 1]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[89 + index - 1]?.netID); }"
                  />
                  <div v-if="inventory[89 + index - 1]?.stack > 1" class="item-stack">{{ inventory[89 + index - 1]?.stack }}</div>
                  <div v-if="inventory[89 + index - 1]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 89 + index - 1 }}</div>
                </div>
              </div>
            </div>
          </div>
          
          <div class="inventory-section equipment-group">
            <div class="section-title">
              <img :src="getItemImage(5000)" alt="装备" class="title-icon" />
              <span>装备组2</span>
            </div>
            <div class="equipment-table">
              <div 
                v-for="index in 10" 
                :key="'row2-' + index"
                class="equipment-row"
              >
                <div 
                  class="equipment-cell dye-cell clickable" 
                  :title="'序号: ' + (309 + index) + ' (点击编辑)'"
                  @click="handleCellClick(309 + index)"
                >
                  <img 
                    v-if="inventory[309 + index]?.netID > 0"
                    :src="getItemImage(inventory[309 + index]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[309 + index]?.netID); }"
                  />
                  <div v-if="inventory[309 + index]?.stack > 1" class="item-stack">{{ inventory[309 + index]?.stack }}</div>
                  <div v-if="inventory[309 + index]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 309 + index }}</div>
                </div>
                
                <div 
                  class="equipment-cell cosmetic-cell clickable" 
                  :title="'序号: ' + (299 + index) + ' (点击编辑)'"
                  @click="handleCellClick(299 + index)"
                >
                  <img 
                    v-if="inventory[299 + index]?.netID > 0"
                    :src="getItemImage(inventory[299 + index]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[299 + index]?.netID); }"
                  />
                  <div v-if="inventory[299 + index]?.stack > 1" class="item-stack">{{ inventory[299 + index]?.stack }}</div>
                  <div v-if="inventory[299 + index]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 299 + index }}</div>
                </div>
                
                <div 
                  class="equipment-cell armor-cell clickable" 
                  :title="'序号: ' + (289 + index) + ' (点击编辑)'"
                  @click="handleCellClick(289 + index)"
                >
                  <img 
                    v-if="inventory[289 + index]?.netID > 0"
                    :src="getItemImage(inventory[289 + index]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[289 + index]?.netID); }"
                  />
                  <div v-if="inventory[289 + index]?.stack > 1" class="item-stack">{{ inventory[289 + index]?.stack }}</div>
                  <div v-if="inventory[289 + index]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 289 + index }}</div>
                </div>
              </div>
            </div>
          </div>
          
          <div class="inventory-section equipment-group">
            <div class="section-title">
              <img :src="getItemImage(5000)" alt="装备" class="title-icon" />
              <span>装备组3</span>
            </div>
            <div class="equipment-table">
              <div 
                v-for="index in 10" 
                :key="'row3-' + index"
                class="equipment-row"
              >
                <div 
                  class="equipment-cell dye-cell clickable" 
                  :title="'序号: ' + (339 + index) + ' (点击编辑)'"
                  @click="handleCellClick(339 + index)"
                >
                  <img 
                    v-if="inventory[339 + index]?.netID > 0"
                    :src="getItemImage(inventory[339 + index]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[339 + index]?.netID); }"
                  />
                  <div v-if="inventory[339 + index]?.stack > 1" class="item-stack">{{ inventory[339 + index]?.stack }}</div>
                  <div v-if="inventory[339 + index]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 339 + index }}</div>
                </div>
                
                <div 
                  class="equipment-cell cosmetic-cell clickable" 
                  :title="'序号: ' + (329 + index) + ' (点击编辑)'"
                  @click="handleCellClick(329 + index)"
                >
                  <img 
                    v-if="inventory[329 + index]?.netID > 0"
                    :src="getItemImage(inventory[329 + index]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[329 + index]?.netID); }"
                  />
                  <div v-if="inventory[329 + index]?.stack > 1" class="item-stack">{{ inventory[329 + index]?.stack }}</div>
                  <div v-if="inventory[329 + index]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 329 + index }}</div>
                </div>
                
                <div 
                  class="equipment-cell armor-cell clickable" 
                  :title="'序号: ' + (319 + index) + ' (点击编辑)'"
                  @click="handleCellClick(319 + index)"
                >
                  <img 
                    v-if="inventory[319 + index]?.netID > 0"
                    :src="getItemImage(inventory[319 + index]?.netID)" 
                    class="item-image"
                    @error="(e) => { handleImageError(inventory[319 + index]?.netID); }"
                  />
                  <div v-if="inventory[319 + index]?.stack > 1" class="item-stack">{{ inventory[319 + index]?.stack }}</div>
                  <div v-if="inventory[319 + index]?.favorited" class="item-favorite">⭐</div>
                  <div class="item-index">{{ 319 + index }}</div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
      
      <div class="extra-storage-groups">
        <div class="inventory-section">
          <div class="section-title">
            <img :src="getItemImage(3213)" alt="存储罐" class="title-icon" />
            <span>存储罐</span>
          </div>
          <div class="inventory-grid main-inventory">
            <div 
              v-for="index in 40" 
              :key="'safe-' + (99 + index - 1)"
              class="inventory-item clickable"
              :class="{ empty: inventory[99 + index - 1]?.netID === 0 }"
              :title="'序号: ' + (99 + index - 1) + ' (点击编辑)'"
              @click="handleCellClick(99 + index - 1)"
            >
              <img 
                v-if="inventory[99 + index - 1]?.netID > 0"
                :src="getItemImage(inventory[99 + index - 1]?.netID)" 
                class="item-image"
                @error="(e) => { handleImageError(inventory[99 + index - 1]?.netID); }"
              />
              <div v-if="inventory[99 + index - 1]?.stack > 1" class="item-stack">{{ inventory[99 + index - 1]?.stack }}</div>
              <div v-if="inventory[99 + index - 1]?.favorited" class="item-favorite">⭐</div>
              <div class="item-index">{{ 99 + index - 1 }}</div>
            </div>
          </div>
        </div>
        
        <div class="inventory-section">
          <div class="section-title">
            <img :src="getItemImage(346)" alt="保险柜" class="title-icon" />
            <span>保险柜</span>
          </div>
          <div class="inventory-grid main-inventory">
            <div 
              v-for="index in 40" 
              :key="'vault-' + (139 + index - 1)"
              class="inventory-item clickable"
              :class="{ empty: inventory[139 + index - 1]?.netID === 0 }"
              :title="'序号: ' + (139 + index - 1) + ' (点击编辑)'"
              @click="handleCellClick(139 + index - 1)"
            >
              <img 
                v-if="inventory[139 + index - 1]?.netID > 0"
                :src="getItemImage(inventory[139 + index - 1]?.netID)" 
                class="item-image"
                @error="(e) => { handleImageError(inventory[139 + index - 1]?.netID); }"
              />
              <div v-if="inventory[139 + index - 1]?.stack > 1" class="item-stack">{{ inventory[139 + index - 1]?.stack }}</div>
              <div v-if="inventory[139 + index - 1]?.favorited" class="item-favorite">⭐</div>
              <div class="item-index">{{ 139 + index - 1 }}</div>
            </div>
          </div>
        </div>
        
        <div class="inventory-section">
          <div class="section-title">
            <img :src="getItemImage(3813)" alt="护卫熔炉" class="title-icon" />
            <span>护卫熔炉</span>
          </div>
          <div class="inventory-grid main-inventory">
            <div 
              v-for="index in 40" 
              :key="'forge-' + (180 + index - 1)"
              class="inventory-item clickable"
              :class="{ empty: inventory[180 + index - 1]?.netID === 0 }"
              :title="'序号: ' + (180 + index - 1) + ' (点击编辑)'"
              @click="handleCellClick(180 + index - 1)"
            >
              <img 
                v-if="inventory[180 + index - 1]?.netID > 0"
                :src="getItemImage(inventory[180 + index - 1]?.netID)" 
                class="item-image"
                @error="(e) => { handleImageError(inventory[180 + index - 1]?.netID); }"
              />
              <div v-if="inventory[180 + index - 1]?.stack > 1" class="item-stack">{{ inventory[180 + index - 1]?.stack }}</div>
              <div v-if="inventory[180 + index - 1]?.favorited" class="item-favorite">⭐</div>
              <div class="item-index">{{ 180 + index - 1 }}</div>
            </div>
          </div>
        </div>
        
        <div class="inventory-section">
          <div class="section-title">
            <img :src="getItemImage(4131)" alt="虚空宝库" class="title-icon" />
            <span>虚空宝库</span>
          </div>
          <div class="inventory-grid main-inventory">
            <div 
              v-for="index in 40" 
              :key="'void-' + (220 + index - 1)"
              class="inventory-item clickable"
              :class="{ empty: inventory[220 + index - 1]?.netID === 0 }"
              :title="'序号: ' + (220 + index - 1) + ' (点击编辑)'"
              @click="handleCellClick(220 + index - 1)"
            >
              <img 
                v-if="inventory[220 + index - 1]?.netID > 0"
                :src="getItemImage(inventory[220 + index - 1]?.netID)" 
                class="item-image"
                @error="(e) => { handleImageError(inventory[220 + index - 1]?.netID); }"
              />
              <div v-if="inventory[220 + index - 1]?.stack > 1" class="item-stack">{{ inventory[220 + index - 1]?.stack }}</div>
              <div v-if="inventory[220 + index - 1]?.favorited" class="item-favorite">⭐</div>
              <div class="item-index">{{ 220 + index - 1 }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <ItemEditModal
      :show="showModal"
      :slot-index="editingSlot"
      :initial-item-id="editingItem?.netID || 0"
      :initial-stack="editingItem?.stack || 1"
      :initial-prefix="editingItem?.prefix || 0"
      :title="'编辑背包: 格子 ' + editingSlot"
      @close="handleModalClose"
      @submit="handleEditSubmit"
    />
  </div>
</template>

<style scoped>
.invsee-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 0;
  overflow-y: auto;
  max-height: calc(100vh - 140px);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
  padding: 0 20px;
}

.section-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.4rem;
  font-weight: 600;
}

.invsee-input {
  display: flex;
  gap: 14px;
  margin-bottom: 20px;
  padding: 0 20px;
}

.invsee-username-input {
  flex: 1;
  max-width: 400px;
  padding: 14px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-lg);
  color: var(--text-primary);
  font-size: 1rem;
  transition: all 0.25s ease;
}

.invsee-username-input:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.invsee-username-input::placeholder {
  color: var(--text-muted);
}

.invsee-btn {
  padding: 14px 28px;
  background: linear-gradient(135deg, var(--accent-secondary), #16a34a);
  color: white;
  border: none;
  border-radius: var(--radius-lg);
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-md);
}

.invsee-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: var(--shadow-lg);
}

.invsee-btn:disabled {
  background: var(--bg-hover);
  color: var(--text-muted);
  cursor: not-allowed;
  box-shadow: none;
}

.invsee-error {
  padding: 14px 16px;
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border-radius: var(--radius-lg);
  margin-bottom: 16px;
  margin-left: 20px;
  margin-right: 20px;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.loading-state {
  text-align: center;
  padding: 60px 20px;
  color: var(--text-muted);
}

.inventory-container {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 20px;
  overflow-y: auto;
  padding: 0 20px 20px;
}

.main-content-row {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  justify-content: center;
  align-items: flex-start;
}

.main-inventory-area {
  flex: 0 0 auto;
}

.main-inventory-area .inventory-section {
  flex: 0 0 auto;
}

.equipment-groups {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  justify-content: flex-start;
}

.main-bag-section .bag-with-extras {
  display: flex;
  gap: 6px;
}

.bag-grid {
  display: grid;
  grid-template-columns: repeat(10, 44px);
  gap: 6px;
}

.extra-column,
.ammo-column {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.extra-cell {
  width: 44px;
  height: 44px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s ease;
}

.extra-cell:hover {
  border-color: var(--accent-secondary);
  transform: scale(1.05);
  box-shadow: var(--shadow-md);
}

.extra-storage-groups {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  justify-content: center;
}

.extra-storage-groups .inventory-section {
  flex: 0 0 auto;
}

.equipment-group {
  flex: 0 0 auto;
  min-width: 160px;
}

.inventory-section {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 16px;
  box-shadow: var(--shadow-md);
  border: 1px solid var(--border-light);
}

.equipment-table {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.equipment-row {
  display: flex;
  flex-direction: row;
  gap: 6px;
}

.equipment-cell {
  width: 44px;
  height: 44px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s ease;
}

.equipment-cell.empty {
  background: transparent;
  border: 1px dashed var(--border-color);
}

.equipment-cell:hover {
  border-color: var(--accent-secondary);
  transform: scale(1.05);
  box-shadow: var(--shadow-md);
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 14px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--border-light);
  color: var(--text-primary);
  font-weight: 600;
  font-size: 0.95rem;
}

.title-icon {
  width: 24px;
  height: 24px;
  image-rendering: pixelated;
}

.inventory-grid {
  display: grid;
  grid-template-columns: repeat(10, 44px);
  gap: 6px;
  justify-content: center;
}

.main-inventory {
  background: transparent;
  padding: 0;
  border-radius: 0;
  overflow-y: visible;
  max-height: none;
}

.inventory-item {
  width: 44px;
  height: 44px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s ease;
}

.inventory-item.empty {
  background: transparent;
  border: 1px dashed var(--border-color);
}

.inventory-item.clickable,
.equipment-cell.clickable,
.extra-cell.clickable {
  cursor: pointer;
}

.inventory-item.clickable:hover,
.equipment-cell.clickable:hover,
.extra-cell.clickable:hover {
  border-color: var(--accent-primary);
  background: var(--bg-hover);
}

.item-image {
  width: 85%;
  height: 85%;
  object-fit: contain;
  image-rendering: pixelated;
}

.item-stack {
  position: absolute;
  bottom: 2px;
  right: 4px;
  font-size: 0.75rem;
  font-weight: 700;
  color: white;
  text-shadow: 1px 1px 2px rgba(0, 0, 0, 0.8);
}

.item-favorite {
  position: absolute;
  top: 2px;
  left: 2px;
  font-size: 0.85rem;
}

.item-index {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  font-size: 0.65rem;
  color: white;
  background: rgba(0, 0, 0, 0.8);
  padding: 2px 5px;
  border-radius: var(--radius-sm);
  opacity: 0;
  transition: opacity 0.2s;
  pointer-events: none;
  font-weight: 500;
}

.inventory-item:hover .item-index,
.equipment-cell:hover .item-index,
.extra-cell:hover .item-index {
  opacity: 1;
}

/* ── 移动端 ── */
@media (max-width: 767px) {
  .bag-grid,
  .inventory-grid {
    grid-template-columns: repeat(10, 1fr) !important;
    gap: 3px;
  }
  .inventory-item,
  .equipment-cell,
  .extra-cell {
    width: auto;
    height: auto;
    aspect-ratio: 1;
    min-width: 0;
  }
  .inventory-item img,
  .equipment-cell img,
  .extra-cell img {
    width: 75%;
    height: 75%;
  }
  .main-inventory-area {
    flex: 1 1 100% !important;
    width: 100%;
  }
  .main-inventory-area .inventory-section {
    flex: 1 1 100% !important;
  }
  .main-bag-section .bag-with-extras {
    flex-wrap: wrap;
    gap: 3px;
  }
  .bag-with-extras .bag-grid {
    width: 100%;
  }
  .bag-with-extras .extra-column,
  .bag-with-extras .ammo-column {
    flex-direction: row;
    gap: 3px;
    width: calc(50% - 2px);
  }
  .bag-with-extras .extra-cell {
    flex: 1;
    width: auto;
    height: auto;
    aspect-ratio: 1;
  }
  .extra-column,
  .ammo-column {
    gap: 3px;
  }
  .inventory-container {
    padding: 0 8px 8px;
    overflow-x: auto;
  }
  .invsee-input {
    flex-direction: column;
    padding: 0 12px;
    gap: 8px;
  }
  .invsee-username-input {
    max-width: none;
  }
  .section-header {
    padding: 0 12px;
  }
  .invsee-error {
    margin-left: 12px;
    margin-right: 12px;
  }
  .equipment-group {
    min-width: 0;
    flex: 0 1 calc(50% - 8px);
  }
  .equipment-cell {
    width: 100%;
  }
  .extra-storage-groups .inventory-section {
    flex: 1 1 100%;
  }
  .extra-storage-groups .inventory-grid {
    grid-template-columns: repeat(10, 1fr) !important;
  }
  .inventory-section {
    padding: 10px;
  }
  .section-title {
    font-size: 0.8rem;
    margin-bottom: 8px;
    padding-bottom: 6px;
  }
  .item-index {
    font-size: 0.5rem;
    padding: 1px 3px;
  }
  .item-stack {
    font-size: 0.6rem;
  }
  .equipment-row {
    gap: 3px;
  }
}
</style>