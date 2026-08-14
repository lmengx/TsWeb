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
  statueControls: [], // [{ slot, statueItemId, targetShopIndex, name }]
  shops: []          // [{ name, items: [{ itemId, price, stack, condition }] }]
})

const slots40 = Array.from({ length: 40 }, (_, i) => i)

const controlBySlot = (slot) => config.value.statueControls.find(c => c.slot === slot)
const controlSlots = computed(() => new Set(config.value.statueControls.map(c => c.slot)))
// 商品区 = 40 格中非控件格（有序），控件优先级高于商品
const goodsSlots = computed(() => slots40.filter(s => !controlSlots.value.has(s)))

// 编辑弹窗：null | { type:'control', slot } | { type:'item', shopIndex, goodsIndex }
const modal = ref(null)
const modalControl = computed(() =>
  modal.value?.type === 'control' ? controlBySlot(modal.value.slot) : null)
const modalItem = computed(() => {
  const m = modal.value
  if (m?.type !== 'item') return null
  return config.value.shops[m.shopIndex]?.items[m.goodsIndex] ?? null
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

// 商店序号渐变色（循环）
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
// {type:'summon'} | {type:'control-add', slot} | {type:'control-replace', slot}
// | {type:'item-add', shopIndex} | {type:'item-replace', shopIndex, goodsIndex}
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
  } else if (t.type === 'control-replace') {
    const c = controlBySlot(t.slot)
    if (c) c.statueItemId = item.id
  } else if (t.type === 'item-add') {
    config.value.shops[t.shopIndex].items.push({
      itemId: item.id, price: 100, stack: 1,
      condition: { type: 'always', flag: '', npcId: 0, npcIds: [] }
    })
    const gi = Math.min(config.value.shops[t.shopIndex].items.length - 1, goodsSlots.value.length - 1)
    modal.value = { type: 'item', shopIndex: t.shopIndex, goodsIndex: gi }
  } else if (t.type === 'item-replace') {
    config.value.shops[t.shopIndex].items[t.goodsIndex].itemId = item.id
  }
  showItemSearch.value = false
}

// ═════════ 格子点击 ═════════
const onControlCellClick = (slot) => {
  if (controlBySlot(slot)) modal.value = { type: 'control', slot }
  else openSearch({ type: 'control-add', slot })
}
const onGoodsCellClick = (shopIndex, slot) => {
  const gi = goodsSlots.value.indexOf(slot)
  if (gi >= 0 && config.value.shops[shopIndex]?.items[gi]) {
    modal.value = { type: 'item', shopIndex, goodsIndex: gi }
  } else {
    openSearch({ type: 'item-add', shopIndex })
  }
}
const onLockedControlClick = () => {
  success.value = '控件格为全局锁定，请到顶部「控件」面板编辑'
  setTimeout(() => { success.value = '' }, 2000)
}
const cellTitle = (shop, slot) => {
  const c = controlBySlot(slot)
  if (c) return '控件：' + getItemName(c.statueItemId) + '（顶部控件面板编辑）'
  const gi = goodsSlots.value.indexOf(slot)
  const item = shop.items[gi]
  return item ? getItemName(item.itemId) : '点击选择物品添加商品'
}

// ═════════ 弹窗操作 ═════════
const removeModalControl = () => {
  const m = modal.value
  if (m?.type !== 'control') return
  const idx = config.value.statueControls.findIndex(c => c.slot === m.slot)
  if (idx >= 0) config.value.statueControls.splice(idx, 1)
  modal.value = null
}
const removeModalItem = () => {
  const m = modal.value
  if (m?.type !== 'item') return
  config.value.shops[m.shopIndex].items.splice(m.goodsIndex, 1)
  modal.value = null
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
  modal.value = null
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
      setTimeout(() => { success.value = '' }, 2000)
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
            <p class="section-desc">40 格面板（10×4）可视化配置：点击格子指定「控件」或「商品」，保存后即时生效</p>
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

      <div class="shopui-layout">
      <!-- ═══════ 左栏：控件面板 ═══════ -->
      <div class="layout-left">
      <div class="section-card">
        <div class="card-head">
          <h3>控件（商店切换按钮）<span class="count">{{ config.statueControls.length }}</span></h3>
        </div>
        <p class="section-desc">
          点击格子 → 选择物品即成为控件。控件优先占格：占用的格子在<strong>所有商店中锁定</strong>，剩余 {{ goodsSlots.length }} 格才是商品区，数量不限。
        </p>

        <div class="goods-grid">
          <div
            v-for="slot in slots40"
            :key="'c' + slot"
            class="cell"
            :class="{ 'cell-control': controlBySlot(slot), 'cell-empty': !controlBySlot(slot) }"
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
            <span v-else class="cell-plus">+</span>
          </div>
        </div>
      </div>
      </div>

      <!-- ═══════ 右栏：商店内容 ═══════ -->
      <div class="layout-right">
      <div class="section-card">
        <div class="card-head">
          <h3>商店内容<span class="count">{{ config.shops.length }}</span></h3>
          <button class="ghost-btn accent" @click="addShop">+ 添加商店</button>
        </div>
        <p class="section-desc">控件格锁定（紫色），剩余 {{ goodsSlots.length }} 格为商品区；商品超出自动截断。</p>

        <div v-for="(shop, shopIndex) in config.shops" :key="shopIndex" class="shop-card">
          <div class="shop-head" :style="{ '--shop-accent': shopAccent(shopIndex) }">
            <span class="shop-seq">{{ shopIndex + 1 }}</span>
            <input v-model="shop.name" class="form-input shop-name-input" placeholder="商店名称" />
            <span class="badge" :class="{ 'badge-full': shop.items.length > goodsSlots.length }">
              {{ shop.items.length }}/{{ goodsSlots.length }}{{ shop.items.length > goodsSlots.length ? ' 截断' : '' }}
            </span>
            <button @click="removeShop(shopIndex)" class="mini-btn danger" title="删除商店">×</button>
          </div>

          <div class="goods-grid">
            <div
              v-for="slot in slots40"
              :key="'s' + shopIndex + '-' + slot"
              class="cell"
              :class="{
                'cell-control': controlBySlot(slot),
                'cell-empty': !controlBySlot(slot) && !(goodsSlots.indexOf(slot) < shop.items.length)
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
                <span v-else class="cell-plus">+</span>
              </template>
            </div>
          </div>
        </div>
      </div>
      </div>
      </div>
    </template>

    <!-- ═══════ 编辑弹窗 ═══════ -->
    <div v-if="modal" class="modal-overlay" @click.self="modal = null">
      <div class="modal-card">
        <!-- 控件编辑 -->
        <template v-if="modalControl">
          <div class="modal-head">
            <span class="modal-title">控件 · 格 {{ modalControl.slot + 1 }}</span>
            <button class="mini-btn" @click="modal = null" title="关闭">×</button>
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
            <button class="ghost-btn danger" @click="removeModalControl">移除控件</button>
            <button class="save-btn sm" @click="modal = null">完成</button>
          </div>
        </template>

        <!-- 商品编辑 -->
        <template v-else-if="modalItem">
          <div class="modal-head">
            <span class="modal-title">商品 · 格 {{ goodsSlots[modal.goodsIndex] + 1 }}</span>
            <button class="mini-btn" @click="modal = null" title="关闭">×</button>
          </div>
          <div class="modal-body">
            <div class="field-row">
              <span class="form-label">商品</span>
              <div class="pick-row">
                <div class="item-icon-frame">
                  <img
                    v-if="getItemIconUrl(modalItem.itemId)"
                    :src="getItemIconUrl(modalItem.itemId)" :alt="getItemName(modalItem.itemId)"
                    @error="handleItemImageError(modalItem.itemId)"
                  />
                </div>
                <span class="item-name">{{ getItemName(modalItem.itemId) }}</span>
                <button
                  @click="openSearch({ type: 'item-replace', shopIndex: modal.shopIndex, goodsIndex: modal.goodsIndex })"
                  class="ghost-btn accent"
                >更换</button>
              </div>
            </div>
            <div class="field-row">
              <span class="form-label">价格</span>
              <div class="price-inputs">
                <div class="price-item">
                  <input type="number" min="0" :value="fromCopper(modalItem.price).g"
                    @input="setPrice(modalItem, 'g', $event.target.value)" class="price-input" placeholder="0" />
                  <span class="price-label">金</span>
                </div>
                <div class="price-item">
                  <input type="number" min="0" max="99" :value="fromCopper(modalItem.price).s"
                    @input="setPrice(modalItem, 's', $event.target.value)" class="price-input" placeholder="0" />
                  <span class="price-label">银</span>
                </div>
                <div class="price-item">
                  <input type="number" min="0" max="99" :value="fromCopper(modalItem.price).c"
                    @input="setPrice(modalItem, 'c', $event.target.value)" class="price-input" placeholder="0" />
                  <span class="price-label">铜</span>
                </div>
              </div>
            </div>
            <div class="field-row field-row-sm">
              <span class="form-label">数量</span>
              <input type="number" v-model.number="modalItem.stack" min="1" class="form-input stack-input" />
            </div>
            <div class="field-row">
              <span class="form-label">解锁条件</span>
              <div class="cond-row">
                <select v-model="modalItem.condition.type" class="form-select cond-select">
                  <option v-for="t in conditionTypes" :key="t.value" :value="t.value">{{ t.name }}</option>
                </select>
                <select v-if="modalItem.condition.type === 'boss'" v-model="modalItem.condition.flag" class="form-select cond-select">
                  <option v-for="b in bossFlags" :key="b.flag" :value="b.flag">{{ b.name }}</option>
                </select>
                <input
                  v-else-if="modalItem.condition.type === 'kill'"
                  :value="getCondNpcText(modalItem.condition)"
                  @input="setCondNpcText(modalItem.condition, $event.target.value)"
                  class="form-input cond-input" placeholder="NPC ID，如 266 / 13,14,15"
                />
              </div>
            </div>
          </div>
          <div class="modal-foot">
            <button class="ghost-btn danger" @click="removeModalItem">移除商品</button>
            <button class="save-btn sm" @click="modal = null">完成</button>
          </div>
        </template>
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

/* ═══════ 两栏布局（左控件 / 右商店） ═══════ */
.shopui-layout {
  display: flex;
  align-items: flex-start;
  gap: 16px;
}
.layout-left {
  width: 716px;
  flex-shrink: 0;
}
.layout-right {
  flex: 1;
  min-width: 0;
}
@media (max-width: 1500px) {
  .shopui-layout { flex-direction: column; }
  .layout-left { width: 100%; }
}

.card-head { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 8px; }
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
.cell-control { background: rgba(139, 92, 246, 0.1); border-color: rgba(139, 92, 246, 0.5); }
.cell-control:hover { border-color: #8b5cf6; }
.cell-plus { color: var(--text-muted); font-size: 26px; line-height: 1; opacity: 0.5; }
.cell-tag {
  position: absolute; bottom: 2px; left: 50%; transform: translateX(-50%);
  font-size: 9px; line-height: 1; color: #8b5cf6; background: rgba(139, 92, 246, 0.15);
  padding: 0 5px; border-radius: 4px; white-space: nowrap;
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
