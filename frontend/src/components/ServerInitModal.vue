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

// ═══════════════ 投票式状态 ═══════════════
// 每屏一个问题，选项是卡片：单击选中，再点一次已选中卡片 = 确认进入下一屏
const step = ref(1)

const audience = ref(null)        // 'friends' | 'public'
const sscEnabled = ref(null)      // true=开启 | false=关闭
const registerMode = ref(null)    // 'default' | 'auto' | 'block'
const acOn = ref(null)            // true=开反作弊 | false=关
const bfOn = ref(null)            // true=开修复 | false=关

// SSC 加载（服务器当前真实状态，仅作「当前」提示，不预置选中）
const sscRawContent = ref('')
const sscCurrent = ref(null)      // true | false | null(未知)
const sscLoading = ref(false)

// 快速权限
const groupDone = ref({})         // 已应用的预设 key
const groupApplying = ref(null)
const groupMsg = ref('')

// 完成确认
const saving = ref(false)
const confirmArmed = ref(false)   // 完成卡是否已点一次（再点一次提交）
const showSkipConfirm = ref(false)
const skipCountdown = ref(0)
let countdownTimer = null

// 跳过
const skipToast = ref(false)
let skipToastTimer = null

// ═══════════════ 每屏标题 ═══════════════
const stepTitle = computed(() => ({
  1: '服务器面向哪类玩家？',
  2: '是否启用 SSC（服务器角色存储）？',
  3: '新玩家采用哪种注册方式？',
  4: '是否开启反作弊检测？',
  5: '是否开启反恶性 Bug 修复？',
  6: '快速为玩家组添加常用权限'
}[step.value]))

const stepHint = computed(() => step.value === 6
  ? '点一次卡片执行添加，完成后点两次底部完成卡提交'
  : '点一下选中，再点一次所选选项确认'
)

// ═══════════════ 选项卡片（每卡自带介绍） ═══════════════
const audienceCards = [
  { key: 'friends', icon: 'users', title: '熟悉网友服',
    desc: '朋友与长期玩家的小圈子，宽松、便捷优先' },
  { key: 'public', icon: 'globe', title: '公开服务器',
    desc: '面向陌生玩家，安全、公平与秩序优先' }
]

const sscCards = computed(() => {
  const pub = audience.value === 'public'
  return [
    {
      key: true, title: '开启 SSC',
      desc: pub ? '玩家角色存于服务器，强制开荒，无法携带外部物品进入' : '角色存档保存在服务器，换设备不丢档',
      badge: pub ? { text: '必开', cls: 'flow' } : null
    },
    {
      key: false, title: '关闭 SSC',
      desc: pub ? '玩家可携带外部物品进服，存在被炸图毁档的风险' : '角色随客户端本地保存，好友可带外部物品进入',
      badge: null,
      danger: pub
    }
  ]
})

const regCards = computed(() => {
  const pub = audience.value === 'public'
  return [
    {
      key: 'auto', title: '自动注册',
      desc: pub ? '进服即自动注册，适合有管理在线的服务器' : '进服自动注册，免去 tshock 手动注册的门槛',
      badge: pub ? { text: '推荐', cls: 'green' } : { text: '推荐', cls: 'green' }
    },
    {
      key: 'block', title: '阻止注册',
      desc: pub ? '只放行白名单或 QQ 机器人登记的玩家，防广告与恶意注册' : '新玩家需人工放行，不适合朋友服',
      badge: pub ? { text: '推荐', cls: 'green' } : { text: '不推荐', cls: 'gray' }
    },
    {
      key: 'default', title: '默认注册',
      desc: 'tshock 原生行为：玩家须先 /register 注册才能游玩',
      badge: pub ? { text: '不推荐', cls: 'gray' } : null
    }
  ]
})

const acCards = computed(() => {
  const pub = audience.value === 'public'
  return [
    {
      key: true, title: '开启反作弊',
      items: ['物品违禁检测', '弹幕违禁检测'],
      desc: pub ? '按默认规则拦截违规物品与弹幕，防修改器传播' : '按默认规则拦截违规物品与弹幕，朋友服易误伤',
      badge: pub ? { text: '必开', cls: 'flow' } : { text: '不推荐', cls: 'gray' }
    },
    {
      key: false, title: '关闭反作弊',
      items: ['物品违禁检测', '弹幕违禁检测'],
      desc: pub ? '不拦截物品与弹幕，玩家可携带违规内容进入' : '完全放行，游玩最自由',
      badge: null,
      danger: pub
    }
  ]
})

const bfCards = computed(() => {
  const pub = audience.value === 'public'
  const items = [
    '登录修复：UUID 变更无法进服的连接 Bug',
    '宝箱修复：防数据包复制 / 溢出',
    '召唤物限制：防异常数量洪泛',
    '粒子防线：拦截伪造粒子洪泛与 /lightning'
  ]
  return [
    {
      key: true, title: '开启反恶性 Bug',
      items,
      desc: pub ? '四项修复全开，防止恶意利用毁档' : '四项修复全开，朋友服场景少，一般不推荐',
      badge: pub ? { text: '必开', cls: 'flow' } : { text: '不推荐', cls: 'gray' }
    },
    {
      key: false, title: '关闭反恶性 Bug',
      desc: pub ? '不启用任何修复，存在被恶意炸档的风险' : '维持 tshock 原版行为',
      badge: null,
      danger: pub
    }
  ]
})

const permCards = computed(() => {
  if (audience.value === 'public') {
    return [
      { key: 'default', title: '常规权限 → default 组',
        groupName: 'default', perms: [...REASONABLE_PERMS],
        desc: '给所有新玩家（default 组）发放召唤 boss / 晶塔 / 传送杖 / 移动 NPC 等常规权限' },
      { key: 'vip', title: 'tp 权限 → vip 组',
        groupName: 'vip', perms: TP_PERMS,
        desc: '仅 vip 组获得传送权限，可按游玩时长在「权限提升」里自动授予 vip' }
    ]
  }
  return [
    { key: 'default', title: '常规 + tp 权限 → default 组',
      groupName: 'default', perms: [...REASONABLE_PERMS, ...TP_PERMS],
      desc: '给所有玩家（default 组）发放常规玩法与传送权限，朋友开黑更顺' }
  ]
})

// 权限预设（与 PermissionManager.vue 保持一致）
const REASONABLE_PERMS = [
  'tshock.npc.hurttown', 'tshock.npc.spawnpets', 'tshock.npc.startdd2', 'tshock.npc.startinvasion',
  'tshock.npc.summonboss', 'tshock.tp.demonconch', 'tshock.tp.magicconch', 'tshock.tp.pylon',
  'tshock.tp.rod', 'tshock.tp.tppotion', 'tshock.tp.wormhole', 'tshock.world.movenpc',
  'tshock.world.time.usemoondial', 'tshock.world.time.usesundial', 'tshock.world.worldupgrades'
]
const TP_PERMS = ['tshock.tp.self', 'tshock.tp.block', 'tshock.tp.spawn', 'tshock.tp.home']

// ═══════════════ 投票交互 ═══════════════
const makePicker = (pickKey) => (card) => {
  if (pickKey.value === card.key) { next() } else { pickKey.value = card.key }
}
const pickAudience = makePicker(audience)
const pickSsc = makePicker(sscEnabled)
const pickReg = makePicker(registerMode)
const pickAc = makePicker(acOn)
const pickBf = makePicker(bfOn)

const isPicked = (pickKey, key) => pickKey.value === key

const next = () => { if (step.value < 6) step.value++ }
const prev = () => { if (step.value > 1) step.value-- }

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
        sscCurrent.value = !!cfg?.Settings?.Enabled
      } catch { sscCurrent.value = null }
    }
  } catch { /* 静默失败 */ }
  finally { sscLoading.value = false }
}

// ═══════════════ 跳过 ═══════════════
const doSkip = () => {
  showSkipToast()
  emit('close')
}
const showSkipToast = () => {
  skipToast.value = true
  clearTimeout(skipToastTimer)
  skipToastTimer = setTimeout(() => { skipToast.value = false }, 3300)
}

// ═══════════════ 快速权限：应用预设 ═══════════════
const applyGroupPreset = async (card) => {
  if (groupApplying.value) return
  groupApplying.value = card.key
  groupMsg.value = ''
  try {
    const headers = props.serverId ? { 'x-server-id': props.serverId } : {}
    let failed = 0
    for (const perm of card.perms) {
      const res = await apiRequest('/api/tshock/groups/permission/add', {
        method: 'POST', headers,
        body: JSON.stringify({ groupName: card.groupName, permission: perm })
      })
      const data = await res.json().catch(() => ({}))
      if (data?.error) failed++
    }
    if (failed > 0) {
      groupMsg.value = `${card.title}：${failed} 项失败（组 "${card.groupName}" 不存在？）`
    } else {
      groupDone.value[card.key] = true
      groupMsg.value = ''
    }
  } catch (e) {
    groupMsg.value = `${card.title}：${e.message}`
  } finally {
    groupApplying.value = null
    confirmArmed.value = false   // 改动后需重新点两次完成卡
  }
}

const anyGroupDone = computed(() => Object.values(groupDone.value).some(Boolean))

// ═══════════════ 提交（完成卡：点一次武装，再点一次提交） ═══════════════
const onCompletePick = () => {
  if (saving.value) return
  if (!confirmArmed.value) { confirmArmed.value = true; return }
  submit()
}

const submit = async () => {
  if (!anyGroupDone.value) {
    // 未添加任何组权限 → 确认弹窗（3 秒冷却）
    showSkipConfirm.value = true
    confirmArmed.value = false
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
    // SSC：用户确认后回写 Settings.Enabled（无原始内容则跳过）
    let sscContent = ''
    if (sscRawContent.value && sscEnabled.value !== null) {
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
      anticheat: { itemEnabled: acOn.value, projEnabled: acOn.value },
      bugfix: {
        enabled: bfOn.value,
        loginFix: true, chestFix: true, minionLimit: true, lightning: true
      }
    }

    const res = await post('/api/setup/plugin-init-v2', body)
    const data = await res.json().catch(() => ({}))
    if (res.ok && data?.success) {
      emit('completed', data)
    } else {
      alert('初始化失败：' + (data?.error || '未知错误'))
    }
  } catch (e) {
    alert('初始化失败：' + e.message)
  } finally { saving.value = false }
}

const backToAdd = () => {
  clearInterval(countdownTimer)
  showSkipConfirm.value = false
  confirmArmed.value = false
}
const confirmSkipGrant = () => {
  clearInterval(countdownTimer)
  showSkipConfirm.value = false
  doSubmit()
}

// ═══════════════ 生命周期 ═══════════════
watch(() => props.show, (v) => {
  if (v) {
    step.value = 1
    audience.value = null
    sscEnabled.value = null
    registerMode.value = null
    acOn.value = null
    bfOn.value = null
    sscRawContent.value = ''
    sscCurrent.value = null
    groupDone.value = {}
    groupMsg.value = ''
    confirmArmed.value = false
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
    <!-- 厚高斯模糊遮罩；点击外部不关闭 -->
    <div v-if="show" class="init-mask">
      <div class="init-modal">
        <div class="init-head">
          <div>
            <h3>初始化插件设置</h3>
            <p class="init-sub">{{ serverName ? `服务器：${serverName}` : '配置新服务器的插件功能' }}</p>
          </div>
          <button class="icon-btn" title="跳过初始化" @click="doSkip">跳过</button>
        </div>

        <div class="init-body">
          <div class="screen-title">{{ stepTitle }}</div>
          <div class="vote-hint">{{ stepHint }}</div>

          <!-- ════ 屏 1：玩家群体 ════ -->
          <div v-if="step === 1" class="vote-list">
            <div
              v-for="card in audienceCards" :key="card.key"
              class="vote-card"
              :class="{ picked: isPicked(audience, card.key) }"
              @click="pickAudience(card)"
            >
              <svg class="card-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
                <template v-if="card.icon === 'users'">
                  <circle cx="9" cy="8" r="3.2"/><path d="M2.5 19c.6-3.1 3.2-5 6.5-5s5.9 1.9 6.5 5"/><circle cx="17" cy="9" r="2.4"/><path d="M16 14.4c2.4.2 4.3 1.6 5 4.1"/>
                </template>
                <template v-else>
                  <circle cx="12" cy="12" r="8.5"/><path d="M3.6 12h16.8M12 3.5c2.7 2.2 4 5.3 4 8.5s-1.3 6.3-4 8.5c-2.7-2.2-4-5.3-4-8.5s1.3-6.3 4-8.5z"/>
                </template>
              </svg>
              <div class="card-title">{{ card.title }}</div>
              <div class="card-desc">{{ card.desc }}</div>
              <div class="pick-mark" v-if="isPicked(audience, card.key)">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"><path d="M4.5 12.5l5 5L19.5 6.5"/></svg>
              </div>
            </div>
          </div>

          <!-- ════ 屏 2：SSC ════ -->
          <div v-else-if="step === 2" class="vote-list">
            <div v-if="sscLoading" class="screen-note">正在读取当前 SSC 状态...</div>
            <div
              v-for="card in sscCards" :key="String(card.key)"
              class="vote-card"
              :class="{ picked: isPicked(sscEnabled, card.key), danger: card.danger }"
              @click="pickSsc(card)"
            >
              <span v-if="card.badge" class="mini-badge" :class="card.badge.cls">{{ card.badge.text }}</span>
              <div class="card-title">
                {{ card.title }}
                <span v-if="sscCurrent !== null && sscCurrent === card.key" class="mini-tag">当前状态</span>
              </div>
              <div class="card-desc">{{ card.desc }}</div>
              <div class="pick-mark" v-if="isPicked(sscEnabled, card.key)">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"><path d="M4.5 12.5l5 5L19.5 6.5"/></svg>
              </div>
            </div>
          </div>

          <!-- ════ 屏 3：注册模式 ════ -->
          <div v-else-if="step === 3" class="vote-list">
            <div
              v-for="card in regCards" :key="card.key"
              class="vote-card"
              :class="{ picked: isPicked(registerMode, card.key) }"
              @click="pickReg(card)"
            >
              <span v-if="card.badge" class="mini-badge" :class="card.badge.cls">{{ card.badge.text }}</span>
              <div class="card-title">{{ card.title }}</div>
              <div class="card-desc">{{ card.desc }}</div>
              <div class="pick-mark" v-if="isPicked(registerMode, card.key)">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"><path d="M4.5 12.5l5 5L19.5 6.5"/></svg>
              </div>
            </div>
          </div>

          <!-- ════ 屏 4：反作弊 ════ -->
          <div v-else-if="step === 4" class="vote-list">
            <div
              v-for="card in acCards" :key="String(card.key)"
              class="vote-card"
              :class="{ picked: isPicked(acOn, card.key), danger: card.danger }"
              @click="pickAc(card)"
            >
              <span v-if="card.badge" class="mini-badge" :class="card.badge.cls">{{ card.badge.text }}</span>
              <div class="card-title">{{ card.title }}</div>
              <ul v-if="card.items" class="card-subs">
                <li v-for="s in card.items" :key="s">{{ s }}</li>
              </ul>
              <div class="card-desc">{{ card.desc }}</div>
              <div class="pick-mark" v-if="isPicked(acOn, card.key)">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"><path d="M4.5 12.5l5 5L19.5 6.5"/></svg>
              </div>
            </div>
          </div>

          <!-- ════ 屏 5：反恶性 Bug ════ -->
          <div v-else-if="step === 5" class="vote-list">
            <div
              v-for="card in bfCards" :key="String(card.key)"
              class="vote-card"
              :class="{ picked: isPicked(bfOn, card.key), danger: card.danger }"
              @click="pickBf(card)"
            >
              <span v-if="card.badge" class="mini-badge" :class="card.badge.cls">{{ card.badge.text }}</span>
              <div class="card-title">{{ card.title }}</div>
              <ul v-if="card.items" class="card-subs">
                <li v-for="s in card.items" :key="s">{{ s }}</li>
              </ul>
              <div class="card-desc">{{ card.desc }}</div>
              <div class="pick-mark" v-if="isPicked(bfOn, card.key)">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"><path d="M4.5 12.5l5 5L19.5 6.5"/></svg>
              </div>
            </div>
          </div>

          <!-- ════ 屏 6：快速权限 ════ -->
          <div v-else-if="step === 6" class="vote-list">
            <div
              v-for="card in permCards" :key="card.key"
              class="vote-card perm-card"
              :class="{ done: groupDone[card.key] }"
              @click="applyGroupPreset(card)"
            >
              <div class="card-title">
                {{ card.title }}
                <span v-if="groupDone[card.key]" class="mini-tag ok">已添加</span>
              </div>
              <div class="card-desc">{{ card.desc }}</div>
              <div v-if="groupApplying === card.key" class="card-state">添加中...</div>
            </div>
            <div v-if="groupMsg" class="perm-msg">{{ groupMsg }}</div>

            <!-- 完成卡：点一次武装，再点一次提交 -->
            <div
              class="vote-card done-card"
              :class="{ picked: confirmArmed }"
              @click="onCompletePick"
            >
              <svg class="card-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="12" cy="12" r="9"/><path d="M8 12.5l3 3 5-6"/>
              </svg>
              <div class="card-title">{{ saving ? '提交中...' : (confirmArmed ? '再次点击确认完成' : '确认完成初始化') }}</div>
              <div class="card-desc">{{ anyGroupDone ? '将以上选择应用到服务器' : '尚未添加任何玩家组权限' }}</div>
            </div>
          </div>
        </div>

        <!-- 底部：仅上一步 -->
        <div class="init-foot">
          <button v-if="step > 1" class="btn ghost" :disabled="saving" @click="prev">上一步</button>
        </div>
      </div>

      <!-- 跳过权限确认（未添加任何组权限时点完成触发） -->
      <div v-if="showSkipConfirm" class="confirm-mask">
        <div class="confirm-box">
          <div class="confirm-title">跳过授予基本权限？</div>
          <p class="confirm-text">
            你跳过了授予基本权限，玩家可能无法正常使用召唤 boss、晶塔、移动 NPC 等功能。
          </p>
          <div class="confirm-actions">
            <button class="btn ghost" @click="backToAdd">返回添加</button>
            <button class="btn primary" :disabled="skipCountdown > 0" @click="confirmSkipGrant">
              {{ skipCountdown > 0 ? `确定跳过（${skipCountdown}s）` : '确定跳过' }}
            </button>
          </div>
        </div>
      </div>

      <!-- 跳过提示 -->
      <Transition name="skip-slide">
        <div v-if="skipToast" class="skip-toast">已跳过，可随时在服务器设置中重新初始化</div>
      </Transition>
    </div>
  </Teleport>
</template>

<style scoped>
.init-mask {
  position: fixed; inset: 0;
  background: rgba(10, 12, 22, 0.66);
  backdrop-filter: blur(22px) saturate(1.15);
  -webkit-backdrop-filter: blur(22px) saturate(1.15);
  z-index: 9000;
  display: flex; align-items: center; justify-content: center;
  padding: 24px;
}
.init-modal {
  width: 560px; max-width: calc(100vw - 48px);
  max-height: calc(100vh - 96px);
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 18px;
  box-shadow: 0 30px 90px rgba(0, 0, 0, 0.6);
  display: flex; flex-direction: column;
  overflow: hidden;
}

.init-head {
  display: flex; align-items: flex-start; justify-content: space-between;
  padding: 20px 24px 12px;
  border-bottom: 1px solid var(--border-color);
}
.init-head h3 { margin: 0; font-size: 1.15rem; color: var(--text-primary); }
.init-sub { margin: 5px 0 0; font-size: .78rem; color: var(--text-muted); }
.icon-btn {
  border: 1px solid var(--border-color); background: var(--bg-tertiary); color: var(--text-muted);
  font-size: .82rem; padding: 6px 16px; border-radius: 8px; cursor: pointer;
  transition: all .18s ease; flex-shrink: 0;
}
.icon-btn:hover { color: var(--text-primary); border-color: var(--border-light); }

.init-body { flex: 1; overflow-y: auto; padding: 20px 24px 24px; }
.screen-title { font-size: 1.08rem; font-weight: 700; color: var(--text-primary); }
.vote-hint { font-size: .76rem; color: var(--text-muted); margin: 6px 0 18px; }
.screen-note { font-size: .8rem; color: var(--text-muted); margin-bottom: 8px; }

.vote-list { display: flex; flex-direction: column; gap: 12px; }

/* 投票卡片 */
.vote-card {
  position: relative;
  display: flex; flex-direction: column; gap: 5px;
  padding: 16px 18px;
  background: var(--bg-tertiary);
  border: 1.5px solid var(--border-color);
  border-radius: 14px;
  cursor: pointer;
  user-select: none;
  transition: transform .16s ease, border-color .16s ease, box-shadow .16s ease, background .16s ease;
  text-align: left;
}
.vote-card:hover {
  transform: translateY(-1px);
  border-color: var(--border-light);
}
.vote-card.picked {
  border-color: var(--accent-primary);
  background: color-mix(in srgb, var(--accent-primary) 10%, var(--bg-tertiary));
  box-shadow: 0 0 0 1.5px var(--accent-primary), 0 8px 26px rgba(99, 102, 241, .16);
}
.vote-card.danger:not(.picked) {
  border-color: rgba(239, 68, 68, .65);
  background: color-mix(in srgb, rgba(239, 68, 68, .07), var(--bg-tertiary));
}
.vote-card.danger.picked {
  border-color: #ef4444;
  box-shadow: 0 0 0 1.5px #ef4444, 0 8px 26px rgba(239, 68, 68, .2);
  background: color-mix(in srgb, rgba(239, 68, 68, .14), var(--bg-tertiary));
}
.card-icon { width: 26px; height: 26px; color: var(--accent-primary); margin-bottom: 4px; }
.card-title {
  font-size: .98rem; font-weight: 700; color: var(--text-primary);
  display: flex; align-items: center; gap: 8px;
  padding-right: 30px;
}
.card-desc { font-size: .82rem; color: var(--text-muted); line-height: 1.6; }
.card-subs { margin: 2px 0 0; padding: 0; list-style: none; display: flex; flex-direction: column; gap: 3px; }
.card-subs li {
  font-size: .78rem; color: var(--text-secondary, var(--text-muted));
  padding-left: 16px; position: relative; line-height: 1.5;
}
.card-subs li::before {
  content: ''; position: absolute; left: 2px; top: 8px;
  width: 5px; height: 5px; border-radius: 50%;
  background: var(--accent-primary); opacity: .75;
}
.card-state { font-size: .76rem; color: var(--accent-primary); }

/* 选中标记（右上角对勾圆） */
.pick-mark {
  position: absolute; top: 14px; right: 14px;
  width: 22px; height: 22px; border-radius: 50%;
  background: var(--accent-primary); color: #fff;
  display: flex; align-items: center; justify-content: center;
  flex-shrink: 0;
}
.pick-mark svg { width: 13px; height: 13px; }

/* 徽标 */
.mini-badge {
  align-self: flex-start;
  font-size: .64rem; font-weight: 800; letter-spacing: .04em;
  padding: 2px 9px; border-radius: 999px; line-height: 1.6;
}
.mini-badge.green { background: rgba(34, 197, 94, .15); color: #4ade80; border: 1px solid rgba(34, 197, 94, .35); }
.mini-badge.gray { background: rgba(148, 163, 184, .15); color: #94a3b8; border: 1px solid rgba(148, 163, 184, .3); }
.mini-badge.flow {
  color: #fff;
  background: linear-gradient(90deg, #4f46e5, #7c3aed, #d946ef, #f472b6, #f59e0b, #4f46e5);
  background-size: 300% 100%;
  animation: flow 3.2s linear infinite;
  border: none;
}
@keyframes flow {
  0% { background-position: 0% 50%; }
  100% { background-position: 300% 50%; }
}
.mini-tag {
  font-size: .62rem; font-weight: 700;
  padding: 1px 8px; border-radius: 999px;
  background: var(--border-color); color: var(--text-muted);
  white-space: nowrap;
}
.mini-tag.ok { background: rgba(34, 197, 94, .15); color: #4ade80; }

/* 快速权限 */
.perm-card.done { border-color: rgba(34, 197, 94, .55); }
.perm-msg { font-size: .8rem; color: var(--text-muted); }

/* 完成卡 */
.done-card {
  margin-top: 6px;
  border-style: dashed;
  border-color: var(--border-light);
}
.done-card.picked { border-style: solid; }
.done-card .card-title { padding-right: 0; }

/* 底部 */
.init-foot {
  display: flex; justify-content: flex-start;
  padding: 12px 24px 18px;
  border-top: 1px solid var(--border-color);
}
.btn {
  border: none; border-radius: 9px; cursor: pointer;
  padding: 8px 18px; font-size: .86rem; font-weight: 600;
  transition: all .18s ease;
}
.btn.ghost { background: transparent; border: 1px solid var(--border-color); color: var(--text-muted); }
.btn.ghost:hover { border-color: var(--accent-primary); color: var(--accent-primary); background: rgba(99, 102, 241, .05); }
.btn.ghost:disabled, .btn.primary:disabled { opacity: .45; cursor: not-allowed; }
.btn.primary {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5); color: #fff;
  box-shadow: 0 2px 8px rgba(99, 102, 241, .3);
}

/* 跳过权限确认 */
.confirm-mask {
  position: fixed; inset: 0; z-index: 9600;
  background: rgba(8, 10, 18, .6);
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
  display: flex; align-items: center; justify-content: center;
  padding: 24px;
}
.confirm-box {
  width: 430px; max-width: 100%;
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: 16px;
  padding: 24px;
  box-shadow: 0 24px 70px rgba(0, 0, 0, .5);
}
.confirm-title { font-size: 1rem; font-weight: 700; color: var(--text-primary); margin-bottom: 10px; }
.confirm-text {
  font-size: .86rem; color: var(--text-muted); line-height: 1.7;
  margin: 0 0 20px;
}
.confirm-actions { display: flex; justify-content: flex-end; gap: 10px; }

/* 跳过 toast */
.skip-toast {
  position: fixed; top: 20px; left: 50%;
  transform: translateX(-50%);
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  color: var(--text-primary);
  font-size: .9rem; font-weight: 600;
  padding: 12px 22px; border-radius: 10px;
  box-shadow: 0 10px 34px rgba(0, 0, 0, .4);
  z-index: 9700;
  white-space: nowrap;
}
.skip-slide-enter-active { transition: all .3s cubic-bezier(0.34, 1.56, 0.64, 1); }
.skip-slide-leave-active { transition: all .3s ease-in; }
.skip-slide-enter-from { opacity: 0; transform: translate(-50%, -110%); }
.skip-slide-enter-to { opacity: 1; transform: translate(-50%, 0); }
.skip-slide-leave-from { opacity: 1; transform: translate(-50%, 0); }
.skip-slide-leave-to { opacity: 0; transform: translate(-50%, -110%); }
</style>
