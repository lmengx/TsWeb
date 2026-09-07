<script setup>
import { ref, computed, watch, onUnmounted } from 'vue'
import { apiRequest, post } from '../utils/api.js'

const props = defineProps({
  show: Boolean,
  /** 目标服务器 id（缺省用当前选中服务器） */
  serverId: { type: String, default: '' },
  serverName: { type: String, default: '' }
})

const emit = defineEmits(['close', 'completed'])

// ═══════════════ 步骤导航 ═══════════════
const step = ref(1)
const STEPS = [
  { n: 1, title: '玩家群体' },
  { n: 2, title: 'SSC 配置' },
  { n: 3, title: '注册模式' },
  { n: 4, title: '反作弊' },
  { n: 5, title: '反恶性 bug' },
  { n: 6, title: '快速权限' }
]

// ═══════════════ 各步骤表单状态 ═══════════════
const audience = ref('friends')          // friends | public
const touched = ref({})                  // 用户手动改过的选项集合（群体切换时不覆盖）

const sscEnabled = ref(false)            // SSC 开关（读自 sscconfig.json）
const sscRawContent = ref('')            // 原始 sscconfig.json 文本（保存时改 Enabled 回写）
const sscLoading = ref(false)

const registerMode = ref('auto')         // default | auto | block（朋友服默认推荐 auto）

const acItem = ref(false)                // 反作弊·物品（朋友服不推荐开 → 默认关）
const acProj = ref(false)                // 反作弊·弹幕

const bfEnabled = ref(false)             // 反恶性 bug 总开关（朋友服不推荐开 → 默认关）
const bfLoginFix = ref(true)             // 子功能默认全开
const bfChestFix = ref(true)
const bfMinionLimit = ref(true)
const bfLightning = ref(true)

// ═══════════════ 快速权限（步骤 6：组权限推荐） ═══════════════
// 复用 PermissionManager.vue 的预设：常规 15 项 + tp 4 项
const REASONABLE_PERMS = [
  'tshock.npc.hurttown', 'tshock.npc.spawnpets', 'tshock.npc.startdd2', 'tshock.npc.startinvasion',
  'tshock.npc.summonboss', 'tshock.tp.demonconch', 'tshock.tp.magicconch', 'tshock.tp.pylon',
  'tshock.tp.rod', 'tshock.tp.tppotion', 'tshock.tp.wormhole', 'tshock.world.movenpc',
  'tshock.world.time.usemoondial', 'tshock.world.time.usesundial', 'tshock.world.worldupgrades'
]
const TP_PERMS = ['tshock.tp.self', 'tshock.tp.block', 'tshock.tp.spawn', 'tshock.tp.home']

const groupApplying = ref(null)          // 正在应用的预设标识（'friends' | 'public-default' | 'public-vip'）
const groupDone = ref({})                // 已成功应用的预设标识集合
const groupMsg = ref('')                 // 步骤 6 操作结果提示

// 跳过确认弹窗（未添加任何组权限时点「完成」触发）
const showSkipConfirm = ref(false)
const skipCountdown = ref(0)
let countdownTimer = null

const saving = ref(false)
const skipToast = ref(false)
let skipToastTimer = null

// ═══════════════ 群体映射：显示文本与推荐值 ═══════════════
// 群体切换时对「未手动修改」的选项应用推荐值
const applyAudience = (a) => {
  audience.value = a
  if (a === 'friends') {
    // SSC：可开可不开，保持当前值不强制
    if (!touched.value.registerMode) registerMode.value = 'auto'   // 推荐自动注册
    if (!touched.value.acItem) acItem.value = false                // 不推荐开：防止误踢
    if (!touched.value.acProj) acProj.value = false
    if (!touched.value.bfEnabled) bfEnabled.value = false          // 不推荐开：防止误踢
  } else {
    if (!touched.value.sscEnabled) sscEnabled.value = true         // 必开：强制开荒
    // 注册模式：公开服无单一推荐（auto=管理在线服 / block=白名单或QQ机器人服），保持当前值
    if (!touched.value.acItem) acItem.value = true                 // 必开
    if (!touched.value.acProj) acProj.value = true                 // 必开
    if (!touched.value.bfEnabled) bfEnabled.value = true           // 必开：不开可能被恶意炸档
  }
}

const markTouched = (key) => { touched.value[key] = true }

// ═══════════════ SSC 步骤文本（随群体 + 开关状态） ═══════════════
const sscDesc = computed(() => {
  if (audience.value === 'public') {
    return sscEnabled.value
      ? '开启后强制开荒：玩家无法带外部物品进入，从零开始'
      : '危险：玩家可以随意带外部物品进入，必被炸图毁档'
  }
  return sscEnabled.value
    ? '开启后存档保存在服务器，换设备不丢档'
    : '默认行为：角色随客户端本地，好友可带外部物品进入'
})

// 公开服务器关闭 SSC → 红色阴影边框
const sscDanger = computed(() => audience.value === 'public' && !sscEnabled.value)
// 公开服务器开启 SSC → 流光"必开"徽标
const sscMustBadge = computed(() => audience.value === 'public' && sscEnabled.value)

// ═══════════════ 注册模式文本（随群体） ═══════════════
const regDesc = (mode) => {
  if (mode === 'auto') {
    return audience.value === 'friends'
      ? '自动注册，可免去 tshock 手动注册的门槛'
      : '推荐管理在线的服务器使用'
  }
  if (mode === 'block') {
    return audience.value === 'friends'
      ? '阻止注册（不推荐）'
      : '推荐使用白名单或 QQ 机器人的服务器开'
  }
  return audience.value === 'friends'
    ? 'tshock 默认行为'
    : 'tshock 默认行为（不推荐）'
}
const regBadge = (mode) => {
  if (mode === 'auto') {
    // 好友服：唯一推荐；公开服：管理在线的服务器推荐用
    return { text: '推荐', cls: 'green' }
  }
  if (mode === 'block') {
    // 好友服：不推荐；公开服：白名单/QQ 机器人服推荐用
    return audience.value === 'friends' ? { text: '不推荐', cls: 'gray' } : { text: '推荐', cls: 'green' }
  }
  // default：公开服明确不推荐；好友服中性（无徽标）
  return audience.value === 'public' ? { text: '不推荐', cls: 'gray' } : { text: '', cls: '' }
}

// ═══════════════ 反作弊 / 反恶性 bug 文本（随群体） ═══════════════
const antiCheatHint = computed(() =>
  audience.value === 'public'
    ? '必开：防止玩家通过修改器等手段获取违规物品并传播'
    : '不推荐开：防止误踢'
)
const bugFixHint = computed(() =>
  audience.value === 'public'
    ? '必开：不开可能被恶意炸档'
    : '不推荐开：防止误踢'
)
const antiCheatMust = computed(() => audience.value === 'public')
const bugFixMust = computed(() => audience.value === 'public')

// ═══════════════ 步骤 6 组权限推荐（随群体） ═══════════════
const permExplain = computed(() =>
  audience.value === 'friends'
    ? '推荐给朋友服玩家所在的 default 组添加常规与传送（tp）权限，方便朋友游玩。'
    : '推荐给 default 组添加常规权限（普通玩家基本玩法），tp 权限只给 vip 组（需在「插件设置-权限提升」配置按游玩时长授予 vip）。'
)
const permGroups = computed(() => {
  if (audience.value === 'friends') {
    return [
      { key: 'friends', label: '常规 + tp → default 组', groupName: 'default',
        perms: [...REASONABLE_PERMS, ...TP_PERMS], desc: `${REASONABLE_PERMS.length + TP_PERMS.length} 项权限（召唤boss/晶塔/传送杖/移动npc/传送等）` }
    ]
  }
  return [
    { key: 'public-default', label: '常规 → default 组', groupName: 'default',
      perms: REASONABLE_PERMS, desc: `${REASONABLE_PERMS.length} 项权限（召唤boss/晶塔/传送杖/移动npc等）` },
    { key: 'public-vip', label: 'tp → vip 组', groupName: 'vip',
      perms: TP_PERMS, desc: `${TP_PERMS.length} 项传送权限（需 vip 组存在）` }
  ]
})

// ═══════════════ 数据加载 ═══════════════
const loadSsc = async () => {
  if (!props.show) return
  sscLoading.value = true
  try {
    const headers = props.serverId ? { 'x-server-id': props.serverId } : {}
    const res = await apiRequest('/api/setup/ssc-config', { method: 'GET', headers })
    if (!res.ok) return
    const data = await res.json()
    const content = data?.content
    if (typeof content === 'string' && content.length) {
      sscRawContent.value = content
      try {
        const cfg = JSON.parse(content)
        sscEnabled.value = !!cfg?.Settings?.Enabled
      } catch { sscEnabled.value = false }
    }
  } catch { /* 静默失败：保持默认 */ }
  finally { sscLoading.value = false }
}

// ═══════════════ 跳过（右上角） ═══════════════
const doSkip = () => {
  showSkipToast()
  emit('close')
}

const showSkipToast = () => {
  skipToast.value = true
  clearTimeout(skipToastTimer)
  skipToastTimer = setTimeout(() => { skipToast.value = false }, 3300)
}

// ═══════════════ 步骤 6：应用组权限预设 ═══════════════
const applyGroupPreset = async (preset) => {
  if (groupApplying.value) return
  groupApplying.value = preset.key
  groupMsg.value = ''
  try {
    const headers = props.serverId ? { 'x-server-id': props.serverId } : {}
    let failed = 0
    for (const perm of preset.perms) {
      // 走后端显式路由 POST /api/tshock/groups/permission/add（JSON body）
      const res = await apiRequest('/api/tshock/groups/permission/add', {
        method: 'POST',
        headers,
        body: JSON.stringify({ groupName: preset.groupName, permission: perm })
      })
      const data = await res.json().catch(() => ({}))
      if (data?.error) failed++
    }
    if (failed > 0) {
      groupMsg.value = `⚠️ ${preset.label}：${failed} 项失败（组 "${preset.groupName}" 可能不存在）`
    } else {
      groupDone.value[preset.key] = true
      groupMsg.value = `✅ ${preset.label}：已添加`
    }
  } catch (e) {
    groupMsg.value = `❌ ${preset.label}：${e.message}`
  } finally { groupApplying.value = null }
}

const anyGroupDone = computed(() => Object.values(groupDone.value).some(v => v))

// ═══════════════ 提交 ═══════════════
const submit = async () => {
  // 未添加任何组权限 → 弹确认（3 秒冷却）
  if (!anyGroupDone.value) {
    showSkipConfirm.value = true
    skipCountdown.value = 3
    clearInterval(countdownTimer)
    countdownTimer = setInterval(() => {
      skipCountdown.value--
      if (skipCountdown.value <= 0) clearInterval(countdownTimer)
    }, 1000)
    return
  }
  await doSubmit()
}

const doSubmit = async () => {
  saving.value = true
  try {
    // 构建 ssc content：修改 Settings.Enabled 后回写
    let sscContent = sscRawContent.value
    if (sscRawContent.value) {
      try {
        const cfg = JSON.parse(sscRawContent.value)
        cfg.Settings = cfg.Settings || {}
        cfg.Settings.Enabled = sscEnabled.value
        sscContent = JSON.stringify(cfg, null, 2)
      } catch { sscContent = sscRawContent.value }
    }

    const body = {
      serverId: props.serverId || undefined,
      registerMode: registerMode.value,
      ssc: { content: sscContent },
      anticheat: { itemEnabled: acItem.value, projEnabled: acProj.value },
      bugfix: {
        enabled: bfEnabled.value,
        loginFix: bfLoginFix.value,
        chestFix: bfChestFix.value,
        minionLimit: bfMinionLimit.value,
        lightning: bfLightning.value
      }
    }

    const res = await post('/api/setup/plugin-init-v2', body)
    const data = await res.json().catch(() => ({}))
    if (res.ok && data?.success) {
      // 成功只发 completed，由父组件负责关闭并标记 done；不再 emit('close')，
      // 避免父组件把 close 一律当作「跳过」覆盖掉 done 状态
      emit('completed', data)
    } else {
      alert('初始化失败：' + (data?.error || '未知错误'))
    }
  } catch (e) {
    alert('初始化失败：' + e.message)
  } finally { saving.value = false }
}

// 跳过确认弹窗：返回添加
const backToAdd = () => {
  clearInterval(countdownTimer)
  showSkipConfirm.value = false
}
// 确定跳过（3 秒后可点）→ 继续完成初始化
const confirmSkipGrant = () => {
  clearInterval(countdownTimer)
  showSkipConfirm.value = false
  doSubmit()
}

// ═══════════════ 双击确认交互 ═══════════════
const goNext = () => { if (step.value < STEPS.length) step.value++ }

// ═══════════════ 生命周期 ═══════════════
// immediate：组件随 v-if 每次全新挂载（初始 show=true），watcher 需立即执行一次初始化与加载
watch(() => props.show, (v) => {
  if (v) {
    step.value = 1
    audience.value = 'friends'
    touched.value = {}
    // 默认值：朋友服推荐（SSC 保持加载值、注册 auto、反作弊关、bugfix 总开关关子功能开）
    registerMode.value = 'auto'
    sscEnabled.value = false
    acItem.value = false
    acProj.value = false
    bfEnabled.value = false
    bfLoginFix.value = true
    bfChestFix.value = true
    bfMinionLimit.value = true
    bfLightning.value = true
    groupDone.value = {}
    groupMsg.value = ''
    showSkipConfirm.value = false
    skipCountdown.value = 0
    clearInterval(countdownTimer)
    loadSsc()
  }
}, { immediate: true })

onUnmounted(() => {
  clearTimeout(skipToastTimer)
  clearInterval(countdownTimer)
})
</script>

<template>
  <Teleport to="body">
    <!-- 点击外部不关闭：遮罩仅作视觉隔离，无 @click.self -->
    <div v-if="show" class="init-mask">
      <div class="init-modal">
        <div class="init-head">
          <div>
            <h3>初始化插件设置</h3>
            <p class="init-sub">{{ serverName ? `服务器：${serverName}` : '分步配置新服务器的插件功能' }}</p>
          </div>
          <div class="head-actions">
            <button class="icon-btn" title="跳过" @click="doSkip">跳过</button>
          </div>
        </div>

        <!-- 步骤指示器 -->
        <div class="step-dots">
          <div
            v-for="s in STEPS"
            :key="s.n"
            class="step-dot"
            :class="{ active: step === s.n, done: step > s.n }"
          >
            <span class="dot-num">{{ s.n }}</span>
            <span class="dot-title">{{ s.title }}</span>
          </div>
        </div>

        <div class="init-body">
          <!-- ════════ 步骤 1：玩家群体（单击选中 + 双击确认） ════════ -->
          <div v-if="step === 1" class="step-pane">
            <p class="step-desc">选择服务器面向的玩家群体，后续步骤的推荐与说明将随之变化（单击选中，双击确认）</p>
            <div class="audience-cards">
              <div
                class="audience-card"
                :class="{ selected: audience === 'friends' }"
                @click="applyAudience('friends')"
                @dblclick="applyAudience('friends'); goNext()"
              >
                <div class="audience-icon">👥</div>
                <div class="audience-title">熟悉网友服</div>
                <div class="audience-desc">朋友/长期服，宽松、便捷优先</div>
              </div>
              <div
                class="audience-card"
                :class="{ selected: audience === 'public' }"
                @click="applyAudience('public')"
                @dblclick="applyAudience('public'); goNext()"
              >
                <div class="audience-icon">🌐</div>
                <div class="audience-title">公开服务器</div>
                <div class="audience-desc">面向陌生玩家，安全、管理优先</div>
              </div>
            </div>
            <p class="step-hint">
              {{ audience === 'friends'
                ? '👥 朋友服：SSC 可开可不开，注册自动，反作弊/反恶性bug 不推荐开（防误踢）'
                : '🌐 公开服：SSC 必开，反作弊/反恶性bug 必开，权限收紧' }}
            </p>
          </div>

          <!-- ════════ 步骤 2：SSC（双击行确认进入下一步） ════════ -->
          <div v-else-if="step === 2" class="step-pane">
            <p class="step-desc">服务器角色存储（Server Side Characters）开关（双击此行确认并进入下一步）</p>
            <div
              class="switch-row"
              :class="{ danger: sscDanger, must: sscMustBadge }"
              @dblclick="goNext"
            >
              <div class="switch-info">
                <div class="switch-title">启用 SSC（服务器角色存储）</div>
                <div class="switch-desc">{{ sscDesc }}</div>
              </div>
              <div class="switch-right">
                <span v-if="sscMustBadge" class="flow-badge">必开 · 强制开荒</span>
                <label class="switch">
                  <input type="checkbox" v-model="sscEnabled" @change="markTouched('sscEnabled')" />
                  <span class="slider"></span>
                </label>
              </div>
            </div>
            <div v-if="sscDanger" class="danger-tip">
              ⚠️ 危险：玩家可以随意带外部物品进入，必被炸图毁档
            </div>
            <div v-else-if="audience === 'public' && sscEnabled" class="ok-tip">
              ✅ 强制开荒：玩家无法带外部物品进入，从零开始
            </div>
            <div v-else-if="audience === 'friends'" class="neutral-tip">
              好友服可开可不开：开=存档存服务器；关=好友可带外部物品进入
            </div>
          </div>

          <!-- ════════ 步骤 3：注册模式（单击选中 + 双击确认） ════════ -->
          <div v-else-if="step === 3" class="step-pane">
            <p class="step-desc">新玩家注册方式（单击选中，双击确认；上一步可返回）</p>
            <div class="reg-options">
              <div
                v-for="mode in ['auto', 'block', 'default']"
                :key="mode"
                class="reg-option"
                :class="{ selected: registerMode === mode }"
                @click="registerMode = mode; markTouched('registerMode')"
                @dblclick="registerMode = mode; markTouched('registerMode'); goNext()"
              >
                <div class="reg-title">
                  {{ { auto: '自动注册', block: '阻止注册', default: '默认注册' }[mode] }}
                  <span v-if="regBadge(mode).text" class="rec-badge" :class="regBadge(mode).cls">{{ regBadge(mode).text }}</span>
                </div>
                <div class="reg-desc">{{ regDesc(mode) }}</div>
                <div v-if="mode === 'block' && audience === 'public'" class="reg-sub">
                  教程（链接待补充）
                </div>
              </div>
            </div>
            <div v-if="audience === 'public'" class="step-hint">
              公开服按运营方式选择：管理在线的服务器用自动注册；白名单/QQ 机器人服用阻止注册
            </div>
          </div>

          <!-- ════════ 步骤 4：反作弊（双击行确认进入下一步） ════════ -->
          <div v-else-if="step === 4" class="step-pane">
            <p class="step-desc">物品与弹幕违禁检测开关（开启即用默认配置；双击此行确认并进入下一步）</p>
            <div class="switch-row" @dblclick="goNext">
              <div class="switch-info">
                <div class="switch-title">
                  物品违禁检测
                  <span v-if="antiCheatMust" class="rec-badge purple">必开</span>
                </div>
                <div class="switch-desc">扫描/拦截超进度违禁物品，使用默认检测列表</div>
              </div>
              <label class="switch">
                <input type="checkbox" v-model="acItem" @change="markTouched('acItem')" />
                <span class="slider"></span>
              </label>
            </div>
            <div class="switch-row" @dblclick="goNext">
              <div class="switch-info">
                <div class="switch-title">
                  弹幕违禁检测
                  <span v-if="antiCheatMust" class="rec-badge purple">必开</span>
                </div>
                <div class="switch-desc">拦截超伤害/违禁弹幕，使用默认检测列表</div>
              </div>
              <label class="switch">
                <input type="checkbox" v-model="acProj" @change="markTouched('acProj')" />
                <span class="slider"></span>
              </label>
            </div>
            <div class="feature-tip" :class="{ warn: !antiCheatMust }">{{ antiCheatHint }}</div>
          </div>

          <!-- ════════ 步骤 5：反恶性 bug（双击行确认进入下一步） ════════ -->
          <div v-else-if="step === 5" class="step-pane">
            <p class="step-desc">修复 TShock 恶性 Bug 的功能开关（总开关 + 子功能；双击行确认并进入下一步）</p>
            <div class="switch-row master" @dblclick="goNext">
              <div class="switch-info">
                <div class="switch-title">
                  反恶性 Bug 总开关
                  <span v-if="bugFixMust" class="rec-badge purple">必开</span>
                </div>
                <div class="switch-desc">一键启停以下所有修复功能</div>
              </div>
              <label class="switch">
                <input type="checkbox" v-model="bfEnabled" @change="markTouched('bfEnabled')" />
                <span class="slider"></span>
              </label>
            </div>
            <div class="sub-switches" :class="{ disabled: !bfEnabled }">
              <div class="switch-row" @dblclick="goNext">
                <div class="switch-info">
                  <div class="switch-title">登录修复</div>
                  <div class="switch-desc">修复 UUID 变更导致无法进服的连接层 Bug</div>
                </div>
                <label class="switch">
                  <input type="checkbox" v-model="bfLoginFix" :disabled="!bfEnabled" />
                  <span class="slider"></span>
                </label>
              </div>
              <div class="switch-row" @dblclick="goNext">
                <div class="switch-info">
                  <div class="switch-title">宝箱修复</div>
                  <div class="switch-desc">修复宝箱数据包校验缺失漏洞（防复制/溢出）</div>
                </div>
                <label class="switch">
                  <input type="checkbox" v-model="bfChestFix" :disabled="!bfEnabled" />
                  <span class="slider"></span>
                </label>
              </div>
              <div class="switch-row" @dblclick="goNext">
                <div class="switch-info">
                  <div class="switch-title">召唤物限制</div>
                  <div class="switch-desc">限制异常召唤物数量，防数据洪水</div>
                </div>
                <label class="switch">
                  <input type="checkbox" v-model="bfMinionLimit" :disabled="!bfEnabled" />
                  <span class="slider"></span>
                </label>
              </div>
              <div class="switch-row" @dblclick="goNext">
                <div class="switch-info">
                  <div class="switch-title">粒子防线（闪电）</div>
                  <div class="switch-desc">拦截伪造粒子洪泛攻击（含 /lightning 劈闪工具）</div>
                </div>
                <label class="switch">
                  <input type="checkbox" v-model="bfLightning" :disabled="!bfEnabled" />
                  <span class="slider"></span>
                </label>
              </div>
            </div>
            <div class="feature-tip" :class="{ warn: !bugFixMust }">{{ bugFixHint }}</div>
          </div>

          <!-- ════════ 步骤 6：快速权限（组权限推荐） ════════ -->
          <div v-else-if="step === 6" class="step-pane">
            <p class="step-desc">给服务器玩家组批量添加常用权限（复用组管理预设）</p>
            <div class="perm-explain">{{ permExplain }}</div>
            <div class="perm-groups">
              <div
                v-for="preset in permGroups"
                :key="preset.key"
                class="perm-group-card"
                :class="{ done: groupDone[preset.key] }"
              >
                <div class="perm-group-info">
                  <div class="perm-group-title">{{ preset.label }}</div>
                  <div class="perm-group-desc">{{ preset.desc }}</div>
                </div>
                <button
                  class="btn ghost small"
                  :disabled="groupApplying !== null"
                  @click="applyGroupPreset(preset)"
                >
                  {{ groupDone[preset.key] ? '已添加 ✓' : (groupApplying === preset.key ? '添加中...' : '添加') }}
                </button>
              </div>
            </div>
            <div v-if="audience === 'public'" class="perm-hint">
              💡 可结合「插件设置-权限提升」配置：按 QQ 绑定或游玩时长自动授予 vip 组，无需手动发放
            </div>
            <div v-if="groupMsg" class="perm-msg">{{ groupMsg }}</div>
          </div>
        </div>

        <!-- 底部导航：上一步始终可返回 -->
        <div class="init-foot">
          <button v-if="step > 1" class="btn ghost" :disabled="saving" @click="step--">上一步</button>
          <button v-if="step < 6" class="btn primary" @click="goNext">下一步</button>
          <button v-else class="btn primary" :disabled="saving" @click="submit">
            {{ saving ? '保存中...' : '完成初始化' }}
          </button>
        </div>
      </div>

      <!-- ════════ 跳过确认弹窗（未添加任何组权限时） ════════ -->
      <div v-if="showSkipConfirm" class="confirm-mask">
        <div class="confirm-box">
          <div class="confirm-title">跳过授予基本权限？</div>
          <p class="confirm-text">
            你跳过了授予基本权限，用户可能无法正常使用：召唤 boss、使用晶塔、移动 NPC 等。
          </p>
          <div class="confirm-actions">
            <button class="btn ghost" @click="backToAdd">返回添加</button>
            <button class="btn primary" :disabled="skipCountdown > 0" @click="confirmSkipGrant">
              {{ skipCountdown > 0 ? `确定跳过（${skipCountdown}s）` : '确定跳过' }}
            </button>
          </div>
        </div>
      </div>

      <!-- 跳过提示（顶部滑入 300ms + 3s 滞留 + 滑出） -->
      <Transition name="skip-slide">
        <div v-if="skipToast" class="skip-toast">已跳过，可随时在服务器设置中重新初始化</div>
      </Transition>
    </div>
  </Teleport>
</template>

<style scoped>
.init-mask {
  position: fixed; inset: 0;
  background: rgba(0, 0, 0, 0.55);
  z-index: 9000;
  display: flex; align-items: center; justify-content: center;
}
.init-modal {
  width: 560px; max-width: calc(100vw - 40px);
  max-height: calc(100vh - 80px);
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 16px;
  box-shadow: 0 24px 70px rgba(0, 0, 0, 0.45);
  display: flex; flex-direction: column;
  overflow: hidden;
}

.init-head {
  display: flex; align-items: flex-start; justify-content: space-between;
  padding: 18px 22px 12px;
}
.init-head h3 { margin: 0; font-size: 1.15rem; color: var(--text-primary); }
.init-sub { margin: 4px 0 0; font-size: .8rem; color: var(--text-muted); }
.head-actions { display: flex; align-items: center; }
.icon-btn {
  border: none; background: var(--bg-tertiary); color: var(--text-muted);
  font-size: .82rem; padding: 6px 14px; border-radius: 8px; cursor: pointer;
  transition: all .18s ease;
}
.icon-btn:hover { color: var(--text-primary); background: var(--bg-hover); }

/* 步骤指示器 */
.step-dots {
  display: flex; gap: 4px; padding: 4px 22px 12px;
  border-bottom: 1px solid var(--border-color);
}
.step-dot {
  flex: 1; display: flex; align-items: center; gap: 6px;
  min-width: 0;
}
.dot-num {
  width: 20px; height: 20px; border-radius: 50%; flex-shrink: 0;
  display: flex; align-items: center; justify-content: center;
  font-size: .68rem; font-weight: 700;
  background: var(--bg-tertiary); color: var(--text-muted);
  border: 1px solid var(--border-color);
}
.step-dot.active .dot-num {
  background: var(--accent-primary); border-color: var(--accent-primary); color: #fff;
  box-shadow: 0 0 0 3px rgba(99,102,241,.18);
}
.step-dot.done .dot-num { background: #22c55e; border-color: #22c55e; color: #fff; }
.dot-title {
  font-size: .72rem; color: var(--text-muted); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.step-dot.active .dot-title, .step-dot.done .dot-title { color: var(--text-primary); }

.init-body { flex: 1; overflow-y: auto; padding: 18px 22px; }
.step-pane { display: flex; flex-direction: column; gap: 12px; }
.step-desc { margin: 0; font-size: .84rem; color: var(--text-muted); line-height: 1.5; }
.step-hint {
  font-size: .8rem; color: var(--accent-primary);
  background: rgba(99,102,241,.08); border-radius: 8px; padding: 8px 12px; line-height: 1.5;
}

/* 群体卡片 */
.audience-cards { display: flex; gap: 12px; }
.audience-card {
  flex: 1; text-align: center;
  background: var(--bg-tertiary); border: 1.5px solid var(--border-color);
  border-radius: 12px; padding: 18px 12px; cursor: pointer;
  transition: all .2s ease;
  user-select: none;
}
.audience-card:hover { border-color: var(--accent-primary); transform: translateY(-1px); }
.audience-card.selected {
  border-color: var(--accent-primary);
  background: rgba(99,102,241,.08);
  box-shadow: 0 0 0 2px rgba(99,102,241,.15);
  animation: pop .25s ease;
}
@keyframes pop {
  0% { transform: scale(.97); }
  60% { transform: scale(1.03); }
  100% { transform: scale(1); }
}
.audience-icon { font-size: 1.8rem; margin-bottom: 8px; }
.audience-title { font-size: .95rem; font-weight: 700; color: var(--text-primary); margin-bottom: 4px; }
.audience-desc { font-size: .76rem; color: var(--text-muted); line-height: 1.4; }

/* 开关行 */
.switch-row {
  display: flex; align-items: center; justify-content: space-between; gap: 14px;
  padding: 10px 12px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color); border-radius: 10px;
  transition: border-color .2s ease, box-shadow .2s ease;
}
.switch-row.master {
  background: rgba(99,102,241,.06);
  border-color: var(--accent-primary);
}
/* 公开服 SSC 关闭：红色阴影边框 */
.switch-row.danger {
  border-color: #ef4444;
  box-shadow: 0 0 0 2px rgba(239,68,68,.35), 0 0 16px rgba(239,68,68,.28);
}
.switch-info { flex: 1; min-width: 0; }
.switch-title { font-size: .9rem; font-weight: 600; color: var(--text-primary); }
.switch-desc { font-size: .76rem; color: var(--text-muted); margin-top: 2px; line-height: 1.4; }
.switch-right { display: flex; align-items: center; gap: 10px; flex-shrink: 0; }

/* 流光渐变徽标（公开服 SSC 必开） */
.flow-badge {
  font-size: .68rem; font-weight: 800; color: #fff;
  padding: 3px 10px; border-radius: 999px;
  background: linear-gradient(90deg, #4f46e5, #7c3aed, #eab308, #ec4899, #4f46e5);
  background-size: 300% 100%;
  animation: flow 3s linear infinite;
  white-space: nowrap;
}
@keyframes flow {
  0% { background-position: 0% 50%; }
  100% { background-position: 300% 50%; }
}

.switch { position: relative; display: inline-block; width: 42px; height: 24px; flex-shrink: 0; }
.switch input { opacity: 0; width: 0; height: 0; }
.slider {
  position: absolute; inset: 0; cursor: pointer;
  background: var(--border-color); border-radius: 24px;
  transition: background .2s ease;
}
.slider::before {
  content: ''; position: absolute; width: 18px; height: 18px;
  left: 3px; top: 3px; border-radius: 50%;
  background: #fff; transition: transform .2s ease;
  box-shadow: 0 1px 3px rgba(0,0,0,.3);
}
.switch input:checked + .slider { background: var(--accent-primary); }
.switch input:checked + .slider::before { transform: translateX(18px); }
.switch input:disabled + .slider { opacity: .4; cursor: not-allowed; }

.sub-switches { display: flex; flex-direction: column; gap: 8px; transition: opacity .2s ease; }
.sub-switches.disabled { opacity: .55; }

/* 提示 */
.feature-tip {
  font-size: .8rem; color: #22c55e;
  background: rgba(34,197,94,.1); border-radius: 8px; padding: 8px 12px; line-height: 1.5;
}
.feature-tip.warn {
  color: #f59e0b;
  background: rgba(245,158,11,.1);
}
.danger-tip {
  font-size: .8rem; color: #ef4444; font-weight: 600;
  background: rgba(239,68,68,.1); border-radius: 8px; padding: 8px 12px; line-height: 1.5;
}
.ok-tip {
  font-size: .8rem; color: #22c55e;
  background: rgba(34,197,94,.1); border-radius: 8px; padding: 8px 12px; line-height: 1.5;
}
.neutral-tip {
  font-size: .8rem; color: var(--text-muted);
  background: var(--bg-tertiary); border-radius: 8px; padding: 8px 12px; line-height: 1.5;
}

/* 注册模式 */
.reg-options { display: flex; flex-direction: column; gap: 8px; }
.reg-option {
  display: flex; flex-direction: column; gap: 4px;
  padding: 12px 14px;
  background: var(--bg-tertiary); border: 1.5px solid var(--border-color);
  border-radius: 10px; cursor: pointer; transition: all .18s ease;
  user-select: none;
}
.reg-option:hover { border-color: var(--accent-primary); }
.reg-option.selected {
  border-color: var(--accent-primary); background: rgba(99,102,241,.06);
  animation: pop .25s ease;
}
.reg-title { font-size: .9rem; font-weight: 600; color: var(--text-primary); }
.reg-desc { font-size: .76rem; color: var(--text-muted); margin-top: 2px; }
.reg-sub { font-size: .72rem; color: var(--accent-primary); }
.rec-badge {
  font-size: .66rem; font-weight: 700; color: #fff;
  padding: 1px 7px; border-radius: 6px; vertical-align: 1px; margin-left: 6px;
}
.rec-badge.green { background: #22c55e; }
.rec-badge.purple { background: linear-gradient(135deg, #6366f1, #4f46e5); }
.rec-badge.gray { background: #6b7280; }

/* 步骤 6：组权限 */
.perm-explain {
  font-size: .84rem; color: var(--text-primary);
  background: rgba(99,102,241,.07); border-radius: 10px; padding: 10px 14px; line-height: 1.6;
}
.perm-groups { display: flex; flex-direction: column; gap: 8px; }
.perm-group-card {
  display: flex; align-items: center; justify-content: space-between; gap: 12px;
  padding: 12px 14px;
  background: var(--bg-tertiary); border: 1.5px solid var(--border-color);
  border-radius: 10px; transition: all .2s ease;
}
.perm-group-card.done { border-color: #22c55e; background: rgba(34,197,94,.06); }
.perm-group-info { flex: 1; min-width: 0; }
.perm-group-title { font-size: .9rem; font-weight: 600; color: var(--text-primary); }
.perm-group-desc { font-size: .76rem; color: var(--text-muted); margin-top: 2px; line-height: 1.4; }
.perm-hint {
  font-size: .8rem; color: var(--accent-primary);
  background: rgba(99,102,241,.08); border-radius: 8px; padding: 8px 12px; line-height: 1.5;
}
.perm-msg { font-size: .82rem; color: var(--text-muted); }

/* 底部导航 */
.init-foot {
  display: flex; justify-content: space-between; gap: 10px;
  padding: 14px 22px 18px;
  border-top: 1px solid var(--border-color);
}
.btn {
  border: none; border-radius: 8px; cursor: pointer;
  padding: 8px 18px; font-size: .88rem; font-weight: 600;
  transition: all .2s ease;
}
.btn.primary {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5); color: #fff;
  box-shadow: 0 2px 8px rgba(99,102,241,.25);
}
.btn.primary:hover { opacity: .92; }
.btn.primary:disabled { opacity: .45; cursor: not-allowed; box-shadow: none; }
.btn.ghost { background: transparent; border: 1px solid var(--accent-primary); color: var(--accent-primary); }
.btn.ghost:hover { background: rgba(99,102,241,.1); }
.btn.ghost:disabled { opacity: .45; cursor: not-allowed; }
.btn.small { padding: 6px 14px; font-size: .8rem; }

/* 跳过确认弹窗 */
.confirm-mask {
  position: fixed; inset: 0; z-index: 9600;
  background: rgba(0,0,0,.5);
  display: flex; align-items: center; justify-content: center;
}
.confirm-box {
  width: 420px; max-width: calc(100vw - 40px);
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 14px;
  padding: 22px 24px;
  box-shadow: 0 20px 60px rgba(0,0,0,.45);
}
.confirm-title { font-size: 1rem; font-weight: 700; color: var(--text-primary); margin-bottom: 10px; }
.confirm-text {
  font-size: .86rem; color: var(--text-muted); line-height: 1.6;
  margin: 0 0 18px;
}
.confirm-actions { display: flex; justify-content: flex-end; gap: 10px; }

/* 跳过提示：顶部滑入 300ms + 3s 滞留 + 滑出 */
.skip-toast {
  position: fixed; top: 18px; left: 50%;
  transform: translateX(-50%);
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  color: var(--text-primary);
  font-size: .9rem; font-weight: 600;
  padding: 12px 22px; border-radius: 10px;
  box-shadow: 0 8px 30px rgba(0,0,0,.35);
  z-index: 9500;
  white-space: nowrap;
}
.skip-slide-enter-active { transition: all .3s cubic-bezier(0.34, 1.56, 0.64, 1); }
.skip-slide-leave-active { transition: all .3s ease-in; }
.skip-slide-enter-from { opacity: 0; transform: translate(-50%, -100%); }
.skip-slide-enter-to { opacity: 1; transform: translate(-50%, 0); }
.skip-slide-leave-from { opacity: 1; transform: translate(-50%, 0); }
.skip-slide-leave-to { opacity: 0; transform: translate(-50%, -100%); }
</style>
