<script setup>
/**
 * 服务器切换器（多服）
 *
 * 桌面：侧边栏顶部胶囊 —— 管理员保持原有「一键进服务器管理页」行为；
 *       子管理员（subadmin）不能进管理页（/console/servers 为 requiresAdmin），
 *       故点击改为弹出下拉面板就地切换。
 * 移动：内容区顶部服务器条 —— 管理员与子管理员都弹底部弹层切换
 *       （移动端没有侧边栏，此条是唯一入口）；管理员额外多一条「服务器管理」入口。
 *
 * 切换语义与 ServersView 的「设为当前」一致：selectServer(id) + 广播 server-changed。
 */
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isAdmin, isManager } from '../utils/authHelper.js'
import {
  getServers, getCurrentServer, getCurrentServerId, fetchServers, selectServer
} from '../utils/serverStore.js'

const props = defineProps({
  // desktop = 侧边栏胶囊（下拉）；mobile = 内容区顶部服务器条（底部弹层）
  variant: { type: String, default: 'desktop' }
})

const route = useRoute()
const router = useRouter()

const admin = computed(() => isAdmin())
// 仅管理角色（admin + subadmin）可见：与 /console 的实际可达范围一致
const canSwitch = computed(() => isManager())
// 服务器管理页自身就是切换入口，移动端不在该页重复显示服务器条
const hiddenHere = computed(() => props.variant === 'mobile' && route.path === '/console/servers')

const servers = ref([])
const currentServer = ref(null)
const currentServerId = computed(() => getCurrentServerId())
const open = ref(false)

const triggerRef = ref(null)
const dropStyle = ref({})

const loadServers = async () => {
  await fetchServers()
  servers.value = getServers()
  currentServer.value = getCurrentServer()
}

// 切换后只同步本地回显（列表本身不变），避免整表重排闪烁
const onServerChanged = () => {
  currentServer.value = getCurrentServer()
  servers.value = getServers()
}

let statusTimer = null
onMounted(() => {
  loadServers()
  // 在线状态点与后端心跳（15s）同频刷新
  statusTimer = setInterval(loadServers, 15000)
  window.addEventListener('server-changed', onServerChanged)
})

onUnmounted(() => {
  if (statusTimer) clearInterval(statusTimer)
  window.removeEventListener('server-changed', onServerChanged)
})

const toggle = () => {
  open.value = !open.value
  if (!open.value) return
  loadServers()
  // 侧边栏自身 overflow: hidden 会裁掉下拉，故面板 Teleport 到 body 并按触发器位置定位
  if (props.variant === 'desktop' && triggerRef.value) {
    const rect = triggerRef.value.getBoundingClientRect()
    dropStyle.value = {
      top: (rect.bottom + 8) + 'px',
      left: rect.left + 'px',
      width: Math.max(rect.width, 208) + 'px'
    }
  }
}

const close = () => { open.value = false }

const onTriggerClick = () => {
  // 管理员保持原有行为：直接进服务器管理页（可增删改 + 切换）
  if (props.variant === 'desktop' && admin.value) {
    router.push('/console/servers')
    return
  }
  toggle()
}

const switchTo = (s) => {
  if (s.id !== getCurrentServerId()) {
    selectServer(s.id)
    // 通知横幅/控制台等监听方；Console.vue 依此重挂当前视图，避免继续显示上一台服的数据
    window.dispatchEvent(new CustomEvent('server-changed', { detail: { serverId: s.id } }))
  }
  close()
}

const goManage = () => {
  close()
  router.push('/console/servers')
}
</script>

<template>
  <!-- ═══ 桌面：侧边栏顶部胶囊 ═══ -->
  <div v-if="canSwitch && variant === 'desktop'" class="sw-desktop">
    <button
      ref="triggerRef"
      class="sw-pill"
      :class="{ open }"
      :title="admin ? '服务器管理' : '切换服务器'"
      @click="onTriggerClick"
    >
      <span class="sw-dot" :class="{ online: currentServer?.connected }"></span>
      <span class="sw-name">{{ currentServer?.name || '暂无服务器' }}</span>
      <svg class="sw-chevron" :class="{ rotated: open }" width="14" height="14" viewBox="0 0 24 24"
        fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="9 18 15 12 9 6"></polyline>
      </svg>
    </button>
  </div>

  <!-- ═══ 移动：内容区顶部服务器条 ═══ -->
  <button
    v-if="canSwitch && variant === 'mobile' && !hiddenHere"
    class="sw-bar"
    :class="{ open }"
    @click="toggle"
  >
    <span class="sw-dot" :class="{ online: currentServer?.connected }"></span>
    <span class="sw-bar-label">当前服务器</span>
    <span class="sw-name">{{ currentServer?.name || '暂无服务器' }}</span>
    <svg class="sw-chevron" :class="{ rotated: open }" width="14" height="14" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
      <polyline points="6 9 12 15 18 9"></polyline>
    </svg>
  </button>

  <!-- ═══ 切换面板（桌面=下拉 / 移动=底部弹层） ═══ -->
  <Teleport to="body">
    <div
      v-if="open"
      :class="variant === 'desktop' ? 'sw-backdrop' : 'sw-sheet-mask'"
      @click="close"
    >
      <div
        :class="variant === 'desktop' ? 'sw-dropdown' : 'sw-sheet'"
        :style="variant === 'desktop' ? dropStyle : null"
        @click.stop
      >
        <div class="sw-header">
          <h3>切换服务器</h3>
          <button class="sw-close" @click="close">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor"
              stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>

        <div class="sw-list">
          <button
            v-for="s in servers"
            :key="s.id"
            class="sw-item"
            :class="{ current: s.id === currentServerId }"
            @click="switchTo(s)"
          >
            <span class="sw-dot" :class="{ online: s.connected }"></span>
            <span class="sw-item-name">{{ s.name || s.host }}</span>
            <span v-if="s.id === currentServerId" class="sw-tag current-tag">当前</span>
            <span v-else-if="s.enabled === false" class="sw-tag off-tag">已停用</span>
          </button>

          <div v-if="servers.length === 0" class="sw-empty">
            {{ admin ? '尚未添加服务器，请到服务器管理页添加' : '尚未配置服务器，请联系管理员' }}
          </div>
        </div>

        <div v-if="admin" class="sw-footer">
          <button class="sw-manage" @click="goManage">
            <span>服务器管理</span>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor"
              stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
              <polyline points="9 18 15 12 9 6"></polyline>
            </svg>
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* ── 状态点 ── */
.sw-dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--accent-error);
  flex-shrink: 0;
  box-shadow: 0 0 0 2px rgba(244, 63, 94, 0.15);
}
.sw-dot.online {
  background: var(--accent-secondary);
  box-shadow: 0 0 8px rgba(16, 185, 129, 0.7);
}

.sw-name {
  flex: 1;
  min-width: 0;
  text-align: left;
  font-size: 0.86rem;
  font-weight: 700;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.sw-chevron {
  color: var(--accent-primary);
  flex-shrink: 0;
  transition: transform 0.25s var(--ease-out);
}
.sw-chevron.rotated { transform: rotate(180deg); }
/* 桌面胶囊是右向箭头（原「进管理页」语义），展开时转成下向而非反向 */
.sw-pill .sw-chevron.rotated { transform: rotate(90deg); }

/* ═══ 桌面胶囊 ═══ */
.sw-desktop {
  padding: 0 12px 12px;
  border-bottom: 1px solid var(--border-light);
  margin: 0 8px 12px;
}
.sw-pill {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 11px 12px;
  border: 1px solid rgba(99, 102, 241, 0.28);
  border-radius: var(--radius-md);
  cursor: pointer;
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.16), rgba(139, 92, 246, 0.1));
  color: var(--text-primary);
  transition: all 0.25s var(--ease-out);
}
.sw-pill:hover,
.sw-pill.open {
  border-color: var(--accent-primary);
  box-shadow: var(--glow-primary);
}

/* ═══ 移动服务器条 ═══ */
.sw-bar {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  margin-bottom: 12px;
  border: 1px solid rgba(99, 102, 241, 0.28);
  border-radius: var(--radius-md);
  background: linear-gradient(135deg, rgba(99, 102, 241, 0.16), rgba(139, 92, 246, 0.1));
  color: var(--text-primary);
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  transition: all 0.2s var(--ease-out);
}
.sw-bar.open { border-color: var(--accent-primary); box-shadow: var(--glow-primary); }
.sw-bar-label {
  font-size: 0.7rem;
  font-weight: 700;
  letter-spacing: 0.5px;
  color: var(--text-muted);
  flex-shrink: 0;
}
.sw-bar .sw-name { text-align: right; font-size: 0.84rem; }

/* ═══ 桌面下拉遮罩（透明，仅拦截外部点击；不得加 blur/transform，否则会改变 fixed 子元素定位基准） ═══ */
.sw-backdrop {
  position: fixed;
  inset: 0;
  z-index: 2000;
  background: transparent;
}
.sw-dropdown {
  position: fixed;
  max-height: 60vh;
  overflow-y: auto;
  background: var(--bg-primary);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-lg);
  animation: swDropIn 0.16s var(--ease-out);
}

/* ═══ 移动底部弹层（复用现有底部面板视觉） ═══ */
.sw-sheet-mask {
  position: fixed;
  inset: 0;
  z-index: 10000;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: flex-end;
  animation: swFadeIn 0.2s ease;
}
.sw-sheet {
  width: 100%;
  max-height: 58vh;
  overflow-y: auto;
  background: var(--bg-primary);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-xl) var(--radius-xl) 0 0;
  padding-bottom: env(safe-area-inset-bottom, 0);
  box-shadow: 0 -8px 40px rgba(0, 0, 0, 0.4);
  animation: swSlideUp 0.3s var(--ease-out);
}

/* ═══ 面板内容 ═══ */
.sw-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 18px 12px;
  border-bottom: 1px solid var(--border-light);
}
.sw-header h3 { margin: 0; font-size: 1rem; color: var(--text-primary); }
.sw-close {
  width: 32px;
  height: 32px;
  border-radius: 10px;
  border: 1px solid var(--border-light);
  background: var(--bg-tertiary);
  color: var(--text-secondary);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s;
}
.sw-close:hover { color: var(--accent-error); border-color: var(--accent-error); }

.sw-list { padding: 8px 10px; display: flex; flex-direction: column; gap: 2px; }
.sw-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 14px;
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: 0.88rem;
  font-weight: 500;
  cursor: pointer;
  text-align: left;
  width: 100%;
  background: transparent;
  transition: background 0.15s;
  -webkit-tap-highlight-color: transparent;
}
.sw-item:hover { background: var(--bg-hover); }
.sw-item.current { background: rgba(99, 102, 241, 0.14); color: var(--accent-primary); }
.sw-item-name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.sw-tag {
  flex-shrink: 0;
  font-size: 0.68rem;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 999px;
}
.current-tag { color: var(--accent-primary); background: rgba(99, 102, 241, 0.16); }
.off-tag { color: var(--text-muted); background: var(--bg-tertiary); }

.sw-empty {
  padding: 18px 14px;
  text-align: center;
  font-size: 0.82rem;
  color: var(--text-muted);
}

.sw-footer { padding: 6px 10px 12px; border-top: 1px solid var(--border-light); }
.sw-manage {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 14px;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-secondary);
  font-size: 0.88rem;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.15s, color 0.15s;
}
.sw-manage:hover { background: var(--bg-hover); color: var(--accent-primary); }

@keyframes swFadeIn { from { opacity: 0; } to { opacity: 1; } }
@keyframes swSlideUp { from { transform: translateY(100%); } to { transform: translateY(0); } }
@keyframes swDropIn { from { opacity: 0; transform: translateY(-6px); } to { opacity: 1; transform: translateY(0); } }
</style>
