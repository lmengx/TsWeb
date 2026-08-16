<script setup>
import { ref, computed, onMounted } from 'vue'
import { get, post } from '../../../utils/api.js'
import ItemSearchDialog from '../../../components/ItemSearchDialog.vue'
import { loadItemData } from '../../../api/itemDataApi.js'

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
  statueControls: [], // [{ slot, statueItemId, targetShopIndex, name }]（控件由配置保留，前端仅锁定显示）
  shops: []          // [{ name, items: [{ itemId, price, stack, condition }] }]
})

const slots40 = Array.from({ length: 40 }, (_, i) => i)

const controlBySlot = (slot) => config.value.statueControls.find(c => c.slot === slot)
const controlSlots = computed(() => new Set(config.value.statueControls.map(c => c.slot)))
// 商品区 = 40 格中非控件格（有序），控件优先级高于商品
const goodsSlots = computed(() => slots40.filter(s => !controlSlots.value.has(s)))

// 商品格 → 该格物品（按显式 slot 查找，支持中间留空格；无则 null）
const goodsItem = (shop, slot) => {
  if (!shop || !Array.isArray(shop.items)) return null
  return shop.items.find(it => it.slot === slot) || null
}

// ═════════ 编辑面板（右滑动画）与选中集合 ═════════
// 选中集合使用物品对象引用（拖动/跨商店移动后依然指向同一物品）
const panelOpen = ref(false)
const selectedItems = ref(new Set())

const isSelected = (item) => selectedItems.value.has(item)
const panelItem = computed(() => {
  if (selectedItems.value.size !== 1) return null
  return [...selectedItems.value][0]
})
// 单选时定位物品所在商店/下标（实时查找，兼容拖动后的位置变化）
const panelLoc = computed(() => {
  const item = panelItem.value
  if (!item) return null
  for (let si = 0; si < config.value.shops.length; si++) {
    const gi = config.value.shops[si].items.indexOf(item)
    if (gi >= 0) return { shopIndex: si, goodsIndex: gi }
  }
  return null
})
// 多选列表（按商店顺序排列）
const selectedList = computed(() => {
  const list = []
  for (let si = 0; si < config.value.shops.length; si++) {
    config.value.shops[si].items.forEach((item, gi) => {
      if (selectedItems.value.has(item)) list.push({ shopIndex: si, goodsIndex: gi, item })
    })
  }
  return list
})
const panelTitle = computed(() => {
  if (!panelOpen.value) return ''
  if (selectedItems.value.size === 1) return getItemName(panelItem.value.itemId)
  return `批量赋值 · ${selectedItems.value.size} 个物品`
})

const selectSingle = (item) => {
  selectedItems.value = new Set([item])
  panelOpen.value = true
}
const toggleSelect = (item) => {
  const next = new Set(selectedItems.value)
  if (next.has(item)) next.delete(item)
  else next.add(item)
  selectedItems.value = next
  // 若选择被清空则收起面板；否则保持打开（自动切换单/批量模式）
  if (next.size === 0) panelOpen.value = false
  else panelOpen.value = true
}
const removeFromSelection = (item) => toggleSelect(item)
const clearSelection = () => {
  selectedItems.value = new Set()
  panelOpen.value = false
}
const panelClose = () => {
  panelOpen.value = false
  selectedItems.value = new Set()
}
// 配置完参数后返回控件面板：收起编辑面板（控件面板自动展开），选中保留 → 商品格高亮仍在，点击可继续编辑
const returnToControls = () => {
  panelOpen.value = false
}

// 移除单选面板中的物品
const removePanelItem = () => {
  const loc = panelLoc.value
  if (!loc) return
  const item = panelItem.value
  config.value.shops[loc.shopIndex].items.splice(loc.goodsIndex, 1)
  selectedItems.value = new Set()
  panelOpen.value = false
}

// ═════════ 批量赋值 ═════════
const batchForm = ref({ g: '', s: '', c: '', stack: '', condType: '', flag: '', npcIds: '' })
const applyBatch = () => {
  const b = batchForm.value
  const n = selectedItems.value.size
  if (n === 0) return
  for (const item of selectedItems.value) {
    // 价格：任一枚额非空则覆盖对应位（留空保留原值）
    if (b.g !== '' || b.s !== '' || b.c !== '') {
      const cur = fromCopper(item.price)
      const g = b.g !== '' ? (parseInt(b.g) || 0) : cur.g
      const s = b.s !== '' ? (parseInt(b.s) || 0) : cur.s
      const c = b.c !== '' ? (parseInt(b.c) || 0) : cur.c
      item.price = toCopper(g, s, c)
    }
    if (b.stack !== '') item.stack = Math.max(1, parseInt(b.stack) || 1)
    if (b.condType !== '') {
      item.condition.type = b.condType
      if (b.condType === 'boss' && b.flag !== '') item.condition.flag = b.flag
      if (b.condType === 'kill' && b.npcIds !== '') setCondNpcText(item.condition, b.npcIds)
    }
  }
  batchForm.value = { g: '', s: '', c: '', stack: '', condType: '', flag: '', npcIds: '' }
  showToast(`已应用到 ${n} 个物品`)
}

// ═════════ 拖拽放置（商品 + 控件，拖到哪格就停在哪格，支持空格） ═════════
// dragState: { type: 'goods', shopIndex, slot } 商品拖动 | { type: 'control', slot } 控件拖动
const dragState = ref(null)
const dragOverSlot = ref(-1)
let suppressClickSlot = -1 // 拖放落点防误触（drop 后浏览器会补发 click）

const onDragStart = (shopIndex, slot, ev) => {
  dragState.value = { type: 'goods', shopIndex, slot }
  ev.dataTransfer.effectAllowed = 'move'
  if (ev.dataTransfer.setData) ev.dataTransfer.setData('text/plain', '')
}
const onControlDragStart = (slot, ev) => {
  dragState.value = { type: 'control', slot }
  ev.dataTransfer.effectAllowed = 'move'
  if (ev.dataTransfer.setData) ev.dataTransfer.setData('text/plain', '')
}
const onDragEnd = () => {
  dragState.value = null
  dragOverSlot.value = -1
  setTimeout(() => { suppressClickSlot = -1 }, 80) // 拖放结束后短暂抑制落点 click
}

// 商店面板格子 dragover：商品→仅非控件格；控件→空格或控件格（不能落在商品格）
const onCellDragOver = (ev, shopIndex, slot) => {
  const st = dragState.value
  if (!st) return
  const isControlCell = !!controlBySlot(slot)
  if (st.type === 'goods' && isControlCell) return
  if (st.type === 'control' && !isControlCell && goodsItem(config.value.shops[shopIndex], slot)) return
  ev.preventDefault()
  if (ev.dataTransfer) ev.dataTransfer.dropEffect = 'move'
  dragOverSlot.value = slot
}
// 控件面板格子 dragover：仅控件拖动可落
const onControlCellDragOver = (ev, slot) => {
  const st = dragState.value
  if (!st || st.type !== 'control') return
  ev.preventDefault()
  if (ev.dataTransfer) ev.dataTransfer.dropEffect = 'move'
  dragOverSlot.value = slot
}

// 控件落格：目标有控件→交换 slot；空格→移动；商店面板商品格在 drop 前置检查
const dropControl = (fromSlot, targetSlot) => {
  if (fromSlot === targetSlot) return
  const src = controlBySlot(fromSlot)
  if (!src) return
  const dst = controlBySlot(targetSlot)
  if (dst && dst !== src) dst.slot = fromSlot
  src.slot = targetSlot
  suppressClickSlot = targetSlot
}

// 商店面板格子 drop：按拖拽类型分发
const onCellDrop = (ev, targetShopIndex, targetSlot) => {
  ev.preventDefault()
  const st = dragState.value
  dragState.value = null
  dragOverSlot.value = -1
  if (!st) return
  if (st.type === 'control') {
    // 控件落在商品格 → 拒绝（dragover 已阻止，双保险）
    if (goodsItem(config.value.shops[targetShopIndex], targetSlot)) return
    dropControl(st.slot, targetSlot)
    return
  }
  moveGoods(st, targetShopIndex, targetSlot)
}
// 控件面板格子 drop：仅控件
const onControlCellDrop = (ev, targetSlot) => {
  ev.preventDefault()
  const st = dragState.value
  dragState.value = null
  dragOverSlot.value = -1
  if (!st || st.type !== 'control') return
  dropControl(st.slot, targetSlot)
}

// 商品移动/交换（同商店：空→移动 / 有→交换；跨商店：空→搬移 / 有→双方对调）
const moveGoods = (from, targetShopIndex, targetSlot) => {
  if (from.shopIndex === targetShopIndex && from.slot === targetSlot) return
  const srcShop = config.value.shops[from.shopIndex]
  const dstShop = config.value.shops[targetShopIndex]
  if (!srcShop || !dstShop) return
  const srcIdx = srcShop.items.findIndex(it => it.slot === from.slot)
  if (srcIdx < 0) return
  const srcItem = srcShop.items[srcIdx]
  const dstItem = dstShop.items.find(it => it.slot === targetSlot)

  if (from.shopIndex === targetShopIndex) {
    // 同商店：目标格空 → 移动；目标格有物品 → 交换格子
    if (dstItem && dstItem !== srcItem) {
      srcItem.slot = targetSlot
      dstItem.slot = from.slot
    } else {
      srcItem.slot = targetSlot
    }
  } else {
    // 跨商店：目标格空 → 直接搬过去；目标格有物品 → 双方对调商店与格子
    srcShop.items.splice(srcIdx, 1)
    if (dstItem) {
      const dstIdx = dstShop.items.indexOf(dstItem)
      if (dstIdx >= 0) dstShop.items.splice(dstIdx, 1)
      dstItem.slot = from.slot
      srcShop.items.push(dstItem)
    }
    srcItem.slot = targetSlot
    dstShop.items.push(srcItem)
  }
  suppressClickSlot = targetSlot
}

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
const getCondNpcText = (cond) => {
  const ids = (cond.npcIds && cond.npcIds.length > 0) ? cond.npcIds : (cond.npcId ? [cond.npcId] : [])
  return ids.join(',')
}
const setCondNpcText = (cond, text) => {
  const ids = text.split(/[,，\s]+/).map(s => parseInt(s)).filter(n => n > 0)
  cond.npcIds = ids
  cond.npcId = ids.length > 0 ? ids[0] : 0
}

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

// 商店序号渐变色（循环）
const shopAccents = ['#6366f1', '#8b5cf6', '#06b6d4', '#10b981', '#f59e0b', '#ec4899']
const shopAccent = (index) => shopAccents[index % shopAccents.length]

// ═════════ 物品选择器（单选） ═════════
const showItemSearch = ref(false)
const itemSearchTarget = ref(null)
// {type:'summon'} | {type:'control-add', slot} | {type:'control-replace', slot}
// | {type:'item-add', shopIndex} | {type:'item-replace', shopIndex, goodsIndex}
const openSearch = (target) => {
  itemSearchTarget.value = target
  showItemSearch.value = true
}
const handleItemSelect = (item) => {
  const t = itemSearchTarget.value
  if (!t) { showItemSearch.value = false; return }
  // 多选对话框的结果不经过这里（multi 标记兜底）
  if (item && item.multi) { showItemSearch.value = false; return }
  if (t.type === 'summon') {
    config.value.summonItemId = item.id
  } else if (t.type === 'control-add') {
    config.value.statueControls.push({ slot: t.slot, statueItemId: item.id, targetShopIndex: 0, name: '' })
  } else if (t.type === 'control-replace') {
    const c = controlBySlot(t.slot)
    if (c) c.statueItemId = item.id
  } else if (t.type === 'item-add') {
    // 添加到点击的空槽上（slot 由格子位置决定）
    const obj = newItem(item.id, t.slot)
    config.value.shops[t.shopIndex].items.push(obj)
    selectSingle(obj)
  } else if (t.type === 'item-replace') {
    config.value.shops[t.shopIndex].items[t.goodsIndex].itemId = item.id
  }
  showItemSearch.value = false
}
const newItem = (itemId, slot = -1) => ({
  slot: slot >= 0 && slot < 40 ? slot : -1,
  itemId, price: 100, stack: 1,
  condition: { type: 'always', flag: '', npcId: 0, npcIds: [] }
})

// ═════════ 批量添加（多选对话框） ═════════
const showBatchDialog = ref(false)
const batchShopIndex = ref(-1)
const openBatchAdd = (shopIndex) => {
  batchShopIndex.value = shopIndex
  showBatchDialog.value = true
}
// 该商店第一个可用非控件格（未被占用）
const firstFreeSlot = (shopIndex) => {
  const shop = config.value.shops[shopIndex]
  if (!shop) return -1
  for (let s = 0; s < 40; s++) {
    if (controlSlots.value.has(s)) continue
    if (!shop.items.some(it => it.slot === s)) return s
  }
  return -1
}
const handleBatchSelect = (res) => {
  const items = res?.items || []
  showBatchDialog.value = false
  if (items.length === 0) return
  const shop = config.value.shops[batchShopIndex.value]
  if (!shop) return
  const added = []
  for (const it of items) {
    // 每个新物品自动放到第一个可用空格（不强制紧凑）
    const s = firstFreeSlot(batchShopIndex.value)
    if (s < 0) {
      showToast('商店空格已满，未全部添加')
      break
    }
    const obj = newItem(it.id, s)
    shop.items.push(obj)
    added.push(obj)
  }
  // 新添加的物品进入选中集合 → 面板自动切到批量赋值模式，方便立刻统一设参数
  selectedItems.value = new Set(added)
  panelOpen.value = true
}

// ═════════ 控件编辑（左侧控件面板；选中商品编辑时面板滑出隐藏） ═════════
const controlModal = ref(null) // null | slot 下标
const modalControl = computed(() =>
  controlModal.value !== null ? controlBySlot(controlModal.value) : null)

const onControlCellClick = (slot) => {
  if (suppressClickSlot === slot) return // 刚拖放到该格 → 抑制误触
  if (controlBySlot(slot)) controlModal.value = slot
  else openSearch({ type: 'control-add', slot })
}
const removeControl = () => {
  const slot = controlModal.value
  if (slot === null) return
  const idx = config.value.statueControls.findIndex(c => c.slot === slot)
  if (idx >= 0) config.value.statueControls.splice(idx, 1)
  controlModal.value = null
}
// 商店网格里的控件格：直接打开控件编辑（与左侧控件面板行为一致）
const onLockedControlClick = (slot) => {
  if (suppressClickSlot === slot) return // 刚拖放到该格 → 抑制误触
  controlModal.value = slot
}

// ═════════ 格子交互 ═════════
const onGoodsCellClick = (shopIndex, slot) => {
  if (suppressClickSlot === slot) return // 刚拖放到该格 → 抑制误触
  const item = goodsItem(config.value.shops[shopIndex], slot)
  if (item) {
    selectSingle(item)
  } else {
    // 空格点击 → 添加物品到该格
    openSearch({ type: 'item-add', shopIndex, slot })
  }
}
const cellTitle = (shop, slot) => {
  const c = controlBySlot(slot)
  if (c) return '控件：' + getItemName(c.statueItemId) + '（点击编辑，拖动可换位）'
  const item = goodsItem(shop, slot)
  return item ? getItemName(item.itemId) + '（点击编辑，拖动可排序）' : '点击选择物品添加商品（控件可拖到此格）'
}

// ═════════ 商店操作 ═════════
const addShop = () => {
  config.value.shops.push({ name: `商店 ${config.value.shops.length + 1}`, items: [] })
}
const removeShop = (index) => {
  // 清理选中集合中属于该商店的物品
  const shopItems = config.value.shops[index].items
  const next = new Set([...selectedItems.value].filter(it => !shopItems.includes(it)))
  selectedItems.value = next
  if (next.size === 0) panelOpen.value = false
  config.value.shops.splice(index, 1)
  config.value.statueControls.forEach(c => {
    if (c.targetShopIndex >= config.value.shops.length) c.targetShopIndex = Math.max(0, config.value.shops.length - 1)
  })
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
      config.value = normalizeItemsSlots(normalizeConfig(cfg))
    }
  } catch (err) {
    error.value = '加载配置失败: ' + err.message
  }
  loading.value = false
}

// 兼容缺失字段：旧配置 statueControls 无 slot → 自动分配尾部格子
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
      slot: (it.slot !== undefined && it.slot !== null && it.slot >= 0 && it.slot < 40) ? it.slot : -1,
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

// 商品格位置归一化：旧配置无 slot / 非法 / 控件格 / 重复 → 顺序分配第一个可用非控件格（与后端 NormalizeSlots 一致）
const firstFreeSlotIn = (controlSet, used) => {
  for (let s = 0; s < 40; s++)
    if (!controlSet.has(s) && !used.has(s)) return s
  return -1
}
const normalizeItemsSlots = (cfg) => {
  const controlSet = new Set(cfg.statueControls.map(c => c.slot))
  for (const shop of cfg.shops) {
    const used = new Set()
    for (const it of shop.items) {
      if (it.slot < 0 || it.slot >= 40 || controlSet.has(it.slot) || used.has(it.slot)) {
        it.slot = firstFreeSlotIn(controlSet, used)
        if (it.slot < 0) continue
      }
      used.add(it.slot)
    }
  }
  return cfg
}

let toastTimer = null
const showToast = (msg) => {
  success.value = msg
  clearTimeout(toastTimer)
  toastTimer = setTimeout(() => { success.value = '' }, 2000)
}

const handleSave = async () => {
  saving.value = true
  error.value = ''
  success.value = ''
  try {
    const res = await post('/api/config/shopui', { config: config.value })
    const data = await res.json()
    if (data.status === 200 || data.status === '200' || data.response === '配置已保存') {
      showToast('已保存，在线玩家商店已即时刷新')
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
  <div class="settings-page">
    <div v-if="loading" class="loading-state"><p>加载中...</p></div>

    <template v-else>
      <!-- ═══════ 顶部总览（紧凑） ═══════ -->
      <div class="section-card overview-card">
        <div class="overview-row">
          <div class="overview-info">
            <h3>虚拟商店</h3>
            <p class="section-desc">40 格面板（10×4）可视化配置：点击物品在右侧编辑，商品/控件均可拖动换位，勾选可批量赋值；编辑完点「← 返回」回到商店配置；保存后即时生效</p>
          </div>
          <div class="overview-actions">
            <span class="toggle-label">启用</span>
            <label class="switch">
              <input type="checkbox" v-model="config.enabled" />
              <span class="slider"></span>
            </label>
            <button @click="handleSave" :disabled="saving" class="save-btn">
              {{ saving ? '保存中...' : '保存配置' }}
            </button>
          </div>
        </div>
        <div class="overview-sub">
          <div class="summon-row">
            <span class="toggle-label">召唤物</span>
            <div class="item-icon-frame">
              <img
                v-if="getItemIconUrl(config.summonItemId)"
                :src="getItemIconUrl(config.summonItemId)" :alt="getItemName(config.summonItemId)"
                @error="handleItemImageError(config.summonItemId)"
              />
            </div>
            <span class="summon-name">{{ getItemName(config.summonItemId) }}</span>
            <button @click="openSearch({ type: 'summon' })" class="ghost-btn accent">
              选择物品
            </button>
          </div>
        </div>
      </div>

      <div class="shopui-layout" :class="{ editing: panelOpen }">
      <!-- ═══════ 控件面板（选中商品编辑时向左滑出隐藏） ═══════ -->
      <div class="controls-panel">
      <div class="section-card">
        <div class="card-head">
          <h3>控件（商店切换按钮）<span class="count">{{ config.statueControls.length }}</span></h3>
        </div>
        <p class="section-desc">
          点击格子 → 选择物品即成为控件；<strong>拖动</strong>控件可换位/交换（控件格在所有商店中锁定，剩余 {{ goodsSlots.length }} 格才是商品区）。选中商品编辑时本面板自动向左滑出，点「← 返回」可回到商店配置。
        </p>
        <div class="goods-grid">
          <div
            v-for="slot in slots40"
            :key="'c' + slot"
            class="cell"
            :class="{
              'cell-control': controlBySlot(slot),
              'cell-empty': !controlBySlot(slot),
              'cell-drag-over': dragOverSlot === slot
            }"
            :draggable="!!controlBySlot(slot)"
            @click="onControlCellClick(slot)"
            @dragstart="controlBySlot(slot) && onControlDragStart(slot, $event)"
            @dragover="onControlCellDragOver($event, slot)"
            @drop="onControlCellDrop($event, slot)"
            @dragend="onDragEnd"
            :title="controlBySlot(slot) ? getItemName(controlBySlot(slot).statueItemId) + '（点击编辑，拖动可换位）' : '点击设为控件（控件可从左侧或商店面板拖动换位）'"
          >
            <template v-if="controlBySlot(slot)">
              <img
                v-if="getItemIconUrl(controlBySlot(slot).statueItemId)"
                :src="getItemIconUrl(controlBySlot(slot).statueItemId)"
                :alt="getItemName(controlBySlot(slot).statueItemId)"
                draggable="false"
                @error="handleItemImageError(controlBySlot(slot).statueItemId)"
              />
              <span class="cell-tag">控件</span>
            </template>
            <span v-else class="cell-plus">+</span>
          </div>
        </div>
      </div>
      </div>

      <!-- ═══════ 左栏：商店内容 ═══════ -->
      <div class="layout-left">
      <div class="section-card">
        <div class="card-head">
          <div class="card-head-left">
            <button v-if="panelOpen" class="panel-back-btn" @click="returnToControls" title="返回商店配置（收起物品配置，选中保留）">← 返回</button>
            <h3>商店内容<span class="count">{{ config.shops.length }}</span></h3>
          </div>
          <button class="ghost-btn accent" @click="addShop">+ 添加商店</button>
        </div>
        <p class="section-desc">
          紫色格子为控件（全局锁定，可<strong>拖动换位</strong>）；其余 {{ goodsSlots.length }} 格为商品区，<strong>可随意留空格</strong>、不强制排序。
          <strong>点击</strong>空格添加 / 物品编辑 · <strong>拖动</strong>物品或控件放到任意格（撞到同类型则交换）· <strong>勾选</strong>后批量赋值。
        </p>

        <div v-for="(shop, shopIndex) in config.shops" :key="shopIndex" class="shop-card">
          <div class="shop-head" :style="{ '--shop-accent': shopAccent(shopIndex) }">
            <span class="shop-seq">{{ shopIndex + 1 }}</span>
            <input v-model="shop.name" class="form-input shop-name-input" placeholder="商店名称" />
            <span class="badge" :class="{ 'badge-full': shop.items.length > goodsSlots.length }">
              {{ shop.items.length }}/{{ goodsSlots.length }}{{ shop.items.length > goodsSlots.length ? ' 溢出' : '' }}
            </span>
            <button @click="openBatchAdd(shopIndex)" class="mini-btn accent" title="批量添加物品">＋</button>
            <button @click="removeShop(shopIndex)" class="mini-btn danger" title="删除商店">×</button>
          </div>

          <div class="goods-grid">
            <div
              v-for="slot in slots40"
              :key="'s' + shopIndex + '-' + slot"
              class="cell"
              :class="{
                'cell-control': controlBySlot(slot),
                'cell-empty': !controlBySlot(slot) && !goodsItem(shop, slot),
                'cell-selected': goodsItem(shop, slot) && isSelected(goodsItem(shop, slot)),
                'cell-drag-over': dragOverSlot === slot
              }"
              :draggable="!!controlBySlot(slot) || !!goodsItem(shop, slot)"
              @click="controlBySlot(slot) ? onLockedControlClick(slot) : onGoodsCellClick(shopIndex, slot)"
              @dragstart="controlBySlot(slot) ? onControlDragStart(slot, $event) : (goodsItem(shop, slot) && onDragStart(shopIndex, slot, $event))"
              @dragover="onCellDragOver($event, shopIndex, slot)"
              @drop="onCellDrop($event, shopIndex, slot)"
              @dragend="onDragEnd"
              :title="cellTitle(shop, slot)"
            >
              <template v-if="controlBySlot(slot)">
                <img
                  v-if="getItemIconUrl(controlBySlot(slot).statueItemId)"
                  :src="getItemIconUrl(controlBySlot(slot).statueItemId)"
                  :alt="getItemName(controlBySlot(slot).statueItemId)"
                  draggable="false"
                  @error="handleItemImageError(controlBySlot(slot).statueItemId)"
                />
                <span class="cell-tag">控件</span>
              </template>
              <template v-else>
                <img
                  v-if="goodsItem(shop, slot) && getItemIconUrl(goodsItem(shop, slot).itemId)"
                  :src="getItemIconUrl(goodsItem(shop, slot).itemId)"
                  :alt="getItemName(goodsItem(shop, slot).itemId)"
                  draggable="false"
                  @error="handleItemImageError(goodsItem(shop, slot).itemId)"
                />
                <span v-else class="cell-plus">+</span>
                <label
                  v-if="goodsItem(shop, slot)"
                  class="cell-check"
                  title="勾选加入批量赋值"
                  @click.stop="toggleSelect(goodsItem(shop, slot))"
                >
                  <input type="checkbox" :checked="isSelected(goodsItem(shop, slot))" />
                  <span class="checkmark"></span>
                </label>
              </template>
            </div>
          </div>
        </div>
      </div>
      </div>

      <!-- ═══════ 右栏：物品编辑面板（左移滑入动画） ═══════ -->
      <Transition name="panel-slide">
        <div v-if="panelOpen" class="edit-panel">
          <div class="panel-head">
            <div class="panel-head-left">
              <span class="panel-title">{{ panelTitle }}</span>
            </div>
            <button class="panel-back-btn" @click="returnToControls" title="返回商店配置（选中保留，点击商品可继续编辑）">← 返回</button>
          </div>
          <div class="panel-body">
            <!-- 单选：物品参数编辑 -->
            <template v-if="panelItem && panelLoc">
              <div class="field-row">
                <span class="form-label">商品</span>
                <div class="pick-row">
                  <div class="item-icon-frame">
                    <img
                      v-if="getItemIconUrl(panelItem.itemId)"
                      :src="getItemIconUrl(panelItem.itemId)" :alt="getItemName(panelItem.itemId)"
                      @error="handleItemImageError(panelItem.itemId)"
                    />
                  </div>
                  <span class="item-name">{{ getItemName(panelItem.itemId) }}</span>
                  <button
                    @click="openSearch({ type: 'item-replace', shopIndex: panelLoc.shopIndex, goodsIndex: panelLoc.goodsIndex })"
                    class="ghost-btn accent"
                  >更换</button>
                </div>
              </div>
              <div class="field-row">
                <span class="form-label">价格</span>
                <div class="price-inputs">
                  <div class="price-item">
                    <input type="number" min="0" :value="fromCopper(panelItem.price).g"
                      @input="setPrice(panelItem, 'g', $event.target.value)" class="price-input" placeholder="0" />
                    <span class="price-label">金</span>
                  </div>
                  <div class="price-item">
                    <input type="number" min="0" max="99" :value="fromCopper(panelItem.price).s"
                      @input="setPrice(panelItem, 's', $event.target.value)" class="price-input" placeholder="0" />
                    <span class="price-label">银</span>
                  </div>
                  <div class="price-item">
                    <input type="number" min="0" max="99" :value="fromCopper(panelItem.price).c"
                      @input="setPrice(panelItem, 'c', $event.target.value)" class="price-input" placeholder="0" />
                    <span class="price-label">铜</span>
                  </div>
                </div>
              </div>
              <div class="field-row field-row-sm">
                <span class="form-label">数量</span>
                <input type="number" v-model.number="panelItem.stack" min="1" class="form-input stack-input" />
              </div>
              <div class="field-row">
                <span class="form-label">解锁条件</span>
                <div class="cond-row">
                  <select v-model="panelItem.condition.type" class="form-select cond-select">
                    <option v-for="t in conditionTypes" :key="t.value" :value="t.value">{{ t.name }}</option>
                  </select>
                  <select v-if="panelItem.condition.type === 'boss'" v-model="panelItem.condition.flag" class="form-select cond-select">
                    <option v-for="b in bossFlags" :key="b.flag" :value="b.flag">{{ b.name }}</option>
                  </select>
                  <input
                    v-else-if="panelItem.condition.type === 'kill'"
                    :value="getCondNpcText(panelItem.condition)"
                    @input="setCondNpcText(panelItem.condition, $event.target.value)"
                    class="form-input cond-input" placeholder="NPC ID，如 266 / 13,14,15"
                  />
                </div>
              </div>
              <div class="panel-foot">
                <button class="ghost-btn danger" @click="removePanelItem">移除商品</button>
                <button class="save-btn sm" @click="panelClose">完成</button>
              </div>
            </template>

            <!-- 多选：批量赋值 -->
            <template v-else>
              <p class="batch-tip">
                已选 <strong>{{ selectedItems.size }}</strong> 个物品。填写后点击「应用到选中」统一覆盖，<em>留空字段不修改</em>。
              </p>
              <div class="field-row">
                <span class="form-label">价格（金/银/铜，留空不修改）</span>
                <div class="price-inputs">
                  <div class="price-item">
                    <input type="number" min="0" v-model="batchForm.g" class="price-input" placeholder="—" />
                    <span class="price-label">金</span>
                  </div>
                  <div class="price-item">
                    <input type="number" min="0" max="99" v-model="batchForm.s" class="price-input" placeholder="—" />
                    <span class="price-label">银</span>
                  </div>
                  <div class="price-item">
                    <input type="number" min="0" max="99" v-model="batchForm.c" class="price-input" placeholder="—" />
                    <span class="price-label">铜</span>
                  </div>
                </div>
              </div>
              <div class="field-row field-row-sm">
                <span class="form-label">数量（留空不修改）</span>
                <input type="number" v-model="batchForm.stack" min="1" class="form-input stack-input" placeholder="—" />
              </div>
              <div class="field-row">
                <span class="form-label">解锁条件（选择后应用）</span>
                <div class="cond-row">
                  <select v-model="batchForm.condType" class="form-select cond-select">
                    <option value="">不修改</option>
                    <option v-for="t in conditionTypes" :key="t.value" :value="t.value">{{ t.name }}</option>
                  </select>
                  <select v-if="batchForm.condType === 'boss'" v-model="batchForm.flag" class="form-select cond-select">
                    <option v-for="b in bossFlags" :key="b.flag" :value="b.flag">{{ b.name }}</option>
                  </select>
                  <input
                    v-else-if="batchForm.condType === 'kill'"
                    v-model="batchForm.npcIds"
                    class="form-input cond-input" placeholder="NPC ID，如 266 / 13,14,15"
                  />
                </div>
              </div>
              <button class="save-btn sm batch-apply" @click="applyBatch" :disabled="selectedItems.size === 0">
                应用到选中（{{ selectedItems.size }}）
              </button>
              <div class="batch-list">
                <div v-for="entry in selectedList" :key="entry.shopIndex + '-' + entry.goodsIndex" class="batch-row">
                  <img
                    v-if="getItemIconUrl(entry.item.itemId)"
                    :src="getItemIconUrl(entry.item.itemId)"
                    :alt="getItemName(entry.item.itemId)"
                    class="batch-item-icon"
                    @error="handleItemImageError(entry.item.itemId)"
                  />
                  <span class="batch-item-name">{{ getItemName(entry.item.itemId) }}</span>
                  <button class="mini-btn" title="移出选择" @click="removeFromSelection(entry.item)">×</button>
                </div>
              </div>
            </template>
          </div>
        </div>
      </Transition>
      </div>
    </template>

    <!-- ═══════ 控件编辑弹窗 ═══════ -->
    <div v-if="controlModal !== null && modalControl" class="modal-overlay" @click.self="controlModal = null">
      <div class="modal-card">
        <div class="modal-head">
          <span class="modal-title">控件 · 格 {{ modalControl.slot + 1 }}</span>
          <button class="mini-btn" @click="controlModal = null" title="关闭">×</button>
        </div>
        <div class="modal-body">
          <div class="field-row">
            <span class="form-label">控件物品</span>
            <div class="pick-row">
              <div class="item-icon-frame">
                <img
                  v-if="getItemIconUrl(modalControl.statueItemId)"
                  :src="getItemIconUrl(modalControl.statueItemId)" :alt="getItemName(modalControl.statueItemId)"
                  @error="handleItemImageError(modalControl.statueItemId)"
                />
              </div>
              <span class="item-name">{{ getItemName(modalControl.statueItemId) }}</span>
              <button @click="openSearch({ type: 'control-replace', slot: modalControl.slot })" class="ghost-btn accent">更换</button>
            </div>
          </div>
          <div class="field-row">
            <span class="form-label">跳转到商店</span>
            <select v-model.number="modalControl.targetShopIndex" class="form-select">
              <option v-for="(shop, si) in config.shops" :key="si" :value="si">
                {{ shop.name || ('商店 ' + (si + 1)) }}
              </option>
            </select>
          </div>
          <div class="field-row">
            <span class="form-label">名称（可选）</span>
            <input v-model="modalControl.name" class="form-input" placeholder="控件名称" />
          </div>
        </div>
        <div class="modal-foot">
          <button class="ghost-btn danger" @click="removeControl">移除控件</button>
          <button class="save-btn sm" @click="controlModal = null">完成</button>
        </div>
      </div>
    </div>

    <!-- Toast -->
    <Transition name="toast">
      <div v-if="success" class="toast toast-success">{{ success }}</div>
    </Transition>
    <Transition name="toast">
      <div v-if="error" class="toast toast-error">{{ error }}</div>
    </Transition>

    <ItemSearchDialog
      :show="showItemSearch"
      mode="restrict"
      @select="handleItemSelect"
      @close="showItemSearch = false"
    />
    <ItemSearchDialog
      :show="showBatchDialog"
      mode="restrict"
      multi
      @select="handleBatchSelect"
      @close="showBatchDialog = false"
    />
  </div>
</template>

<style scoped>
.settings-page { padding: 20px; width: 100%; }
.loading-state { text-align: center; padding: 60px; color: var(--text-muted); }

/* ═══════ 卡片 ═══════ */
.section-card {
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-xl);
  padding: 18px 22px;
  margin-bottom: 16px;
  box-shadow: var(--shadow-md);
}
.section-card h3 { margin: 0; color: var(--text-primary); font-size: 1.05rem; font-weight: 600; }
.section-desc { margin: 4px 0 12px 0; color: var(--text-muted); font-size: 0.82rem; line-height: 1.6; }
.section-desc strong { color: var(--accent-primary); }

/* ═══════ 三段布局（控件 / 商店内容 / 编辑面板） ═══════ */
.shopui-layout {
  display: flex;
  align-items: flex-start;
  gap: 16px;
}
/* 控件面板：选中商品编辑时向左滑出隐藏（宽度收缩 + 淡出），商店区自动左移 */
.controls-panel {
  width: 716px;
  flex-shrink: 0;
  overflow: hidden;
  transition: width 0.35s cubic-bezier(0.4, 0, 0.2, 1), opacity 0.3s ease;
}
.shopui-layout.editing .controls-panel {
  width: 0;
  opacity: 0;
}
.layout-left {
  flex: 1;
  min-width: 0;
}
.edit-panel {
  width: 380px;
  flex-shrink: 0;
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-md);
  overflow: hidden;
  position: sticky;
  top: 16px;
  max-height: calc(100vh - 32px);
  display: flex;
  flex-direction: column;
}
@media (max-width: 1500px) {
  .shopui-layout { flex-direction: column; }
  .controls-panel { width: 100%; }
  .shopui-layout.editing .controls-panel { width: 0; }
  .edit-panel { position: static; width: 100%; max-height: none; }
}

/* 面板滑入动画（右→左） */
.panel-slide-enter-active { transition: transform 0.28s cubic-bezier(0.22, 1, 0.36, 1), opacity 0.28s ease; }
.panel-slide-leave-active { transition: transform 0.2s ease, opacity 0.2s ease; }
.panel-slide-enter-from { transform: translateX(60px); opacity: 0; }
.panel-slide-leave-to { transform: translateX(40px); opacity: 0; }

.panel-head {
  display: flex; align-items: center; justify-content: space-between;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border-bottom: 1px solid var(--border-light);
  flex-shrink: 0;
}
.panel-head-left { display: flex; align-items: center; gap: 8px; min-width: 0; }
.panel-head-left .panel-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.panel-back-btn {
  background: transparent;
  border: 1px solid var(--border-color);
  border-radius: 7px;
  color: var(--accent-primary);
  font-size: 0.78rem;
  padding: 3px 9px;
  cursor: pointer;
  white-space: nowrap;
  flex-shrink: 0;
  transition: all 0.2s ease;
}
.panel-back-btn:hover { border-color: var(--accent-primary); background: rgba(99, 102, 241, 0.08); }
.panel-title { font-size: 0.95rem; font-weight: 600; color: var(--accent-primary); }
.panel-body { padding: 14px 16px; display: flex; flex-direction: column; gap: 12px; overflow-y: auto; }
.panel-foot {
  display: flex; align-items: center; justify-content: space-between;
  margin-top: 4px;
}

.card-head { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 8px; }
.card-head-left { display: flex; align-items: center; gap: 8px; min-width: 0; }
.card-head-left h3 { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.count {
  font-size: 0.75rem; font-weight: 600; color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.12); border-radius: 999px; padding: 2px 9px; margin-left: 6px;
  vertical-align: middle;
}

/* ═══════ 顶部总览 ═══════ */
.overview-row { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; }
.overview-info { flex: 1; min-width: 0; }
.overview-info h3 { margin: 0 0 4px 0; }
.overview-info .section-desc { margin: 0; }
.overview-actions { display: flex; align-items: center; gap: 12px; flex-shrink: 0; }
.toggle-label { color: var(--text-primary); font-weight: 500; font-size: 0.9rem; }
.overview-sub { margin-top: 14px; border-top: 1px solid var(--border-light); padding-top: 12px; }
.summon-row { display: flex; align-items: center; gap: 10px; }
.summon-name { font-size: 0.88rem; color: var(--text-primary); font-weight: 500; flex: 1; }

/* ═══════ 开关 ═══════ */
.switch { position: relative; display: inline-block; width: 46px; height: 25px; flex-shrink: 0; }
.switch input { opacity: 0; width: 0; height: 0; }
.slider {
  position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0;
  background: var(--bg-tertiary); border: 2px solid var(--border-color); border-radius: 25px;
  transition: all 0.3s ease;
}
.slider::before {
  content: ''; position: absolute; height: 17px; width: 17px; left: 2px; bottom: 2px;
  background: var(--text-muted); border-radius: 50%; transition: all 0.3s ease;
}
.switch input:checked + .slider { background: var(--accent-primary); border-color: var(--accent-primary); }
.switch input:checked + .slider::before { transform: translateX(21px); background: white; }

/* ═══════ 40 格网格（60px 格子） ═══════ */
.goods-grid {
  display: grid;
  grid-template-columns: repeat(10, 60px);
  gap: 8px;
}
.cell {
  width: 60px; height: 60px;
  border-radius: 10px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  display: flex; align-items: center; justify-content: center;
  cursor: pointer; position: relative;
  transition: all 0.15s; overflow: hidden;
  box-sizing: border-box;
}
.cell:hover { border-color: var(--accent-primary); }
.cell-empty { border-style: dashed; }
.cell img { width: 46px; height: 46px; object-fit: contain; image-rendering: pixelated; }
.cell-control { background: rgba(139, 92, 246, 0.1); border-color: rgba(139, 92, 246, 0.5); cursor: not-allowed; }
.cell-control:hover { border-color: #8b5cf6; }
.cell-selected {
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.25);
}
/* 拖放目标高亮 */
.cell-drag-over {
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.25);
}
.cell-plus { color: var(--text-muted); font-size: 26px; line-height: 1; opacity: 0.5; }
.cell-tag {
  position: absolute; bottom: 2px; left: 50%; transform: translateX(-50%);
  font-size: 9px; line-height: 1; color: #8b5cf6; background: rgba(139, 92, 246, 0.15);
  padding: 0 5px; border-radius: 4px; white-space: nowrap;
}
/* 勾选角标（hover 显示） */
.cell-check {
  position: absolute; top: 3px; left: 3px;
  width: 18px; height: 18px;
  display: flex; align-items: center; justify-content: center;
  cursor: pointer;
  opacity: 0;
  transition: opacity 0.15s ease;
}
.cell:hover .cell-check,
.cell-selected .cell-check { opacity: 1; }
.cell-check input { position: absolute; opacity: 0; width: 0; height: 0; }
.checkmark {
  width: 18px; height: 18px;
  border-radius: 5px;
  background: var(--bg-card);
  border: 2px solid var(--border-color);
  box-sizing: border-box;
  display: flex; align-items: center; justify-content: center;
  transition: all 0.15s ease;
}
.cell-check input:checked + .checkmark {
  background: var(--accent-primary);
  border-color: var(--accent-primary);
}
.cell-check input:checked + .checkmark::after {
  content: '✓'; color: #fff; font-size: 12px; line-height: 1; font-weight: 700;
}

/* ═══════ 商店卡片 ═══════ */
.shop-card {
  border: 1px solid var(--border-light);
  border-radius: var(--radius-lg);
  padding: 10px 12px 12px;
  margin-bottom: 10px;
  background: var(--bg-secondary);
}
.shop-head {
  display: flex; align-items: center; gap: 8px;
  padding-bottom: 8px; margin-bottom: 10px;
  border-bottom: 1px solid var(--border-light);
}
.shop-seq {
  width: 22px; height: 22px; flex-shrink: 0;
  border-radius: 6px; background: var(--shop-accent); color: white;
  font-weight: 700; font-size: 0.8rem;
  display: flex; align-items: center; justify-content: center;
}
.shop-name-input { max-width: 200px; padding: 5px 9px !important; font-size: 0.84rem !important; }
.badge {
  font-size: 0.72rem; padding: 2px 8px; border-radius: 999px;
  background: rgba(99, 102, 241, 0.12); color: var(--accent-primary); font-weight: 600;
}
.badge-full { background: rgba(245, 158, 11, 0.15); color: #f59e0b; }

/* ═══════ 表单 ═══════ */
.form-label { display: block; margin-bottom: 5px; color: var(--text-secondary); font-size: 0.8rem; font-weight: 500; }
.form-select, .form-input {
  width: 100%; padding: 7px 10px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 9px;
  color: var(--text-primary); font-size: 0.85rem; outline: none;
  transition: border-color 0.2s, box-shadow 0.2s; box-sizing: border-box;
}
.form-select:focus, .form-input:focus { border-color: var(--accent-primary); box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.12); }
.form-select option { background: var(--bg-card); color: var(--text-primary); }
.stack-input { width: 64px; }

.item-icon-frame {
  width: 32px; height: 32px; flex-shrink: 0;
  border-radius: 8px;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.12), rgba(139, 92, 246, 0.06));
  border: 1px solid rgba(99, 102, 241, 0.15);
  display: flex; align-items: center; justify-content: center; overflow: hidden;
}
.item-icon-frame img { width: 26px; height: 26px; object-fit: contain; image-rendering: pixelated; }
.item-name { font-size: 0.8rem; color: var(--text-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 160px; }

.pick-row { display: flex; align-items: center; gap: 8px; }

/* ═══════ 按钮 ═══════ */
.ghost-btn {
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  color: var(--text-primary);
  padding: 5px 11px;
  font-size: 0.82rem;
  cursor: pointer;
  transition: all 0.2s ease;
}
.ghost-btn:hover { border-color: var(--accent-primary); }
.ghost-btn.accent { color: var(--accent-primary); border-color: var(--accent-primary); }
.ghost-btn.danger { color: var(--accent-error, #ef4444); border-color: var(--accent-error, #ef4444); }
.mini-btn {
  width: 24px; height: 24px; flex-shrink: 0;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  color: var(--text-primary); font-size: 0.9rem;
  cursor: pointer; transition: all 0.15s ease;
}
.mini-btn:hover { border-color: var(--accent-primary); }
.mini-btn.accent { color: var(--accent-primary); border-color: var(--accent-primary); }
.mini-btn.danger:hover { border-color: var(--accent-error, #ef4444); color: var(--accent-error, #ef4444); }

.save-btn {
  padding: 8px 20px;
  border: none;
  border-radius: 9px;
  background: linear-gradient(135deg, #6366f1, #8b5cf6);
  color: white;
  font-size: 0.88rem;
  font-weight: 600;
  cursor: pointer;
  box-shadow: 0 4px 14px rgba(99, 102, 241, 0.3);
  transition: all 0.25s;
}
.save-btn:hover { transform: translateY(-1px); box-shadow: 0 6px 18px rgba(99, 102, 241, 0.4); }
.save-btn:disabled { opacity: 0.55; cursor: not-allowed; transform: none; }
.save-btn.sm { padding: 6px 16px; font-size: 0.84rem; }

.field-row-sm { display: flex; align-items: center; gap: 10px; }
.field-row-sm .form-label { margin: 0; }

.price-inputs { display: flex; gap: 5px; }
.price-item { display: flex; align-items: center; gap: 3px; }
.price-input {
  width: 44px; padding: 6px 3px; text-align: center;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: 7px;
  color: var(--text-primary); font-size: 0.84rem; outline: none;
  -moz-appearance: textfield;
}
.price-input::-webkit-outer-spin-button,
.price-input::-webkit-inner-spin-button { -webkit-appearance: none; margin: 0; }
.price-input:focus { border-color: var(--accent-primary); }
.price-label { font-size: 0.7rem; color: var(--text-muted); }

.cond-row { display: flex; gap: 6px; flex-wrap: wrap; }
.cond-row .cond-select { min-width: 120px; flex: 1; }
.cond-input { min-width: 150px; flex: 1; }

/* ═══════ 批量赋值 ═══════ */
.batch-tip { margin: 0; font-size: 0.82rem; color: var(--text-secondary); line-height: 1.6; }
.batch-tip strong { color: var(--accent-primary); }
.batch-tip em { color: var(--text-muted); font-style: normal; }
.batch-apply { width: 100%; justify-content: center; }
.batch-list {
  display: flex; flex-direction: column; gap: 6px;
  max-height: 220px; overflow-y: auto;
  border-top: 1px solid var(--border-light); padding-top: 10px;
}
.batch-row {
  display: flex; align-items: center; gap: 8px;
  padding: 6px 8px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-light);
  border-radius: 8px;
}
.batch-item-icon { width: 28px; height: 28px; object-fit: contain; image-rendering: pixelated; flex-shrink: 0; }
.batch-item-name {
  flex: 1; min-width: 0;
  font-size: 0.8rem; color: var(--text-primary);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}

/* ═══════ 弹窗 ═══════ */
.modal-overlay {
  position: fixed; inset: 0; z-index: 1200;
  background: rgba(0, 0, 0, 0.45);
  display: flex; align-items: center; justify-content: center;
  padding: 20px;
}
.modal-card {
  width: 480px; max-width: 100%;
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 16px;
  box-shadow: 0 20px 50px rgba(0, 0, 0, 0.35);
  overflow: hidden;
}
.modal-head {
  display: flex; align-items: center; justify-content: space-between;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border-bottom: 1px solid var(--border-light);
}
.modal-title { font-size: 0.95rem; font-weight: 600; color: var(--accent-primary); }
.modal-body { padding: 14px 16px; display: flex; flex-direction: column; gap: 12px; }
.modal-foot {
  display: flex; align-items: center; justify-content: space-between;
  padding: 12px 16px;
  border-top: 1px solid var(--border-light);
}

/* ═══════ Toast ═══════ */
.toast {
  position: fixed; top: 20px; right: 20px;
  padding: 11px 17px; border-radius: 10px; font-size: 0.88rem;
  z-index: 2000; box-shadow: 0 8px 24px rgba(0, 0, 0, 0.18);
  max-width: 380px;
}
.toast-success { background: rgba(34, 197, 94, 0.15); color: #10b981; border: 1px solid rgba(34, 197, 94, 0.3); }
.toast-error { background: rgba(239, 68, 68, 0.15); color: #ef4444; border: 1px solid rgba(239, 68, 68, 0.3); }
.toast-enter-active, .toast-leave-active { transition: all 0.3s ease; }
.toast-enter-from, .toast-leave-to { opacity: 0; transform: translateY(-10px); }
</style>
