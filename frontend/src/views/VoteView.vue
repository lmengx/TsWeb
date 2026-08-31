<script setup>
import { ref, computed, onMounted, onBeforeUnmount, nextTick } from 'vue'
import forge from 'node-forge'
import { playerGet, playerPost } from '../utils/playerApi.js'

// ═══════════════════════════════════════════════════════════
// 玩家投票页（单页，只展示「活跃中」= 未归档轮次）
//  - 进行中 + 已结束未归档；后端已排序（进行中优先，按结束时间升序）
//  - 仅一个轮次：直接显示详情，无任何切换/往期
//  - 多个轮次：桌面=左侧悬浮菜单（右侧贴紧主体左侧自动定位），窄屏=右下角展开列表
//  - 未登录：看结果 + 「登录以参加投票」按钮；登录：模态框（背景高斯模糊）
//  - 登录后：显示当前轮次的 可投票数 / 剩余票数 / 自身权重
// ═══════════════════════════════════════════════════════════

// ── 轮次数据 ──
const rounds = ref([])       // 活跃中轮次（未归档）
const currentId = ref(null)  // 当前选中的轮次 id
const me = ref(null)         // 玩家登录信息
const loading = ref(true)
const voteError = ref('')
const proposalText = ref('')
const proposalAnon = ref(false)

// ── 新提案上架动画状态：刚提交成功的提案选项 id（列表渲染时播进入动画，超时自动清除）──
const justProposedId = ref(null)
let justProposedTimer = null

// ── 登录模态框 ──
const showLogin = ref(false)
const loginLoading = ref(false)
const loginError = ref('')
const loginForm = ref({ account: '', password: '' })

// ── 悬浮菜单 ──
const isDesktop = ref(window.innerWidth >= 1024)
const bodyEl = ref(null)
const floatingStyle = ref({})

const activeRounds = computed(() => rounds.value)
const currentRound = computed(() => activeRounds.value.find(r => r.id === currentId.value) || activeRounds.value[0] || null)
const showFloating = computed(() => activeRounds.value.length > 1)
const isLoggedIn = computed(() => !!me.value)

// ═══════════════ 工具 ═══════════════

const fmtTime = (t) => (t ? new Date(t).toLocaleString('zh-CN', { hour12: false }) : '长期有效')

const pct = (option, options) => {
  const total = options.reduce((s, o) => s + (Number(o.score) || 0), 0)
  if (total <= 0) return '0%'
  return ((Number(option.score || 0) / total) * 100).toFixed(1) + '%'
}

/** 加权规则的人类可读描述（基于当前轮次的 my.weightRules，登录后后端已下放） */
const ruleText = (r) => {
  const rules = (r.my?.weightRules || []).filter(Boolean)
  return rules.map(w => `游玩时长 ${w.op} ${w.threshold}h 加 ${w.weight} 分`).join('，')
}

const canVote = (r, o) => {
  if (r.status !== 'open' || !r.my) return false
  if (r.my.votedOptions.includes(o.id)) return false
  return r.my.votesLeft > 0
}

/** 是否显示提案栏位（登录 + 进行中 + 开放提案 + 有剩余额度） */
const canPropose = computed(() =>
  isLoggedIn.value &&
  currentRound.value?.status === 'open' &&
  currentRound.value?.allowProposals &&
  currentRound.value?.my &&
  currentRound.value.my.proposalsLeft > 0
)

// ═══ 提案栏位出现/消失动画（高度 + 间距 + 透明度平滑过渡，避免挤占生硬位移）═══
const PP_H = 50 // 与 .propose-row 的 height 一致
const PP_MT = 14 // 与 .propose-row 的 margin-top 一致
const ppBeforeEnter = (el) => {
  el.style.height = '0px'
  el.style.marginTop = '0px'
  el.style.opacity = '0'
  el.style.overflow = 'hidden'
}
const ppEnter = (el, done) => {
  el.style.transition = 'height .2s ease, opacity .2s ease, margin-top .2s ease'
  requestAnimationFrame(() => {
    el.style.height = PP_H + 'px'
    el.style.marginTop = PP_MT + 'px'
    el.style.opacity = '1'
  })
  setTimeout(() => {
    // 动画结束后清除 inline，恢复 CSS 定义值
    el.style.transition = ''
    el.style.height = ''
    el.style.marginTop = ''
    el.style.overflow = ''
    done()
  }, 220)
}
const ppLeave = (el, done) => {
  el.style.transition = 'height .2s ease, opacity .2s ease, margin-top .2s ease'
  el.style.height = '0px'
  el.style.marginTop = '0px'
  el.style.opacity = '0'
  el.style.overflow = 'hidden'
  setTimeout(done, 230)
}

// ═══ 选项列表（TransitionGroup）：新提案插入时先占位缓缓下移，再淡入出现 ═══
//  - before-enter：高度 0 / margin 0 / 透明（不占位，下方内容原位不动）
//  - enter：height 0 → 实际高度 + margin 0 → 12px（下方内容被平滑推下，无生硬跳变）；
//    opacity 由 CSS transition 延迟 0.15s 再淡入 → 「先下移，后出现」
//  - after-enter：清理 inline，恢复 CSS（动画已结束，无跳变）
const optBeforeEnter = (el) => {
  el.style.height = '0px'
  el.style.marginTop = '0px'
  el.style.opacity = '0'
  el.style.overflow = 'hidden'
}
const optEnter = (el, done) => {
  requestAnimationFrame(() => {
    el.style.height = el.scrollHeight + 'px'
    el.style.marginTop = '12px'
    el.style.opacity = '1'
    // 等 height .35s + opacity(延迟 .15s + .3s) 全部结束再 done
    setTimeout(done, 480)
  })
}
const optAfterEnter = (el) => {
  el.style.height = ''
  el.style.marginTop = ''
  el.style.opacity = ''
  el.style.overflow = ''
}

// ═══════════════ 数据加载 ═══════════════

const loadMe = async () => {
  if (!localStorage.getItem('user_player')) { me.value = null; return }
  try {
    const res = await playerGet('/api/auth/player/me')
    const data = await res.json()
    me.value = (data && data.username) ? data : null
  } catch (e) {
    me.value = null
  }
}

const loadRounds = async (opts = {}) => {
  const silent = !!opts?.silent
  if (!silent) loading.value = true
  try {
    const pubRes = await fetch('/api/vote/rounds')
    const pub = await pubRes.json()
    let list = pub.rounds || []
    // 仅登录后拉个人状态（避免未登录触发 playerApi 的 401 跳转）
    // ⚠️ 关键：先拉完 mine 再一次性赋值 rounds（含 my），避免中间帧——
    //    若先赋无 my 的 public 数据再 map 合并，登录卡内容会“消失又重现”的生硬刷新
    if (localStorage.getItem('user_player')) {
      const mineRes = await playerGet('/api/vote/rounds/mine')
      if (mineRes) {
        const d = await mineRes.json()
        const mineMap = new Map((d.rounds || []).map(r => [r.id, r.my]))
        list = list.map(r => ({ ...r, my: mineMap.get(r.id) }))
      }
    }
    rounds.value = list
    // 保持当前选中：默认第一个（后端排序后的最优先轮次）
    if (rounds.value.length) {
      if (!currentId.value || !rounds.value.some(r => r.id === currentId.value)) {
        currentId.value = rounds.value[0].id
      }
    } else {
      currentId.value = null
    }
  } catch (e) {
    console.error('加载投票轮次失败:', e)
  } finally {
    if (!silent) loading.value = false
  }
}

// ═══════════════ 结束倒计时（精确到秒，每秒 tick，动画） ═══════════════

const now = ref(Date.now())
let timer = null
let expiredNotified = false

const countdown = computed(() => {
  const r = currentRound.value
  if (!r || r.status !== 'open' || !r.endAt) return null
  const end = new Date(r.endAt).getTime()
  const diff = end - now.value
  if (diff <= 0) return { expired: true }
  const totalSec = Math.floor(diff / 1000)
  const pad = (n) => String(n).padStart(2, '0')
  const d = Math.floor(totalSec / 86400)
  const h = pad(Math.floor((totalSec % 86400) / 3600))
  const m = pad(Math.floor((totalSec % 3600) / 60))
  const s = pad(totalSec % 60)
  return { expired: false, d, h, m, s }
})

const tick = () => {
  now.value = Date.now()
  const cd = countdown.value
  if (cd?.expired) {
    // 倒计时归零 → 重新拉取状态（服务端将判定为已结束），只触发一次
    if (!expiredNotified) {
      expiredNotified = true
      loadRounds({ silent: true })
    }
  } else {
    expiredNotified = false
  }
}

const selectRound = (id) => {
  currentId.value = id
}

// ═══════════════ 两段式投票：第一次点击选中（变蓝），再次点击同一选项确认 ═══════════════

const selectedId = ref(null)

const toggleSelect = (roundId, optionId) => {
  if (selectedId.value === optionId) {
    // 再次点击同一选项 → 确认投票
    cast(roundId, optionId)
    selectedId.value = null
    return
  }
  selectedId.value = optionId
}

// ═══════════════ 投票 / 提案 ═══════════════

/** 本地乐观更新：投票成功后立即反映已投标记/票数/加权分，避免整页刷新闪烁 */
const applyLocalVote = (optionId, weight) => {
  const r = currentRound.value
  if (!r) return
  if (!r.my) {
    r.my = {
      votedOptions: [],
      votesLeft: r.maxVotesPerUser ?? 1,
      myProposals: 0,
      proposalsLeft: r.maxProposalsPerUser ?? 1,
      weight: Number(r.baseWeight) || 1
    }
  }
  if (!r.my.votedOptions.includes(optionId)) r.my.votedOptions.push(optionId)
  r.my.votesLeft = Math.max(0, (r.my.votesLeft ?? 1) - 1)
  const o = r.options.find(x => x.id === optionId)
  if (o) {
    o.votes = (o.votes || 0) + 1
    o.score = Math.round(((o.score || 0) + (Number(weight) || 1)) * 100) / 100
  }
}

const cast = async (roundId, optionId) => {
  voteError.value = ''
  try {
    const res = await playerPost(`/api/vote/rounds/${roundId}/cast`, { optionId })
    const data = await res.json()
    if (!data.success) { voteError.value = data.error || '投票失败'; return }
    // 立即本地反映，再静默校准（不显示 loading）
    applyLocalVote(optionId, data.vote?.weight)
    selectedId.value = null
    await loadRounds({ silent: true })
  } catch (e) {
    voteError.value = '投票失败: ' + e.message
  }
}

/**
 * 本地乐观更新：提案成功后立即把新选项插入列表（秒出 + 播进入动画），避免等全量刷新
 * @param {string} roundId   目标轮次 id
 * @param {object} option    后端返回的选项对象 { id, text, type, proposer, anonymous }
 * @param {boolean} existing 后端同文本去重是否复用了已有选项（为 true 时不重复插入）
 */
const applyLocalProposal = (roundId, option, existing) => {
  const r = rounds.value.find(x => x.id === roundId) || currentRound.value
  if (!r) return
  // ⚠️ existing: 同轮同文本已存在（后端复用），本地不再插入，避免出现两个同文本选项与后端不一致
  // （justProposedId 已指向该已有选项 → 高亮脉冲照常播放，仅无进入动画）
  if (!existing) {
    r.options.push({
      ...option,
      votes: 0,
      score: 0
    })
  }
  // 同步提案额度：myProposals +1 / proposalsLeft -1（额度用尽后提案栏带离开动画收起）
  if (r.my) {
    r.my.myProposals = (r.my.myProposals || 0) + 1
    r.my.proposalsLeft = Math.max(0, (r.my.proposalsLeft ?? 1) - 1)
  }
}

const submitProposal = async (roundId) => {
  voteError.value = ''
  if (!proposalText.value.trim()) { voteError.value = '请输入提案内容'; return }
  try {
    const res = await playerPost(`/api/vote/rounds/${roundId}/propose`, {
      text: proposalText.value.trim(),
      anonymous: proposalAnon.value
    })
    const data = await res.json()
    if (!data.success) { voteError.value = data.error || '提案失败'; return }
    proposalText.value = ''
    proposalAnon.value = false
    // 记录刚上架的提案选项 id → 渲染时新选项框播放进入动画（淡入上浮 + 高亮脉冲 + 文字从左到右扫出）
    if (data.option?.id) {
      justProposedId.value = data.option.id
      if (justProposedTimer) clearTimeout(justProposedTimer)
      justProposedTimer = setTimeout(() => { justProposedId.value = null }, 2800)
    }
    // 立即本地插入（新选项秒出 + 播进入动画），再静默校准（不显示 loading）——与投票 applyLocalVote 同模式
    applyLocalProposal(roundId, data.option, data.existing)
    await loadRounds({ silent: true })
  } catch (e) {
    voteError.value = '提案失败: ' + e.message
  }
}

// ═══════════════ 登录（模态框，RSA-OAEP 挑战） ═══════════════

const login = async () => {
  if (!loginForm.value.account.trim() || !loginForm.value.password) {
    loginError.value = '请输入 QQ 号 / 角色名和密码'
    return
  }
  loginLoading.value = true
  loginError.value = ''
  try {
    const serverKeyResponse = await fetch('/api/auth/get-server-key')
    const serverKeyData = await serverKeyResponse.json()

    const clientKeys = forge.pki.rsa.generateKeyPair(2048)
    const clientPublicKeyPem = forge.pki.publicKeyToPem(clientKeys.publicKey)

    const serverPublicKey = forge.pki.publicKeyFromPem(serverKeyData.publicKey)
    const encryptedPassword = forge.util.encode64(serverPublicKey.encrypt(loginForm.value.password, 'RSA-OAEP'))

    const loginResponse = await fetch('/api/auth/player-login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        account: loginForm.value.account.trim(),
        encryptedPassword,
        clientPublicKeyPem,
        keyId: serverKeyData.keyId
      })
    })

    const loginResult = await loginResponse.json()
    if (!loginResult.success) {
      loginError.value = loginResult.error || '登录失败，请检查 QQ 号/角色名和密码'
      return
    }

    let token = loginResult.token
    if (loginResult.encryptedToken) {
      token = clientKeys.privateKey.decrypt(forge.util.decode64(loginResult.encryptedToken), 'RSA-OAEP')
    }

    // 玩家登录态只写 user_player，绝不触碰管理端 'user'
    localStorage.setItem('user_player', JSON.stringify({
      username: loginResult.player?.username || loginForm.value.account.trim(),
      qq: loginResult.player?.qq || '',
      usergroup: loginResult.userGroup || 'player',
      token
    }))

    me.value = loginResult.player || { username: loginForm.value.account.trim() }
    loginForm.value = { account: '', password: '' }
    loginError.value = ''
    showLogin.value = false
    // 静默校验（不触发整页 loading 闪烁），登录卡数据由本地乐观状态平滑呈现
    await loadRounds({ silent: true })
  } catch (e) {
    console.error('Player login error:', e)
    loginError.value = '登录失败，请重试'
  } finally {
    loginLoading.value = false
  }
}

// 手动登出（测试用）：退出玩家登录态并刷新数据。
// 不在页面显示退出按钮（防刷票），暴露到 window 供浏览器 console 调用：playerLogout()
const logout = async () => {
  localStorage.removeItem('user_player')
  me.value = null
  proposalText.value = ''
  proposalAnon.value = false
  await loadRounds({ silent: true })
}
window.playerLogout = logout

// ═══════════════ 悬浮菜单定位 ═══════════════

const updateFloatingPos = () => {
  if (!bodyEl.value) return
  const rect = bodyEl.value.getBoundingClientRect()
  // 菜单右缘贴紧主体左侧（留 14px 间距）
  floatingStyle.value = {
    right: (window.innerWidth - rect.left + 14) + 'px',
    top: rect.top + 'px'
  }
}

const onResize = async () => {
  isDesktop.value = window.innerWidth >= 1024
  await nextTick()
  updateFloatingPos()
}

onMounted(async () => {
  window.addEventListener('resize', onResize)
  await Promise.all([loadRounds(), loadMe()])
  await nextTick()
  updateFloatingPos()
  timer = setInterval(tick, 1000)
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', onResize)
  if (timer) clearInterval(timer)
  if (justProposedTimer) clearTimeout(justProposedTimer)
})
</script>

<template>
  <div class="vote-page">
    <!-- ═══ 桌面：左侧悬浮投票列表（仅多个轮次时）═══ -->
    <div v-if="isDesktop && showFloating" class="float-menu" :style="floatingStyle">
      <div class="fm-title">投票列表</div>
      <button
        v-for="r in activeRounds"
        :key="r.id"
        class="fm-item"
        :class="{ cur: r.id === currentRound?.id, closed: r.status === 'closed' }"
        @click="selectRound(r.id)"
      >
        <span class="fm-name">{{ r.title }}</span>
        <span class="fm-badge" :class="r.status">{{ r.status === 'open' ? '进行中' : '已结束' }}</span>
      </button>
    </div>

    <!-- ═══ 窄屏：右下角展开悬浮列表（直接展示，不折叠）═══ -->
    <div v-if="!isDesktop && showFloating" class="fab-menu">
      <div class="fm-title">投票列表</div>
      <button
        v-for="r in activeRounds"
        :key="r.id"
        class="fm-item"
        :class="{ cur: r.id === currentRound?.id, closed: r.status === 'closed' }"
        @click="selectRound(r.id)"
      >
          <span class="fm-name">{{ r.title }}</span>
          <span class="fm-badge" :class="r.status">{{ r.status === 'open' ? '进行中' : '已结束' }}</span>
        </button>
    </div>

    <!-- ═══ 主体：当前投票详情 ═══ -->
    <div class="vote-body" ref="bodyEl">
      <div v-if="loading" class="state-box">加载中…</div>

      <div v-else-if="currentRound" class="vote-card" :class="{ closed: currentRound.status === 'closed' }">
        <!-- 状态行 -->
        <div class="card-top">
          <span class="status-badge" :class="currentRound.status">
            {{ currentRound.status === 'open' ? '进行中' : '已结束' }}
          </span>
          <span v-if="currentRound.allowProposals" class="due">可提案 {{ currentRound.maxProposalsPerUser }} 个/人</span>
        </div>

        <!-- ═══ 顶部：显眼结束倒计时（进行中且有截止时间，精确到秒，主题色）═══ -->
        <div
          v-if="currentRound.status === 'open' && countdown && !countdown.expired"
          class="countdown"
        >
          <span class="cd-label">距投票结束</span>
          <span class="cd-time">
            <template v-if="countdown.d > 0">
              <b class="cd-num" :key="'d' + countdown.d">{{ countdown.d }}</b><i>天</i>
            </template>
            <b class="cd-num" :key="'h' + countdown.h">{{ countdown.h }}</b><i>:</i>
            <b class="cd-num" :key="'m' + countdown.m">{{ countdown.m }}</b><i>:</i>
            <b class="cd-num sec" :key="'s' + countdown.s">{{ countdown.s }}</b>
          </span>
        </div>

        <!-- ═══ 顶部：开始 / 截止 / 结束时间（各占一行，标签加粗）═══ -->
        <div class="time-row">
          <div class="ti"><b class="ti-label">开始时间</b> <span>{{ fmtTime(currentRound.createdAt) }}</span></div>
          <div class="ti"><b class="ti-label">截止时间</b> <span>{{ currentRound.endAt ? fmtTime(currentRound.endAt) : '长期有效' }}</span></div>
        </div>

        <h2 class="title">{{ currentRound.title }}</h2>

        <!-- 说明内容（创建/编辑时填写） -->
        <p v-if="currentRound.description" class="desc">{{ currentRound.description }}</p>

        <!-- 选项（两段式：先选中变蓝，再点确认）
          TransitionGroup：新提案插入时占位缓缓下移→淡入出现；:key=轮次id，切轮次整体重建不播放动画 -->
        <TransitionGroup
          :key="currentRound.id"
          tag="div"
          class="options"
          name="opt"
          @before-enter="optBeforeEnter"
          @enter="optEnter"
          @after-enter="optAfterEnter"
        >
            <div
              v-for="o in currentRound.options"
              :key="o.id"
              class="option"
              :class="{
                voted: currentRound.my?.votedOptions?.includes(o.id),
                'just-added': o.id === justProposedId
              }"
            >
            <button
              class="option-main"
              :class="{ selected: selectedId === o.id }"
              :disabled="!canVote(currentRound, o)"
              @click="toggleSelect(currentRound.id, o.id)"
            >
              <span class="opt-text">{{ o.text }}</span>
              <span v-if="o.type === 'custom' && o.anonymous" class="tag anon">匿名提案</span>
              <span v-else-if="o.type === 'custom'" class="tag proposer">{{ o.proposer }} 提案</span>
              <span v-if="currentRound.my?.votedOptions?.includes(o.id)" class="voted-mark">✓ 已投</span>
              <span v-else-if="selectedId === o.id" class="confirm-hint">再点一次确认</span>
            </button>
            <div class="opt-result">
              <div class="bar"><div class="fill" :style="{ width: pct(o, currentRound.options) }"></div></div>
              <span class="score">{{ o.score }} 分 · {{ o.votes }} 票</span>
            </div>
          </div>
        </TransitionGroup>

        <!-- 提案：模拟追加一个选项栏位（出现/消失带高度动画，不挤占生硬位移） -->
        <Transition
          @before-enter="ppBeforeEnter"
          @enter="ppEnter"
          @leave="ppLeave"
        >
          <div v-if="canPropose" class="propose-row">
            <span class="propose-tag">提案</span>
            <input
              v-model="proposalText"
              class="propose-input"
              placeholder="输入你的自定义选项（最长 50 字）"
              maxlength="50"
              @keyup.enter="submitProposal(currentRound.id)"
            />
            <label class="propose-anon"><input v-model="proposalAnon" type="checkbox" /> 匿名</label>
            <button class="propose-btn" :disabled="!proposalText.trim()" @click="submitProposal(currentRound.id)">提交</button>
          </div>
        </Transition>

        <!-- 登录信息上方的换行与分隔线（始终存在） -->
        <br />
        <hr class="vote-hr" />

        <!-- ═══ 登录区（放页面底部，淡蓝流光数据卡；Transition 平滑出现）═══ -->
        <Transition name="mh" appear>
          <div v-if="isLoggedIn" class="my-hint" :class="currentRound.status">
          <template v-if="currentRound.my">
            <div class="mh-head">
              <div class="mh-user">
                <b class="mh-name">{{ me.username }}</b>
                <span v-if="me.qq" class="mh-qq">QQ {{ me.qq }}</span>
              </div>
            </div>

            <template v-if="currentRound.status === 'open'">
              <div class="mh-stats">
                <div class="mh-stat">
                  <div class="mh-num" :key="'left' + currentRound.my.votesLeft">{{ currentRound.my.votesLeft }}</div>
                  <div class="mh-label">还可投</div>
                </div>
                <div class="mh-stat">
                  <div class="mh-num" :key="'voted' + currentRound.my.votedOptions.length">{{ currentRound.my.votedOptions.length }}</div>
                  <div class="mh-label">已投</div>
                </div>
                <div class="mh-stat">
                  <div class="mh-num" :key="'w' + currentRound.my.weight">{{ currentRound.my.weight }}</div>
                  <div class="mh-label">权重 分/票</div>
                </div>
              </div>
              <div class="mh-rule">
                每票上限 {{ currentRound.maxVotesPerUser }} 票
                <template v-if="ruleText(currentRound)">· 基础 {{ currentRound.my.baseWeight }} 分，{{ ruleText(currentRound) }}</template>
                <template v-else>· 基础 {{ currentRound.my.baseWeight }} 分</template>
              </div>
            </template>
            <div v-else class="mh-closed">该投票已结束，无法再投票，仅可查看结果</div>
          </template>
          </div>
        </Transition>

        <!-- ═══ 未登录：参与入口（放页面底部）═══ -->
        <div v-if="!isLoggedIn && currentRound.status === 'open'" class="login-cta">
          <button class="btn-primary" @click="showLogin = true">登录以参加投票</button>
        </div>
        <div v-else-if="!isLoggedIn" class="closed-note">该投票已结束</div>

        <div v-if="voteError" class="error-box">{{ voteError }}</div>
      </div>

      <div v-else class="state-box">暂无进行中的投票</div>
    </div>

    <!-- ═══ 登录模态框（背景高斯模糊）═══ -->
    <Teleport to="body">
      <div v-if="showLogin" class="modal-mask" @click.self="showLogin = false">
        <div class="modal">
          <button class="modal-close" @click="showLogin = false" aria-label="关闭">✕</button>
          <h3>登录以参加投票</h3>
          <p class="modal-sub">使用绑定的 QQ 号或角色名登录</p>
          <form @submit.prevent="login">
            <input
              v-model="loginForm.account"
              placeholder="QQ 号 / 角色名"
              :disabled="loginLoading"
              autocomplete="username"
            />
            <input
              v-model="loginForm.password"
              type="password"
              placeholder="密码"
              :disabled="loginLoading"
              autocomplete="current-password"
            />
            <div v-if="loginError" class="error-box">{{ loginError }}</div>
            <button class="btn-primary" type="submit" :disabled="loginLoading">
              {{ loginLoading ? '登录中…' : '登 录' }}
            </button>
          </form>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.vote-page {
  min-height: 100vh;
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: 48px 20px;
  box-sizing: border-box;
  /* 方案 A：淡蓝渐变底 + 左上/右下大号模糊蓝色光斑 */
  background:
    radial-gradient(600px 300px at 15% 10%, rgba(59, 130, 246, 0.12), transparent 60%),
    radial-gradient(700px 400px at 85% 90%, rgba(37, 99, 235, 0.10), transparent 60%),
    linear-gradient(160deg, #eaf2fc, #dbe8f8);
  font-family: system-ui, -apple-system, 'Segoe UI', 'PingFang SC', 'Microsoft YaHei', sans-serif;
}

/* ── 主体卡片 ── */
.vote-body {
  width: min(520px, 92vw);
  margin: 0 auto;
}

.vote-card {
  background: #fff;
  border-radius: 0;
  padding: 26px 26px 22px;
  box-shadow: 0 18px 50px rgba(37, 99, 235, 0.18);
  border: none;
  box-sizing: border-box;
  animation: fadeUp 0.3s ease;
}
@keyframes fadeUp {
  from { opacity: 0; transform: translateY(8px); }
  to { opacity: 1; transform: none; }
}

.card-top { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; margin-bottom: 10px; }

.status-badge {
  font-size: 0.72rem;
  font-weight: 700;
  padding: 3px 12px;
  border-radius: 999px;
  letter-spacing: 0.02em;
}
/* 徽标颜色：绿 = 进行中，红 = 已结束 */
.status-badge.open, .fm-badge.open { background: rgba(22, 163, 74, 0.12); color: #16a34a; }
.status-badge.closed, .fm-badge.closed { background: rgba(239, 68, 68, 0.12); color: #dc2626; }

.due { font-size: 0.76rem; color: #94a3b8; }

.title {
  margin: 0 0 16px;
  font-size: 1.35rem;
  font-weight: 800;
  color: #0f172a;
  line-height: 1.35;
  word-break: break-word;
}

/* ── 说明内容（标题下方） ── */
.desc {
  margin: -4px 0 16px;
  padding: 14px 16px;
  background: rgba(59, 130, 246, 0.06);
  border-radius: 8px;
  color: #334155;
  font-size: 1.02rem;
  line-height: 1.85;
  white-space: pre-wrap;
  word-break: break-word;
}

/* ── 显眼倒计时（顶部，主题蓝色，数字滑入动画） ── */
.countdown {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 14px;
  padding: 14px 18px;
  border-radius: 6px;
  margin-bottom: 10px;
  background: linear-gradient(135deg, rgba(59, 130, 246, 0.08), rgba(37, 99, 235, 0.16));
  border: 1px solid rgba(59, 130, 246, 0.25);
  border-left: 4px solid #3b82f6;
  box-sizing: border-box;
}
.cd-label {
  font-size: 0.82rem;
  font-weight: 800;
  color: #2563eb;
  letter-spacing: 0.06em;
  white-space: nowrap;
}
.cd-time {
  display: flex;
  align-items: baseline;
  gap: 2px;
  font-size: 1.6rem;
  font-weight: 800;
  color: #1d4ed8;
  font-variant-numeric: tabular-nums;
  line-height: 1;
}
.cd-time i { font-style: normal; font-size: 0.9rem; color: #3b82f6; margin: 0 4px; }
.cd-num {
  display: inline-block;
  min-width: 1.1em;
  text-align: center;
  animation: cdTick 0.45s ease;
}
@keyframes cdTick {
  0% { opacity: 0; transform: translateY(-35%); }
  35% { opacity: 1; transform: translateY(0); }
  100% { opacity: 1; transform: none; }
}

/* ── 时间信息（开始/截止各占一行，标签加粗） ── */
.time-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 14px;
  padding: 10px 14px;
  background: rgba(59, 130, 246, 0.05);
  border: 1px solid rgba(59, 130, 246, 0.2);
  border-radius: 6px;
}
.ti { display: flex; align-items: baseline; gap: 8px; font-size: 0.85rem; color: #475569; }
.ti-label { color: #2563eb; font-weight: 800; font-size: 0.9rem; white-space: nowrap; }
.ti span { font-variant-numeric: tabular-nums; }

/* ── 登录后提示条 ── */
/* ── 登录后提示条（淡蓝↔蓝动态流光卡，浅底深字） ── */
.my-hint {
  border-radius: 6px;
  padding: 14px 16px;
  margin-bottom: 16px;
  background: linear-gradient(120deg, #dbeafe 0%, #93c5fd 25%, #60a5fa 50%, #93c5fd 75%, #dbeafe 100%);
  background-size: 250% 100%;
  animation: shimmerFlow 6s ease-in-out infinite;
  box-shadow: 0 10px 28px rgba(59, 130, 246, 0.25);
  color: #1e3a8a;
}
@keyframes shimmerFlow {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}
.my-hint.closed {
  background: linear-gradient(120deg, #e2e8f0, #cbd5e1);
  background-size: 100% 100%;
  animation: none;
  box-shadow: 0 10px 28px rgba(100, 116, 139, 0.25);
}

.mh-head { display: flex; align-items: center; gap: 10px; }
.mh-user { display: flex; align-items: center; gap: 8px; min-width: 0; }
.mh-name { font-size: 0.92rem; font-weight: 800; color: #1e3a8a; }
.mh-qq { font-size: 0.76rem; color: rgba(30, 58, 138, 0.65); }

.mh-stats {
  display: flex;
  margin-top: 12px;
  border-top: 1px solid rgba(30, 58, 138, 0.16);
  padding-top: 12px;
}
.mh-stat { flex: 1; text-align: center; }
.mh-stat + .mh-stat { border-left: 1px solid rgba(30, 58, 138, 0.14); }
.mh-num {
  font-size: 1.45rem;
  font-weight: 800;
  line-height: 1.1;
  color: #1e3a8a;
  font-variant-numeric: tabular-nums;
  animation: numPop 0.35s ease;
}
@keyframes numPop {
  0% { transform: scale(0.6); opacity: 0.3; }
  100% { transform: scale(1); opacity: 1; }
}
.mh-label { font-size: 0.72rem; color: rgba(30, 58, 138, 0.68); margin-top: 4px; letter-spacing: 0.03em; }
.mh-rule {
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px solid rgba(30, 58, 138, 0.16);
  font-size: 0.76rem;
  color: rgba(30, 58, 138, 0.78);
  line-height: 1.6;
}
.mh-closed {
  margin-top: 12px;
  border-top: 1px solid rgba(30, 58, 138, 0.16);
  padding-top: 10px;
  font-size: 0.84rem;
  color: rgba(30, 58, 138, 0.85);
}

/* ── 登录卡出现/消失平滑过渡 ── */
.mh-enter-active { transition: opacity 0.3s ease, transform 0.3s ease; }
.mh-enter-from { opacity: 0; transform: translateY(8px); }
.mh-leave-active { transition: opacity 0.2s ease; }
.mh-leave-to { opacity: 0; }

/* ── 提案与登录信息之间的分隔线 ── */
.vote-hr {
  border: none;
  border-top: 1px solid rgba(59, 130, 246, 0.22);
  margin: 4px 0 14px;
}

/* ── 未登录入口 ── */
.login-cta { margin-bottom: 16px; }
.closed-note {
  font-size: 0.85rem;
  color: #16a34a;
  font-weight: 600;
  margin-bottom: 16px;
}

.btn-primary {
  width: 100%;
  padding: 13px 20px;
  background: linear-gradient(135deg, #3b82f6, #2563eb);
  color: #fff;
  border: none;
  border-radius: 12px;
  font-size: 0.98rem;
  font-weight: 700;
  cursor: pointer;
  letter-spacing: 0.04em;
  transition: all 0.2s;
  box-shadow: 0 6px 20px rgba(37, 99, 235, 0.3);
}
.btn-primary:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 8px 24px rgba(37, 99, 235, 0.4); }
.btn-primary:disabled { opacity: 0.6; cursor: not-allowed; }

/* ── 选项（直角 + 阴影；两段式：选中蓝 / 已投绿） ── */
.options { display: flex; flex-direction: column; }
/* 选项间距用 margin（不用 gap）：新选项插入时 margin 可参与过渡动画，平滑推开下方内容 */
.option + .option { margin-top: 12px; }

/* ── 选项列表 TransitionGroup：新插入项高度/间距展开（下方缓缓下移）+ 延迟淡入出现；兄弟项 FLIP 位移 ── */
.opt-enter-active {
  transition: height 0.35s cubic-bezier(0.4, 0, 0.2, 1), margin-top 0.35s cubic-bezier(0.4, 0, 0.2, 1), opacity 0.3s ease 0.15s;
}
.opt-move { transition: transform 0.35s cubic-bezier(0.4, 0, 0.2, 1); }

.option-main {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 14px;
  background: #fff;
  border: 2px solid rgba(59, 130, 246, 0.4);
  border-radius: 4px;
  box-shadow: 0 2px 8px rgba(15, 23, 42, 0.06);
  color: #0f172a;
  font-size: 0.92rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.18s;
  text-align: left;
  box-sizing: border-box;
}
.option-main:hover:not(:disabled) { border-color: #3b82f6; box-shadow: 0 4px 14px rgba(59, 130, 246, 0.18); }
/* 单击一次后：整块变成蓝色实底白字 */
.option-main.selected {
  border-color: #2563eb;
  background: linear-gradient(135deg, #3b82f6, #2563eb);
  color: #fff;
  box-shadow: 0 6px 18px rgba(37, 99, 235, 0.35);
}
.option-main.selected .opt-text { color: #fff; }
.option-main.selected .tag.proposer { background: rgba(255, 255, 255, 0.22); color: #fff; }
.option-main.selected .tag.anon { background: rgba(255, 255, 255, 0.18); color: #fff; }
.option-main.selected .confirm-hint { color: #2563eb; background: #fff; border-color: #fff; }
.option-main:disabled { cursor: not-allowed; }
.option.voted .option-main {
  border-color: rgba(34, 197, 94, 0.55);
  background: rgba(34, 197, 94, 0.06);
  box-shadow: 0 2px 8px rgba(34, 197, 94, 0.12);
}

/* ── 新提案上架：强调动画（边框高亮脉冲 + 文字从左到右扫出 + 微光扫过；进入占位/淡入由 TransitionGroup 负责） ── */
.option.just-added .option-main {
  border-color: #3b82f6;
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.25), 0 6px 18px rgba(37, 99, 235, 0.3);
  animation: newOptionPulse 1.6s ease 0.4s;
}
.option.just-added .opt-text {
  position: relative;
  overflow: hidden;
  animation: textSweep 0.9s cubic-bezier(0.6, 0, 0.2, 1) 0.12s both;
}
.option.just-added .opt-text::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: -60%;
  width: 40%;
  background: linear-gradient(100deg, transparent, rgba(255, 255, 255, 0.75), transparent);
  animation: sweepShine 1.1s ease 0.25s;
  pointer-events: none;
}
@keyframes newOptionPulse {
  0%, 100% { box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.25), 0 6px 18px rgba(37, 99, 235, 0.3); }
  50% { box-shadow: 0 0 0 9px rgba(59, 130, 246, 0.08), 0 6px 18px rgba(37, 99, 235, 0.3); }
}
@keyframes textSweep {
  from { clip-path: inset(0 100% 0 0); }
  to { clip-path: inset(0 0 0 0); }
}
@keyframes sweepShine {
  0% { left: -60%; }
  100% { left: 120%; }
}
.opt-text { flex: 1; word-break: break-word; }

.confirm-hint {
  color: #2563eb;
  font-size: 0.72rem;
  font-weight: 700;
  background: rgba(59, 130, 246, 0.12);
  border: 1px solid rgba(59, 130, 246, 0.3);
  padding: 2px 8px;
  border-radius: 4px;
  white-space: nowrap;
  animation: cdTick 0.3s ease;
}

.tag { font-size: 0.66rem; padding: 1px 8px; border-radius: 8px; font-weight: 600; white-space: nowrap; }
.tag.proposer { background: rgba(59, 130, 246, 0.1); color: #2563eb; }
.tag.anon { background: rgba(100, 116, 139, 0.12); color: #64748b; }

.voted-mark { color: #16a34a; font-size: 0.78rem; font-weight: 800; white-space: nowrap; }

.opt-result { display: flex; align-items: center; gap: 10px; margin-top: 5px; }
.bar { flex: 1; height: 7px; background: rgba(30, 41, 59, 0.07); border-radius: 4px; overflow: hidden; }
.fill { height: 100%; background: linear-gradient(90deg, #3b82f6, #60a5fa); border-radius: 4px; transition: width 0.4s ease; }
.score { font-size: 0.74rem; color: #94a3b8; width: 108px; text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }

/* ── 提案：模拟追加一个选项栏位（尺寸与选项一致：直角 + 实蓝边 + 阴影） ── */
.propose-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 14px;
  height: 50px;
  padding: 0 14px;
  border-radius: 4px;
  border: 2px solid rgba(59, 130, 246, 0.4);
  background: #fff;
  box-shadow: 0 2px 8px rgba(15, 23, 42, 0.06);
  box-sizing: border-box;
}
.propose-tag {
  font-size: 0.78rem;
  font-weight: 800;
  color: #2563eb;
  background: rgba(59, 130, 246, 0.12);
  padding: 3px 10px;
  border-radius: 4px;
  white-space: nowrap;
}
.propose-input {
  flex: 1;
  min-width: 140px;
  border: none;
  background: transparent;
  font-size: 0.9rem;
  color: #0f172a;
  padding: 6px 4px;
  outline: none;
  box-sizing: border-box;
}
.propose-input::placeholder { color: #94a3b8; }
.propose-anon { display: flex; align-items: center; gap: 4px; font-size: 0.78rem; color: #64748b; white-space: nowrap; cursor: pointer; }
.propose-anon input { accent-color: #3b82f6; }
.propose-btn {
  padding: 8px 16px;
  background: linear-gradient(135deg, #3b82f6, #2563eb);
  color: #fff;
  border: none;
  border-radius: 4px;
  font-size: 0.84rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.18s;
}
.propose-btn:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 4px 12px rgba(59, 130, 246, 0.35); }
.propose-btn:disabled { opacity: 0.5; cursor: not-allowed; }

.error-box {
  margin-top: 14px;
  padding: 10px 14px;
  border-radius: 10px;
  background: rgba(239, 68, 68, 0.08);
  color: #dc2626;
  font-size: 0.84rem;
  border: 1px solid rgba(239, 68, 68, 0.18);
}

.state-box {
  color: #94a3b8;
  font-size: 0.92rem;
  text-align: center;
  padding: 60px 0;
}

/* ── 桌面悬浮菜单（左缘贴主体右侧，fixed 自动定位） ── */
.float-menu {
  position: fixed;
  z-index: 40;
  width: 224px;
  max-height: 60vh;
  overflow-y: auto;
  background: rgba(255, 255, 255, 0.92);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(30, 41, 59, 0.08);
  border-radius: 14px;
  box-shadow: 0 8px 30px rgba(30, 41, 59, 0.12);
  padding: 12px;
  box-sizing: border-box;
}
.fm-title { font-size: 0.72rem; font-weight: 800; color: #94a3b8; letter-spacing: 0.06em; margin: 2px 4px 8px; }
.fm-item {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 9px 10px;
  background: transparent;
  border: none;
  border-radius: 9px;
  cursor: pointer;
  text-align: left;
  transition: background 0.15s;
  box-sizing: border-box;
}
.fm-item:hover { background: rgba(59, 130, 246, 0.07); }
.fm-item.cur { background: rgba(59, 130, 246, 0.12); }
.fm-name { flex: 1; font-size: 0.82rem; font-weight: 600; color: #334155; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.fm-item.closed .fm-name { color: #94a3b8; font-weight: 500; }
.fm-badge { font-size: 0.62rem; font-weight: 700; padding: 1px 8px; border-radius: 999px; white-space: nowrap; }

/* ── 窄屏右下角展开悬浮列表 ── */
.fab-menu {
  position: fixed;
  right: 20px;
  bottom: 92px;
  z-index: 40;
  width: min(300px, calc(100vw - 40px));
  max-height: 55vh;
  overflow-y: auto;
  background: rgba(255, 255, 255, 0.96);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(30, 41, 59, 0.1);
  border-radius: 14px;
  box-shadow: 0 12px 36px rgba(30, 41, 59, 0.16);
  padding: 12px;
  box-sizing: border-box;
}

/* ── 登录模态框 ── */
.modal-mask {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(15, 23, 42, 0.35);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  padding: 20px;
  box-sizing: border-box;
}
.modal {
  position: relative;
  width: min(380px, 92vw);
  background: #fff;
  border-radius: 18px;
  padding: 30px 28px 26px;
  box-shadow: 0 24px 70px rgba(15, 23, 42, 0.35);
  box-sizing: border-box;
  animation: fadeUp 0.25s ease;
}
.modal-close {
  position: absolute;
  top: 12px;
  right: 14px;
  background: none;
  border: none;
  color: #94a3b8;
  font-size: 1.05rem;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 8px;
}
.modal-close:hover { background: rgba(0, 0, 0, 0.05); color: #334155; }
.modal h3 { margin: 0 0 6px; font-size: 1.2rem; font-weight: 800; color: #0f172a; }
.modal-sub { margin: 0 0 20px; font-size: 0.82rem; color: #94a3b8; }
.modal form { display: flex; flex-direction: column; gap: 12px; }
.modal form input {
  padding: 12px 14px;
  border: 1.5px solid rgba(30, 41, 59, 0.12);
  border-radius: 10px;
  font-size: 0.92rem;
  color: #0f172a;
  background: #f8fafc;
  outline: none;
  box-sizing: border-box;
  transition: all 0.2s;
}
.modal form input:focus { border-color: #3b82f6; background: #fff; }
.modal form input:disabled { opacity: 0.6; }

@media (max-width: 640px) {
  .vote-page { padding: 32px 14px; }
  .vote-card { padding: 20px 16px 18px; }
  .title { font-size: 1.15rem; }
}
</style>
