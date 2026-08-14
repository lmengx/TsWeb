<script setup>
import { ref, computed, onMounted } from 'vue'
import { get, post } from '../../utils/api.js'
import ItemSearchDialog from '../../components/ItemSearchDialog.vue'
import { loadItemData } from '../../api/itemDataApi.js'

const loading = ref(true)
const saving = ref(false)
const error = ref('')
const success = ref('')
const itemData = ref({ list: [], dict: {} })
const imageErrorIds = ref(new Set())

// ═════════ 配置数据（结构 = 后端 ShopUIConfig JSON schema） ═════════
const config = ref({
  enabled: true,
  summonItemId: 3500,
  statueControls: [], // [{ slot, statueItemId, targetShopIndex, name }]
  shops: []          // [{ name, items: [{ itemId, price, stack, condition }] }]
})

const slots40 = Array.from({ length: 40 }, (_, i) => i)

// 控件占格集合 / 按 slot 找控件
const controlBySlot = (slot) => config.value.statueControls.find(c => c.slot === slot)
const controlSlots = computed(() => new Set(config.value.statueControls.map(c => c.slot)))
// 商品区 = 40 格中非控件格（有序），控件优先级高于商品
const goodsSlots = computed(() => slots40.filter(s => !controlSlots.value.has(s)))

// 商店面板格子 hover 提示
const cellTitle = (shop, slot) => {
  const c = controlBySlot(slot)
  if (c) return '控件：' + getItemName(c.statueItemId) + '（顶部控件面板编辑）'
  const gi = goodsSlots.value.indexOf(slot)
  const item = shop.items[gi]
  return item ? getItemName(item.itemId) : '点击选择物品添加商品'
}

// 选中项（编辑面板）：null | { type:'control', slot } | { type:'item', shopIndex, goodsIndex }
const selected = ref(null)
const selectedControl = computed(() =>
  selected.value?.type === 'control' ? controlBySlot(selected.value.slot) : null)
const selectedItem = computed(() => {
  const s = selected.value
  if (s?.type !== 'item') return null
  return config.value.shops[s.shopIndex]?.items[s.goodsIndex] ?? null
})

// Boss flag 列表（对应插件 ShopUIConfigManager.EvalBossFlag）
const bossFlags = [
  { flag: 'downedSlimeKing', name: '史莱姆王' },
  { flag: 'downedBoss1', name: '克眼' },
  { flag: 'downedQueenBee', name: '蜂后' },
  { flag: 'downedBoss3', name: '骷髅王' },
  { flag: 'downedMechBoss1', name: '毁灭者' },
  { flag: 'downedMechBoss2', name: '双子魔眼' },
  { flag: 'downedMechBoss3', name: '机械骷髅王' },
  { flag: 'downedPlantBoss', name: '世纪之花' },
  { flag: 'downedGolemBoss', name: '石巨人' },
  { flag: 'downedFishron', name: '猪鲨' },
  { flag: 'downedMoonlord', name: '月总' },
  { flag: 'downedQueenSlime', name: '史莱姆皇后' },
  { flag: 'downedEmpressOfLight', name: '光之女皇' },
  { flag: 'downedDeerclops', name: '鹿角怪' }
]

const conditionTypes = [
  { value: 'always', name: '始终上架' },
  { value: 'hardmode', name: '仅肉山后' },
  { value: 'boss', name: '击杀 Boss' },
  { value: 'kill', name: '图鉴击杀数>0' },
  { value: 'never', name: '永不上架' }
]

// 商店头部渐变色（按索引取色，循环）
const shopAccents = ['#6366f1', '#8b5cf6', '#06b6d4', '#10b981', '#f59e0b', '#ec4899']
const shopAccent = (index) => shopAccents[index % shopAccents.length]

// ═════════ 物品辅助 ═════════
const getItemInfo = (itemId) => itemData.value.list.find(i => i.id === itemId)
const getItemName = (itemId) => {
  const info = getItemInfo(itemId)
  return info ? (info.chinese || info.name || `物品 ${itemId}`) : `物品ID: ${itemId}`
}
const getItemIconUrl = (itemId) => {
  if (imageErrorIds.value.has(itemId)) return null
  return `/assets/img/img/Item_${itemId}.png`
}
const handleItemImageError = (itemId) => imageErrorIds.value.add(itemId)

// ═════════ 价格辅助（铜币 ↔ 金/银/铜） ═════════
const toCopper = (g, s, c) => {
  const gg = parseInt(g) || 0, ss = parseInt(s) || 0, cc = parseInt(c) || 0
  return Math.max(0, gg * 10000 + ss * 100 + cc)
}
const fromCopper = (copper) => {
  const v = Math.max(0, parseInt(copper) || 0)
  return { g: Math.floor(v / 10000), s: Math.floor((v % 10000) / 100), c: v % 100 }
}
const setPrice = (item, part, value) => {
  const cur = fromCopper(item.price)
  cur[part] = parseInt(value) || 0
  item.price = toCopper(cur.g, cur.s, cur.c)
}

// ═════════ 条件辅助 ═════════
const condTypeLabel = (type) => conditionTypes.find(t => t.value === type)?.name || type
const getCondNpcText = (cond) => {
  const ids = (cond.npcIds && cond.npcIds.length > 0) ? cond.npcIds : (cond.npcId ? [cond.npcId] : [])
  return ids.join(',')
}
const setCondNpcText = (cond, text) => {
  const ids = text.split(/[,，\s]+/).map(s => parseInt(s)).filter(n => n > 0)
  cond.npcIds = ids
  cond.npcId = ids.length > 0 ? ids[0] : 0
}

// ═════════ 物品选择器 ═════════
const showItemSearch = ref(false)
const itemSearchTarget = ref(null)
// 上下文：{type:'summon'} | {type:'control-add', slot} | {type:'control-replace', slot}
//        | {type:'item-add', shopIndex} | {type:'item-replace', shopIndex, goodsIndex}
const openSearch = (target) => {
  itemSearchTarget.value = target
  showItemSearch.value = true
}
const handleItemSelect = (item) => {
  const t = itemSearchTarget.value
  if (!t) { showItemSearch.value = false; return }
  if (t.type === 'summon') {
    config.value.summonItemId = item.id
  } else if (t.type === 'control-add') {
    config.value.statueControls.push({ slot: t.slot, statueItemId: item.id, targetShopIndex: 0, name: '' })
    selected.value = { type: 'control', slot: t.slot }
  } else if (t.type === 'control-replace') {
    const c = controlBySlot(t.slot)
    if (c) c.statueItemId = item.id
	  } else if (t.type === 'item-add') {
    config.value.shops[t.shopIndex].items.push({
      itemId: item.id, price: 100, stack: 1,
      condition: { type: 'always', flag: '', npcId: 0, npcIds: [] }
    })
    // 选中刚添加的商品（若已超出商品区则选中最后一个可见商品）
    const gi = Math.min(config.value.shops[t.shopIndex].items.length - 1, goodsSlots.value.length - 1)
    selected.value = { type: 'item', shopIndex: t.shopIndex, goodsIndex: gi }
  } else if (t.type === 'item-replace') {
    config.value.shops[t.shopIndex].items[t.goodsIndex].itemId = item.id
  }
  showItemSearch.value = false
}

// ═════════ 格子点击 ═════════
// 控件面板：空格 → 选物品成为控件；控件格 → 选中编辑
const onControlCellClick = (slot) => {
  if (controlBySlot(slot)) selected.value = { type: 'control', slot }
  else openSearch({ type: 'control-add', slot })
}
// 商店面板：控件格锁定（提示）；商品格 → 有商品选中编辑，空格直接选物品添加
const onGoodsCellClick = (shopIndex, slot) => {
  const gi = goodsSlots.value.indexOf(slot)
  if (gi >= 0 && config.value.shops[shopIndex]?.items[gi]) {
    selected.value = { type: 'item', shopIndex, goodsIndex: gi }
  } else {
    openSearch({ type: 'item-add', shopIndex })
  }
}
// 商店面板点击控件格 → 提示去控件面板编辑
const onLockedControlClick = () => {
  success.value = '控件格为全局锁定（顶部「控件」面板可编辑），此格仅显示控件'
  setTimeout(() => { success.value = '' }, 2000)
}

// ═════════ 编辑面板操作 ═════════
const removeSelectedControl = () => {
  const s = selected.value
  if (s?.type !== 'control') return
  const idx = config.value.statueControls.findIndex(c => c.slot === s.slot)
  if (idx >= 0) config.value.statueControls.splice(idx, 1)
  selected.value = null
}
const removeSelectedItem = () => {
  const s = selected.value
  if (s?.type !== 'item') return
  config.value.shops[s.shopIndex].items.splice(s.goodsIndex, 1)
  selected.value = null
}

// ═════════ 商店操作 ═════════
const addShop = () => {
  config.value.shops.push({ name: `商店 ${config.value.shops.length + 1}`, items: [] })
}
const removeShop = (index) => {
  config.value.shops.splice(index, 1)
  config.value.statueControls.forEach(c => {
    if (c.targetShopIndex >= config.value.shops.length) c.targetShopIndex = Math.max(0, config.value.shops.length - 1)
  })
  selected.value = null
}

// ═════════ 数据加载/保存 ═════════
const fetchData = async () => {
  loading.value = true
  error.value = ''
  try {
    const [res, items] = await Promise.all([
      get('/api/config/shopui'),
      loadItemData()
    ])
    itemData.value = items
    const data = await res.json()
    const cfg = data.config || data
    if (cfg && (cfg.shops || cfg.statueControls)) {
      config.value = normalizeConfig(cfg)
    }
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  loading.value = false
}

// 兼容缺失字段：旧配置 statueControls 无 slot → 自动分配尾部格子（36,37,38,39...）
const normalizeConfig = (cfg) => ({
  enabled: cfg.enabled !== false,
  summonItemId: cfg.summonItemId || 3500,
  statueControls: Array.isArray(cfg.statueControls) ? cfg.statueControls.map((s, i, arr) => ({
    slot: (s.slot !== undefined && s.slot !== null) ? s.slot : (40 - arr.length + i),
    statueItemId: s.statueItemId || 1,
    targetShopIndex: s.targetShopIndex ?? 0,
    name: s.name || ''
  })) : [],
  shops: Array.isArray(cfg.shops) ? cfg.shops.map(shop => ({
    name: shop.name || '',
    items: Array.isArray(shop.items) ? shop.items.map(it => ({
      itemId: it.itemId || 1,
      price: it.price ?? 100,
      stack: it.stack || 1,
      condition: {
        type: it.condition?.type || 'always',
        flag: it.condition?.flag || '',
        npcId: it.condition?.npcId || 0,
        npcIds: it.condition?.npcIds || []
      }
    })) : []
  })) : []
})

const handleSave = async () => {
  saving.value = true
  error.value = ''
  success.value = ''
  try {
    const res = await post('/api/config/shopui', { config: config.value })
    const data = await res.json()
    if (data.status === 200 || data.status === '200' || data.response === '配置已保存') {
      success.value = '已保存，在线玩家商店已即时刷新'
      setTimeout(() => { success.value = '' }, 2500)
    } else {
      error.value = data.error || '保存失败'
    }
  } catch (err) {
    error.value = '保存失败: ' + err.message
  }
  saving.value = false
}

onMounted(() => {
  fetchData()
})
</script>

<template>
  <div class="shopui-page">
    <!-- ═══════ 页头 ═══════ -->
    <div class="page-header">
      <div class="page-header-main">
        <h2>虚拟商店配置</h2>
        <p class="page-desc">40 格商店面板（10×4）可视化配置：控件格 + 商品格，保存后即时生效</p>
      </div>
      <div class="status-badge" :class="config.enabled ? 'status-on' : 'status-off'">
        <span class="status-dot"></span>
        {{ config.enabled ? '已启用' : '已停用' }}
      </div>
    </div>

    <div v-if="loading" class="loading-state">
      <div class="loading-spinner"></div>
      <span>加载中...</span>
    </div>

    <template v-else>
      <!-- ═══════ 基础配置 ═══════ -->
      <div class="section-card">
        <div class="card-header">
          <div class="card-icon icon-indigo">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"></path>
            </svg>
          </div>
          <div class="card-title">
            <h3>基础配置</h3>
            <span class="card-sub">总开关与召唤物</span>
          </div>
          <label class="switch">
            <input type="checkbox" v-model="config.enabled" />
            <span class="slider"></span>
          </label>
        </div>

        <div class="card-body">
          <div class="field-block">
            <label class="form-label">召唤物（手持挥动召唤旅商）</label>
            <div class="summon-row">
              <div class="item-icon-frame item-icon-frame-lg">
                <img
                  v-if="getItemIconUrl(config.summonItemId)"
                  :src="getItemIconUrl(config.summonItemId)" :alt="getItemName(config.summonItemId)"
                  @error="handleItemImageError(config.summonItemId)"
                />
              </div>
              <span class="summon-name">{{ getItemName(config.summonItemId) }}</span>
              <button @click="openSearch({ type: 'summon' })" class="btn-pick">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="11" cy="11" r="8"></circle>
                  <path d="M21 21l-4.35-4.35"></path>
                </svg>
                选择物品
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- ═══════ 控件面板（40 格） ═══════ -->
      <div class="section-card">
        <div class="card-header">
          <div class="card-icon icon-violet">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M5 3v18M19 3v18M5 7h14M5 17h14"></path>
            </svg>
          </div>
          <div class="card-title">
            <h3>控件（商店切换按钮）</h3>
            <span class="card-sub">{{ config.statueControls.length }} 个控件 · 点击格子指定控件物品</span>
          </div>
        </div>
        <p class="card-tip">
          在下方 40 格中点击格子 → 选择物品，该格即成为<strong>控件</strong>（点击跳转到对应商店）。
          控件优先占格：其占用的格子在<strong>所有商店面板中锁定</strong>，剩余格子才是商品区。数量不限。
        </p>

        <div class="goods-grid">
          <div
            v-for="slot in slots40"
            :key="'c' + slot"
            class="cell"
            :class="{
              'cell-control': controlBySlot(slot),
              'cell-empty': !controlBySlot(slot),
              'cell-selected': selected?.type === 'control' && selected.slot === slot
            }"
            @click="onControlCellClick(slot)"
            :title="controlBySlot(slot) ? getItemName(controlBySlot(slot).statueItemId) : '点击设为控件'"
          >
            <template v-if="controlBySlot(slot)">
              <img
                v-if="getItemIconUrl(controlBySlot(slot).statueItemId)"
                :src="getItemIconUrl(controlBySlot(slot).statueItemId)"
                :alt="getItemName(controlBySlot(slot).statueItemId)"
                @error="handleItemImageError(controlBySlot(slot).statueItemId)"
              />
              <span class="cell-tag">控件</span>
            </template>
            <span v-else class="cell-plus">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="5" x2="12" y2="19"></line>
                <line x1="5" y1="12" x2="19" y2="12"></line>
              </svg>
            </span>
          </div>
        </div>

        <!-- 控件编辑面板 -->
        <div v-if="selectedControl" class="editor-panel">
          <div class="editor-head">
            <span class="editor-title">控件 · 格 {{ selectedControl.slot + 1 }}</span>
            <button @click="removeSelectedControl" class="btn-remove">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
              移除控件
            </button>
          </div>
          <div class="editor-row">
            <div class="editor-item item-preview-row">
              <span class="form-label">控件物品</span>
              <div class="id-search-row">
                <div class="item-icon-frame">
                  <img
                    v-if="getItemIconUrl(selectedControl.statueItemId)"
                    :src="getItemIconUrl(selectedControl.statueItemId)"
                    :alt="getItemName(selectedControl.statueItemId)"
                    @error="handleItemImageError(selectedControl.statueItemId)"
                  />
                </div>
                <span class="item-name">{{ getItemName(selectedControl.statueItemId) }}</span>
                <button @click="openSearch({ type: 'control-replace', slot: selectedControl.slot })" class="btn-pick btn-pick-sm">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="11" cy="11" r="8"></circle>
                    <path d="M21 21l-4.35-4.35"></path>
                  </svg>
                  更换
                </button>
              </div>
            </div>
            <div class="editor-item">
              <span class="form-label">跳转到商店</span>
              <select v-model.number="selectedControl.targetShopIndex" class="form-select">
                <option v-for="(shop, si) in config.shops" :key="si" :value="si">
                  {{ shop.name || ('商店 ' + (si + 1)) }}
                </option>
              </select>
            </div>
            <div class="editor-item">
              <span class="form-label">名称（可选）</span>
              <input v-model="selectedControl.name" class="form-input" placeholder="控件名称" />
            </div>
          </div>
        </div>
      </div>

      <!-- ═══════ 商店面板（每商店 40 格） ═══════ -->
      <div class="section-card">
        <div class="card-header">
          <div class="card-icon icon-cyan">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z"></path>
              <polyline points="9 22 9 12 15 12 15 22"></polyline>
            </svg>
          </div>
          <div class="card-title">
            <h3>商店内容</h3>
            <span class="card-sub">{{ config.shops.length }} 个商店 · 控件格锁定，剩余 {{ goodsSlots.length }} 格为商品区</span>
          </div>
        </div>
        <p class="card-tip">
          每个商店同样为 40 格面板：紫色格子为<strong>锁定控件</strong>（顶部控件面板编辑），其余格子为<strong>商品区</strong>（点击选物品，可编辑价格/数量/解锁条件）。商品数量不限，超出商品区自动截断。
        </p>

        <div class="shop-list">
          <div v-for="(shop, shopIndex) in config.shops" :key="shopIndex" class="shop-card">
            <div class="shop-header" :style="{ '--shop-accent': shopAccent(shopIndex) }">
              <div class="shop-header-glow"></div>
              <div class="shop-seq">{{ shopIndex + 1 }}</div>
              <input v-model="shop.name" class="form-input shop-name-input" placeholder="商店名称" />
              <span class="badge" :class="{ 'badge-full': shop.items.length > goodsSlots.length }">
                {{ shop.items.length }}/{{ goodsSlots.length }}{{ shop.items.length > goodsSlots.length ? ' 截断' : '' }}
              </span>
              <button @click="removeShop(shopIndex)" class="btn-remove" title="删除商店">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2"></path>
                </svg>
              </button>
            </div>

            <div class="goods-grid">
              <div
                v-for="slot in slots40"
                :key="'s' + shopIndex + '-' + slot"
                class="cell"
                :class="{
                  'cell-control': controlBySlot(slot),
                  'cell-empty': !controlBySlot(slot) && !(goodsSlots.indexOf(slot) < shop.items.length),
                  'cell-selected': selected?.type === 'item' && selected.shopIndex === shopIndex && selected.goodsIndex === goodsSlots.indexOf(slot)
                }"
                @click="controlBySlot(slot) ? onLockedControlClick() : onGoodsCellClick(shopIndex, slot)"
                :title="cellTitle(shop, slot)"
              >
                <template v-if="controlBySlot(slot)">
                  <img
                    v-if="getItemIconUrl(controlBySlot(slot).statueItemId)"
                    :src="getItemIconUrl(controlBySlot(slot).statueItemId)"
                    :alt="getItemName(controlBySlot(slot).statueItemId)"
                    @error="handleItemImageError(controlBySlot(slot).statueItemId)"
                  />
                  <span class="cell-tag">控件</span>
                </template>
                <template v-else>
                  <img
                    v-if="shop.items[goodsSlots.indexOf(slot)] && getItemIconUrl(shop.items[goodsSlots.indexOf(slot)].itemId)"
                    :src="getItemIconUrl(shop.items[goodsSlots.indexOf(slot)].itemId)"
                    :alt="getItemName(shop.items[goodsSlots.indexOf(slot)].itemId)"
                    @error="handleItemImageError(shop.items[goodsSlots.indexOf(slot)].itemId)"
                  />
                  <span v-else class="cell-plus">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <line x1="12" y1="5" x2="12" y2="19"></line>
                      <line x1="5" y1="12" x2="19" y2="12"></line>
                    </svg>
                  </span>
                </template>
              </div>
            </div>

            <!-- 商品编辑面板 -->
            <div v-if="selectedItem && selected.shopIndex === shopIndex" class="editor-panel">
              <div class="editor-head">
                <span class="editor-title">商品 · 格 {{ goodsSlots[selected.goodsIndex] + 1 }}</span>
                <button @click="removeSelectedItem" class="btn-remove">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="18" y1="6" x2="6" y2="18"></line>
                    <line x1="6" y1="6" x2="18" y2="18"></line>
                  </svg>
                  移除商品
                </button>
              </div>

              <div class="editor-grid">
                <div class="editor-item item-preview-row">
                  <span class="form-label">商品</span>
                  <div class="id-search-row">
                    <div class="item-icon-frame">
                      <img
                        v-if="getItemIconUrl(selectedItem.itemId)"
                        :src="getItemIconUrl(selectedItem.itemId)"
                        :alt="getItemName(selectedItem.itemId)"
                        @error="handleItemImageError(selectedItem.itemId)"
                      />
                    </div>
                    <span class="item-name">{{ getItemName(selectedItem.itemId) }}</span>
                    <button
                      @click="openSearch({ type: 'item-replace', shopIndex: selected.shopIndex, goodsIndex: selected.goodsIndex })"
                      class="btn-pick btn-pick-sm"
                    >
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="11" cy="11" r="8"></circle>
                        <path d="M21 21l-4.35-4.35"></path>
                      </svg>
                      更换
                    </button>
                  </div>
                </div>

                <div class="editor-item">
                  <span class="form-label">价格</span>
                  <div class="price-inputs">
                    <div class="price-item">
                      <input type="number" min="0" :value="fromCopper(selectedItem.price).g"
                        @input="setPrice(selectedItem, 'g', $event.target.value)" class="price-input" placeholder="0" />
                      <span class="price-label">金</span>
                    </div>
                    <div class="price-item">
                      <input type="number" min="0" max="99" :value="fromCopper(selectedItem.price).s"
                        @input="setPrice(selectedItem, 's', $event.target.value)" class="price-input" placeholder="0" />
                      <span class="price-label">银</span>
                    </div>
                    <div class="price-item">
                      <input type="number" min="0" max="99" :value="fromCopper(selectedItem.price).c"
                        @input="setPrice(selectedItem, 'c', $event.target.value)" class="price-input" placeholder="0" />
                      <span class="price-label">铜</span>
                    </div>
                  </div>
                </div>

                <div class="editor-item editor-item-sm">
                  <span class="form-label">数量</span>
                  <input type="number" v-model.number="selectedItem.stack" min="1" class="form-input stack-input" />
                </div>

                <div class="editor-item editor-item-cond">
                  <span class="form-label">解锁条件</span>
                  <div class="cond-row">
                    <select v-model="selectedItem.condition.type" class="form-select cond-select">
                      <option v-for="t in conditionTypes" :key="t.value" :value="t.value">{{ t.name }}</option>
                    </select>
                    <select v-if="selectedItem.condition.type === 'boss'" v-model="selectedItem.condition.flag" class="form-select cond-select">
                      <option v-for="b in bossFlags" :key="b.flag" :value="b.flag">{{ b.name }}</option>
                    </select>
                    <input
                      v-else-if="selectedItem.condition.type === 'kill'"
                      :value="getCondNpcText(selectedItem.condition)"
                      @input="setCondNpcText(selectedItem.condition, $event.target.value)"
                      class="form-input cond-input" placeholder="NPC ID，如 266 / 13,14,15"
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>

          <button class="btn-add btn-add-shop" @click="addShop">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"></line>
              <line x1="5" y1="12" x2="19" y2="12"></line>
            </svg>
            添加商店
          </button>
        </div>
      </div>

      <!-- 保存 -->
      <div class="actions">
        <button @click="handleSave" :disabled="saving" class="save-btn">
          <svg v-if="saving" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="spinner">
            <circle cx="12" cy="12" r="10"></circle>
          </svg>
          <svg v-else width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M19 21H5a2 2 0 01-2-2V5a2 2 0 012-2h11l5 5v11a2 2 0 01-2 2z"></path>
            <polyline points="17 21 17 13 7 13 7 21"></polyline>
            <polyline points="7 3 7 8 15 8"></polyline>
          </svg>
          {{ saving ? '保存中...' : '保存配置' }}
        </button>
        <span class="save-tip">保存后立即生效：在线玩家已打开的商店会按新配置刷新</span>
      </div>
    </template>

    <!-- Toast -->
    <Transition name="toast">
      <div v-if="success" class="toast toast-success">
        <svg class="toast-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18">
          <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/>
        </svg>
        <span>{{ success }}</span>
      </div>
    </Transition>
    <Transition name="toast">
      <div v-if="error" class="toast toast-error">
        <svg class="toast-icon" viewBox="0 0 20 20" fill="currentColor" width="18" height="18">
          <path fill-rule="evenodd" d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z" clip-rule="evenodd"/>
        </svg>
        <span>{{ error }}</span>
      </div>
    </Transition>

    <ItemSearchDialog
      :show="showItemSearch"
      mode="restrict"
      @select="handleItemSelect"
      @close="showItemSearch = false"
    />
  </div>
</template>

<style scoped>
.shopui-page {
  padding: 24px;
  width: 100%;
  max-width: 1080px;
  margin: 0 auto;
  box-sizing: border-box;
}

/* ═══════ 页头 ═══════ */
.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 24px;
}
.page-header h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.6rem;
  font-weight: 700;
  letter-spacing: -0.01em;
}
.page-desc {
  margin: 6px 0 0 0;
  color: var(--text-muted);
  font-size: 0.9rem;
}
.status-badge {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  padding: 6px 14px;
  border-radius: 999px;
  font-size: 0.82rem;
  font-weight: 600;
  flex-shrink: 0;
  transition: all 0.3s;
}
.status-badge .status-dot { width: 8px; height: 8px; border-radius: 50%; }
.status-on { background: rgba(16, 185, 129, 0.12); color: #10b981; }
.status-on .status-dot { background: #10b981; box-shadow: 0 0 0 3px rgba(16, 185, 129, 0.18); }
.status-off { background: rgba(148, 163, 184, 0.15); color: var(--text-muted); }
.status-off .status-dot { background: #94a3b8; }

.loading-state {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 80px;
  color: var(--text-muted);
}
.loading-spinner {
  width: 22px; height: 22px;
  border: 3px solid var(--border-color);
  border-top-color: var(--accent-primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }
.spinner { animation: spin 0.8s linear infinite; }

/* ═══════ 卡片通用 ═══════ */
.section-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 20px;
  box-shadow: var(--shadow-md);
  padding: 24px;
  margin-bottom: 20px;
  transition: border-color 0.3s, box-shadow 0.3s;
}
.section-card:hover {
  border-color: rgba(99, 102, 241, 0.25);
  box-shadow: 0 8px 30px rgba(99, 102, 241, 0.08);
}
.card-header {
  display: flex;
  align-items: center;
  gap: 14px;
  margin-bottom: 14px;
}
.card-icon {
  width: 42px; height: 42px;
  border-radius: 12px;
  display: flex; align-items: center; justify-content: center;
  flex-shrink: 0;
}
.icon-indigo { background: rgba(99, 102, 241, 0.12); color: #6366f1; }
.icon-violet { background: rgba(139, 92, 246, 0.12); color: #8b5cf6; }
.icon-cyan { background: rgba(6, 182, 212, 0.12); color: #06b6d4; }
.card-title { flex: 1; min-width: 0; }
.card-title h3 { margin: 0; color: var(--text-primary); font-size: 1.08rem; font-weight: 600; }
.card-sub { font-size: 0.78rem; color: var(--text-muted); display: block; margin-top: 2px; }
.card-body { display: flex; flex-direction: column; gap: 14px; }
.card-tip {
  margin: -2px 0 18px 0;
  padding: 10px 14px;
  border-radius: 10px;
  background: rgba(99, 102, 241, 0.05);
  border: 1px dashed rgba(99, 102, 241, 0.25);
  color: var(--text-secondary);
  font-size: 0.83rem;
}
.card-tip strong { color: var(--accent-primary); }

/* ═══════ 表单 ═══════ */
.form-label {
  display: block;
  margin-bottom: 6px;
  color: var(--text-secondary);
  font-size: 0.82rem;
  font-weight: 500;
}
.form-select, .form-input {
  width: 100%;
  padding: 9px 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 10px;
  color: var(--text-primary);
  font-size: 0.88rem;
  outline: none;
  transition: border-color 0.2s, box-shadow 0.2s;
  box-sizing: border-box;
}
.form-select:focus, .form-input:focus {
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.12);
}
.form-select option { background: var(--bg-card); color: var(--text-primary); }

.item-icon-frame {
  width: 34px; height: 34px;
  border-radius: 9px;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.12), rgba(139, 92, 246, 0.06));
  border: 1px solid rgba(99, 102, 241, 0.15);
  display: flex; align-items: center; justify-content: center;
  flex-shrink: 0;
  overflow: hidden;
}
.item-icon-frame-lg { width: 42px; height: 42px; border-radius: 11px; }
.item-icon-frame img { width: 30px; height: 30px; object-fit: contain; image-rendering: pixelated; }
.item-icon-frame-lg img { width: 36px; height: 36px; }
.item-name {
  font-size: 0.8rem;
  color: var(--text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 160px;
}
.id-search-row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.field-block { max-width: 480px; }

.summon-row {
  display: flex;
  align-items: center;
  gap: 12px;
}
.summon-name { font-size: 0.9rem; color: var(--text-primary); font-weight: 500; flex: 1; }

.btn-pick {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  border: none;
  border-radius: 9px;
  background: rgba(99, 102, 241, 0.12);
  color: var(--accent-primary);
  font-size: 0.84rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}
.btn-pick:hover { background: var(--accent-primary); color: white; }
.btn-pick-sm { padding: 6px 10px; font-size: 0.8rem; }

/* ═══════ Switch ═══════ */
.switch { position: relative; display: inline-block; width: 50px; height: 28px; flex-shrink: 0; }
.switch input { opacity: 0; width: 0; height: 0; }
.slider {
  position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 28px;
  transition: all 0.3s ease;
}
.slider::before {
  content: '';
  position: absolute;
  height: 18px; width: 18px;
  left: 3px; bottom: 3px;
  background: var(--text-muted);
  border-radius: 50%;
  transition: all 0.3s ease;
}
.switch input:checked + .slider {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
}
.switch input:checked + .slider::before {
  transform: translateX(22px);
  background: white;
}

/* ═══════ 40 格网格 ═══════ */
.goods-grid {
  display: grid;
  grid-template-columns: repeat(10, 1fr);
  gap: 8px;
}
.cell {
  aspect-ratio: 1;
  min-height: 40px;
  border-radius: 10px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  position: relative;
  transition: all 0.2s;
  overflow: hidden;
}
.cell:hover {
  border-color: var(--accent-primary);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.12);
}
.cell-empty { border-style: dashed; }
.cell img { width: 30px; height: 30px; object-fit: contain; image-rendering: pixelated; }
.cell-control {
  background: rgba(139, 92, 246, 0.1);
  border-color: rgba(139, 92, 246, 0.45);
}
.cell-control:hover {
  border-color: #8b5cf6;
  box-shadow: 0 4px 12px rgba(139, 92, 246, 0.2);
}
.cell-selected {
  border-color: var(--accent-primary) !important;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.18) !important;
}
.cell-plus {
  color: var(--text-muted);
  opacity: 0.6;
  display: flex;
}
.cell-tag {
  position: absolute;
  bottom: 1px;
  left: 50%;
  transform: translateX(-50%);
  font-size: 7px;
  line-height: 1.1;
  color: #8b5cf6;
  background: rgba(139, 92, 246, 0.12);
  padding: 0 4px;
  border-radius: 4px;
  white-space: nowrap;
}

/* ═══════ 编辑面板 ═══════ */
.editor-panel {
  margin-top: 18px;
  padding: 16px 18px;
  background: var(--bg-tertiary);
  border: 1px solid rgba(99, 102, 241, 0.25);
  border-radius: 14px;
}
.editor-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}
.editor-title {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--accent-primary);
}
.editor-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 18px;
}
.editor-row {
  display: flex;
  flex-wrap: wrap;
  gap: 18px;
  align-items: flex-end;
}
.editor-item { min-width: 160px; }
.editor-item-sm { min-width: 80px; }
.editor-item-cond { min-width: 240px; flex: 1; }
.item-preview-row { min-width: 220px; }
.cond-row { display: flex; gap: 8px; flex-wrap: wrap; }
.cond-row .cond-select { min-width: 130px; }
.cond-input { min-width: 160px; }

.price-inputs { display: flex; gap: 5px; }
.price-item { display: flex; align-items: center; gap: 4px; }
.price-input {
  width: 46px;
  padding: 7px 4px;
  text-align: center;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 8px;
  color: var(--text-primary);
  font-size: 0.85rem;
  outline: none;
  transition: border-color 0.2s, box-shadow 0.2s;
  -moz-appearance: textfield;
}
.price-input::-webkit-outer-spin-button,
.price-input::-webkit-inner-spin-button { -webkit-appearance: none; margin: 0; }
.price-input:focus { border-color: var(--accent-primary); box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.12); }
.price-label { font-size: 0.72rem; color: var(--text-muted); }
.stack-input { width: 64px; }

/* ═══════ 商店卡片 ═══════ */
.shop-list { display: flex; flex-direction: column; gap: 18px; }
.shop-card {
  background: var(--bg-primary);
  border: 1px solid var(--border-light);
  border-radius: 18px;
  overflow: hidden;
  transition: border-color 0.3s, box-shadow 0.3s;
}
.shop-card:hover {
  border-color: rgba(99, 102, 241, 0.3);
  box-shadow: 0 10px 34px rgba(99, 102, 241, 0.1);
}
.shop-header {
  position: relative;
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 18px;
  background: linear-gradient(135deg, color-mix(in srgb, var(--shop-accent) 10%, transparent), transparent 70%);
  border-bottom: 1px solid var(--border-light);
}
.shop-header-glow {
  position: absolute;
  left: 0; top: 0; bottom: 0;
  width: 4px;
  background: var(--shop-accent);
  border-radius: 0 4px 4px 0;
}
.shop-seq {
  width: 30px; height: 30px;
  flex-shrink: 0;
  border-radius: 9px;
  background: var(--shop-accent);
  color: white;
  font-weight: 700;
  font-size: 0.9rem;
  display: flex; align-items: center; justify-content: center;
}
.shop-name-input { max-width: 240px; }
.badge {
  font-size: 0.75rem;
  padding: 3px 10px;
  border-radius: 999px;
  background: rgba(99, 102, 241, 0.12);
  color: var(--accent-primary);
  font-weight: 600;
  flex-shrink: 0;
}
.badge-full { background: rgba(245, 158, 11, 0.15); color: #f59e0b; }

/* ═══════ 按钮 ═══════ */
.btn-remove {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 7px 12px;
  flex-shrink: 0;
  border: none;
  border-radius: 9px;
  background: rgba(239, 68, 68, 0.1);
  color: #ef4444;
  font-size: 0.8rem;
  cursor: pointer;
  transition: all 0.2s;
}
.btn-remove:hover { background: #ef4444; color: white; }

.btn-add {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 10px 18px;
  border: 2px dashed var(--border-color);
  border-radius: 11px;
  background: transparent;
  color: var(--text-muted);
  font-size: 0.86rem;
  cursor: pointer;
  transition: all 0.2s;
}
.btn-add:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.04);
}
.btn-add-shop { margin-top: 6px; padding: 13px; width: 100%; }

/* ═══════ 保存 ═══════ */
.actions {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-top: 8px;
}
.save-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 13px 34px;
  border: none;
  border-radius: 12px;
  background: linear-gradient(135deg, #6366f1, #8b5cf6);
  color: white;
  font-size: 0.95rem;
  font-weight: 600;
  cursor: pointer;
  box-shadow: 0 6px 20px rgba(99, 102, 241, 0.3);
  transition: all 0.25s;
}
.save-btn:hover { transform: translateY(-1px); box-shadow: 0 8px 26px rgba(99, 102, 241, 0.4); }
.save-btn:disabled { opacity: 0.55; cursor: not-allowed; transform: none; }
.save-tip { font-size: 0.78rem; color: var(--text-muted); }

/* ═══════ Toast ═══════ */
.toast {
  position: fixed;
  top: 20px;
  right: 24px;
  z-index: 9999;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 18px;
  border-radius: 12px;
  font-size: 0.88rem;
  font-weight: 500;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.18);
  pointer-events: none;
  max-width: 380px;
}
.toast-success { color: #065f46; background: #d1fae5; border: 1px solid #6ee7b7; }
.toast-error { color: #991b1b; background: #fee2e2; border: 1px solid #fca5a5; }
.toast-icon { flex-shrink: 0; }
.toast-enter-active { transition: all 0.3s ease-out; }
.toast-leave-active { transition: all 0.25s ease-in; }
.toast-enter-from { opacity: 0; transform: translateX(40px); }
.toast-leave-to { opacity: 0; transform: translateX(40px); }
</style>
