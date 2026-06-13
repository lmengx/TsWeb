<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { get, post } from '../../utils/api.js'
import InventoryViewer from '../../components/InventoryViewer.vue'

const route = useRoute()
const router = useRouter()

const userDetails = ref(null)
const loading = ref(true)
const error = ref('')

const invseeInventory = ref([])
const invseeLoading = ref(false)
const invseeError = ref('')

const duplicateIPLoading = ref(false)
const duplicateIPResult = ref(null)

const showGiveModal = ref(false)
const showPrefixDropdown = ref(false)
const giveItemId = ref('')
const giveAmount = ref(1)
const givePrefixId = ref('')
const giveLoading = ref(false)
const giveError = ref('')
const giveSuccess = ref('')
const giveWarning = ref('')

const showBanModal = ref(false)
const banReason = ref('不当行为')
const banLoading = ref(false)
const banError = ref('')
const banSuccess = ref('')

const showKickModal = ref(false)
const kickReason = ref('')
const kickLoading = ref(false)
const kickError = ref('')
const kickSuccess = ref('')

const isOnline = ref(false)
const onlinePlayers = ref([])

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

const filteredPrefixes = computed(() => {
  const input = givePrefixId.value.trim()
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
  givePrefixId.value = prefix.name
  showPrefixDropdown.value = false
}

const truncatedUUID = computed(() => {
  if (!userDetails.value?.UUID) return '未知'
  const uuid = userDetails.value.UUID
  return uuid.length > 8 ? uuid.substring(0, 8) + '...' : uuid
})

const truncatedIP = computed(() => {
  if (!userDetails.value?.KnownIPs) return '无'
  const ip = userDetails.value.KnownIPs
  return ip.length > 25 ? ip.substring(0, 25) + '...' : ip
})

const itemImageUrl = computed(() => {
  if (!giveItemId.value || isNaN(parseInt(giveItemId.value))) return null
  return `/assets/img/img/Item_${giveItemId.value}.png`
})

const openGiveModal = () => {
  showGiveModal.value = true
  giveItemId.value = ''
  giveAmount.value = 1
  givePrefixId.value = ''
  giveError.value = ''
  giveSuccess.value = ''
}

const closeGiveModal = () => {
  showGiveModal.value = false
}

const executeGive = async () => {
  if (!giveItemId.value.trim()) {
    giveError.value = '请输入物品ID'
    return
  }

  giveLoading.value = true
  giveError.value = ''
  giveSuccess.value = ''
  giveWarning.value = ''

  const username = userDetails.value?.Username || userDetails.value?.name || route.params.username
  let command = `/give ${giveItemId.value} ${username} ${giveAmount.value}`
  let prefixIdToUse = ''

  if (givePrefixId.value.trim()) {
    const input = givePrefixId.value.trim()
    if (prefixMap[input] !== undefined) {
      prefixIdToUse = prefixMap[input]
    } else if (/^\d+$/.test(input)) {
      prefixIdToUse = input
      giveWarning.value = '前缀ID使用了数字输入，已直接使用该ID'
    } else {
      giveWarning.value = `前缀"${input}"无法识别，已忽略`
    }
    if (prefixIdToUse) {
      command += ` ${prefixIdToUse}`
    }
  }

  try {
    const response = await post('/api/tshock/command', { command })
    const result = await response.json()

    if (result.error) {
      giveError.value = result.error
    } else {
      giveSuccess.value = result.response || '物品已给予'
      fetchInventory(username)
    }
  } catch (err) {
    giveError.value = err.message || '执行失败'
  }

  giveLoading.value = false
}

const fetchUserDetails = async (username) => {
  loading.value = true
  error.value = ''
  try {
    const response = await get(`/api/tshock/userlist?username=${encodeURIComponent(username)}`)
    const result = await response.json()

    if (result.status === '200' && result.users && result.users.length > 0) {
      userDetails.value = result.users[0]
    } else {
      error.value = '用户不存在'
    }
  } catch (err) {
    error.value = '加载失败: ' + err.message
  }
  loading.value = false
}

const fetchInventory = async (username) => {
  invseeLoading.value = true
  invseeInventory.value = []
  invseeError.value = ''

  try {
    const response = await get(`/api/tshock/invsee?player=${encodeURIComponent(username)}`)
    const data = await response.json()

    if (data.status === '200' && data.inventory) {
      invseeInventory.value = data.inventory
    } else if (data.error) {
      invseeError.value = data.error
    } else {
      invseeError.value = '未知错误'
    }
  } catch (err) {
    if (err.message !== 'Unauthorized') {
      invseeError.value = 'Error: ' + err.message
    }
  }

  invseeLoading.value = false
}

const handleEditItem = async (data) => {
  const { player, slotIndex, itemId, stack, prefix } = data
  
  try {
    const response = await post('/api/tshock/editinv', {
      player,
      slotIndex,
      itemId,
      stack,
      prefix
    })
    const result = await response.json()
    
    if (result.error) {
      alert('编辑失败: ' + result.error)
    } else {
      fetchInventory(player)
    }
  } catch (err) {
    alert('编辑失败: ' + err.message)
  }
}

const checkDuplicateIPs = async () => {
  if (!userDetails.value) return

  duplicateIPLoading.value = true
  duplicateIPResult.value = null

  try {
    const username = userDetails.value.Username || userDetails.value.name
    const response = await get(`/api/tshock/duplicateips?username=${encodeURIComponent(username)}`)
    const result = await response.json()
    duplicateIPResult.value = result
  } catch (err) {
    console.error('Failed to check duplicate IPs:', err)
    duplicateIPResult.value = { error: '查询失败' }
  }

  duplicateIPLoading.value = false
}

const resetDuplicateIPResult = () => {
  duplicateIPResult.value = null
}

const openBanModal = () => {
  showBanModal.value = true
  banReason.value = '不当行为'
  banError.value = ''
  banSuccess.value = ''
}

const closeBanModal = () => {
  showBanModal.value = false
}

const executeBan = async () => {
  if (!userDetails.value) return

  banLoading.value = true
  banError.value = ''
  banSuccess.value = ''

  try {
    const response = await post('/api/tshock/ban', {
      name: userDetails.value.Username || userDetails.value.name,
      reason: banReason.value
    })
    const result = await response.json()

    if (result.error) {
      banError.value = result.error
    } else {
      banSuccess.value = result.response || '封禁成功'
    }
  } catch (err) {
    banError.value = err.message || '封禁失败'
  }

  banLoading.value = false
}

const openKickModal = () => {
  showKickModal.value = true
  kickReason.value = ''
  kickError.value = ''
  kickSuccess.value = ''
}

const closeKickModal = () => {
  showKickModal.value = false
}

const executeKick = async () => {
  if (!userDetails.value) return

  kickLoading.value = true
  kickError.value = ''
  kickSuccess.value = ''

  try {
    const username = userDetails.value.Username || userDetails.value.name
    const command = kickReason.value.trim() 
      ? `/kick ${username} ${kickReason.value}`
      : `/kick ${username}`

    const response = await post('/api/tshock/command', { command })
    const result = await response.json()

    if (result.error) {
      kickError.value = result.error
    } else {
      kickSuccess.value = result.response || '踢出成功'
      await checkOnlineStatus()
    }
  } catch (err) {
    kickError.value = err.message || '踢出失败'
  }

  kickLoading.value = false
}

const checkOnlineStatus = async () => {
  try {
    const response = await get('/api/tshock/activeusers')
    const result = await response.json()
    
    if (result.activeusers) {
      const names = result.activeusers.split('\t').filter(n => n.trim())
      onlinePlayers.value = names
      const username = userDetails.value?.Username || userDetails.value?.name
      isOnline.value = names.some(name => name.toLowerCase() === username.toLowerCase())
    } else {
      isOnline.value = false
    }
  } catch (err) {
    isOnline.value = false
  }
}

const copyToClipboard = async (text) => {
  if (!text) return
  try {
    await navigator.clipboard.writeText(text)
    alert('已复制到剪贴板')
  } catch (err) {
    console.error('复制失败:', err)
  }
}

const goBack = () => {
  router.push('/console/players')
}

watch(() => route.params.username, (newUsername) => {
  if (newUsername) {
    fetchUserDetails(newUsername)
    fetchInventory(newUsername)
    checkOnlineStatus()
  }
}, { immediate: true })

onMounted(() => {
  if (route.params.username) {
    fetchUserDetails(route.params.username)
    fetchInventory(route.params.username)
    checkOnlineStatus()
  }
})
</script>

<template>
  <div class="user-detail-page">
    <div class="page-header">
      <button @click="goBack" class="back-btn">
        ← 返回
      </button>
      <div class="title-section">
        <h2>用户详情</h2>
        <template v-if="userDetails">
          <span class="username">{{ userDetails.Username || userDetails.name }}</span>
          <div class="online-status" :class="{ online: isOnline }">
            <span class="status-dot"></span>
            <span>{{ isOnline ? '在线' : '离线' }}</span>
          </div>
        </template>
      </div>
      <div v-if="userDetails" class="action-buttons">
        <button @click="openKickModal" :disabled="!isOnline" class="kick-btn" :class="{ disabled: !isOnline }">
          踢出
        </button>
        <button @click="openBanModal" class="ban-btn">
          封禁
        </button>
      </div>
    </div>

    <div v-if="loading" class="loading-state">
      <p>加载中...</p>
    </div>

    <div v-else-if="error" class="error-state">
      <p>{{ error }}</p>
    </div>

    <template v-else-if="userDetails">
      <div class="user-info-section">
        <dl class="info-list">
          <div class="info-item">
            <dt>ID</dt>
            <dd>{{ userDetails.ID || userDetails.id }}</dd>
          </div>
          <div class="info-item">
            <dt>用户名</dt>
            <dd>{{ userDetails.Username || userDetails.name }}</dd>
          </div>
          <div class="info-item">
            <dt>用户组</dt>
            <dd>{{ userDetails.Usergroup || userDetails.group }}</dd>
          </div>
          <div class="info-item">
            <dt>UUID</dt>
            <dd class="uuid-with-copy">
              <span :title="'完整UUID: ' + userDetails.UUID">{{ truncatedUUID }}</span>
              <button
                @click="copyToClipboard(userDetails.UUID)"
                class="copy-badge"
                title="复制UUID"
              >
                复制
              </button>
            </dd>
          </div>
          <div class="info-item">
            <dt>注册时间</dt>
            <dd>{{ userDetails.Registered ? new Date(userDetails.Registered).toLocaleString() : '未知' }}</dd>
          </div>
          <div class="info-item">
            <dt>最后访问</dt>
            <dd>{{ userDetails.LastAccessed ? new Date(userDetails.LastAccessed).toLocaleString() : '从未访问' }}</dd>
          </div>
          <div class="info-item">
            <dt>已知IP</dt>
            <dd class="ip-with-check">
              <button
                @click="checkDuplicateIPs"
                :disabled="duplicateIPLoading"
                class="check-badge"
                :class="{ loading: duplicateIPLoading }"
                title="检测共享IP"
              >
                {{ duplicateIPLoading ? '检测中...' : '检测共享' }}
              </button>
              <span class="ip-text" :title="'完整IP: ' + userDetails.KnownIPs">{{ truncatedIP }}</span>
            </dd>
          </div>
        </dl>

        <div v-if="duplicateIPResult" class="duplicate-ip-result">
          <button @click="resetDuplicateIPResult" class="close-result-btn">×</button>

          <div v-if="duplicateIPResult.error" class="result-error">
            <p>❌ {{ duplicateIPResult.error }}</p>
          </div>

          <div v-else-if="duplicateIPResult.count === 0" class="result-empty">
            <p>✅ 未发现共享IP的用户</p>
          </div>

          <div v-else class="result-content">
            <h4>发现 {{ duplicateIPResult.count }} 个共享IP的用户:</h4>
            <div class="duplicate-list">
              <div
                v-for="user in duplicateIPResult.duplicates"
                :key="user.ID"
                class="duplicate-item"
              >
                <span class="duplicate-id">ID: {{ user.ID }}</span>
                <span class="duplicate-name">用户名: {{ user.Username }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div class="inventory-section">
        <div class="inventory-header">
          <h3>背包信息</h3>
          <button @click="openGiveModal" class="give-btn">
            给予物品
          </button>
        </div>
        <InventoryViewer
          :inventory="invseeInventory"
          :loading="invseeLoading"
          :error="invseeError"
          :initial-username="route.params.username"
          @fetch="fetchInventory"
          @editItem="handleEditItem"
        />
      </div>
    </template>

    <div v-if="showKickModal" class="modal-overlay" @click.self="closeKickModal">
      <div class="modal">
        <div class="modal-header">
          <h3>踢出玩家</h3>
          <button @click="closeKickModal" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="kick-form">
            <div class="form-row">
              <label>踢出原因</label>
              <input
                v-model="kickReason"
                type="text"
                placeholder="输入踢出原因（可选）"
                class="form-input"
              />
            </div>
          </div>
          <div v-if="kickError" class="give-error">
            {{ kickError }}
          </div>
          <div v-if="kickSuccess" class="give-success">
            {{ kickSuccess }}
          </div>
        </div>
        <div class="modal-footer">
          <button @click="closeKickModal" class="cancel-btn">取消</button>
          <button @click="executeKick" :disabled="kickLoading" class="kick-submit-btn">
            {{ kickLoading ? '踢出中...' : '确认踢出' }}
          </button>
        </div>
      </div>
    </div>

    <div v-if="showBanModal" class="modal-overlay" @click.self="closeBanModal">
      <div class="modal">
        <div class="modal-header">
          <h3>封禁玩家</h3>
          <button @click="closeBanModal" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="ban-warning">
            <p>⚠️ 此操作将封禁该玩家的账户、UUID 和所有已知 IP 地址。</p>
            <p>被封禁的玩家将无法再次进入服务器。</p>
          </div>
          <div class="ban-form">
            <div class="form-row">
              <label>封禁原因</label>
              <input
                v-model="banReason"
                type="text"
                placeholder="输入封禁原因"
                class="form-input"
              />
            </div>
          </div>
          <div v-if="banError" class="give-error">
            {{ banError }}
          </div>
          <div v-if="banSuccess" class="give-success">
            {{ banSuccess }}
          </div>
        </div>
        <div class="modal-footer">
          <button @click="closeBanModal" class="cancel-btn">取消</button>
          <button @click="executeBan" :disabled="banLoading" class="ban-submit-btn">
            {{ banLoading ? '封禁中...' : '确认封禁' }}
          </button>
        </div>
      </div>
    </div>

    <div v-if="showGiveModal" class="modal-overlay" @click.self="closeGiveModal">
      <div class="modal">
        <div class="modal-header">
          <h3>给予物品</h3>
          <button @click="closeGiveModal" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="give-form">
            <div class="form-row">
              <label>物品ID</label>
              <div class="item-input-row">
                <input
                  v-model="giveItemId"
                  type="text"
                  placeholder="输入物品ID"
                  class="form-input"
                />
                <div v-if="itemImageUrl" class="item-preview">
                  <img
                    :src="itemImageUrl"
                    :alt="'Item ' + giveItemId"
                    class="item-image"
                    @error="(e) => e.target.style.display = 'none'"
                  />
                </div>
              </div>
            </div>
            <div class="form-row">
              <label>数量</label>
              <input
                v-model="giveAmount"
                type="number"
                min="1"
                class="form-input"
              />
            </div>
            <div class="form-row">
              <label>前缀ID</label>
              <input
                v-model="givePrefixId"
                type="text"
                placeholder="可选，默认无，可输入中文搜索"
                class="form-input"
                @input="giveWarning = ''; showPrefixDropdown = true"
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

          <div v-if="giveError" class="give-error">
            {{ giveError }}
          </div>
          <div v-if="giveWarning" class="give-warning">
            {{ giveWarning }}
          </div>
          <div v-if="giveSuccess" class="give-success">
            {{ giveSuccess }}
          </div>
        </div>
        <div class="modal-footer">
          <button @click="closeGiveModal" class="cancel-btn">取消</button>
          <button @click="executeGive" :disabled="giveLoading" class="submit-btn">
            {{ giveLoading ? '执行中...' : '给予' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.user-detail-page {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  padding: 0;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 24px;
  padding: 0 20px;
}

.title-section {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 12px;
}

.title-section h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.4rem;
  font-weight: 600;
}

.username {
  color: var(--accent-primary);
  font-weight: 600;
}

.online-status {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  background: var(--bg-tertiary);
  border-radius: 20px;
  font-size: 0.8rem;
  color: var(--text-muted);
}

.online-status.online {
  background: rgba(34, 197, 94, 0.15);
  color: #22c55e;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-muted);
  backdrop-filter: blur(4px);
}

.online-status.online .status-dot {
  background: #22c55e;
  box-shadow: 0 0 8px rgba(34, 197, 94, 0.6);
}

.action-buttons {
  display: flex;
  gap: 10px;
}

.kick-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, #f59e0b, #d97706);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.kick-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(245, 158, 11, 0.4);
}

.kick-btn.disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.ban-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, #dc2626, #b91c1c);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.ban-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(220, 38, 38, 0.4);
}

.back-btn {
  padding: 10px 20px;
  background: var(--bg-tertiary);
  color: var(--text-primary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
}

.back-btn:hover {
  background: var(--bg-hover);
  border-color: var(--accent-primary);
}

.loading-state,
.error-state {
  text-align: center;
  padding: 60px 20px;
  color: var(--text-muted);
}

.error-state {
  color: var(--accent-error);
}

.user-info-section {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  margin: 0 20px 24px;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
}

.info-list {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 16px;
  margin: 0 0 20px 0;
}

.info-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
  border: 1px solid var(--border-light);
}

.info-item dt {
  color: var(--text-secondary);
  font-weight: 600;
  font-size: 0.95rem;
}

.info-item dd {
  color: var(--text-primary);
  font-size: 0.95rem;
  margin: 0;
}

.uuid-with-copy {
  display: flex;
  align-items: center;
  gap: 8px;
}

.copy-badge {
  padding: 6px 12px;
  color: #16a34a;
  border: 1px solid #16a34a;
  border-radius: var(--radius-sm);
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 500;
  transition: all 0.2s ease;
}

.copy-badge:hover {
  color: #15803d;
  border-color: #15803d;
}

.ip-with-check {
  display: flex;
  align-items: center;
  gap: 8px;
}

.ip-text {
  color: var(--text-primary);
  font-size: 0.95rem;
}

.check-badge {
  padding: 6px 12px;
  color: #16a34a;
  border: 1px solid #16a34a;
  border-radius: var(--radius-sm);
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 500;
  transition: all 0.2s ease;
}

.check-badge:hover:not(:disabled) {
  color: #15803d;
  border-color: #15803d;
}

.check-badge:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.check-badge.loading {
  color: var(--text-muted);
  border-color: var(--text-muted);
}

.duplicate-ip-result {
  margin-top: 16px;
  padding: 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
  position: relative;
  border: 1px solid var(--border-light);
}

.close-result-btn {
  position: absolute;
  top: 12px;
  right: 12px;
  background: var(--bg-hover);
  border: none;
  color: var(--text-secondary);
  font-size: 1.1rem;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: var(--radius-sm);
  transition: all 0.2s ease;
}

.close-result-btn:hover {
  color: var(--text-primary);
  background: var(--bg-card);
}

.result-error {
  color: var(--accent-error);
  font-weight: 500;
}

.result-empty {
  color: var(--accent-secondary);
  font-weight: 500;
}

.result-content h4 {
  margin: 0 0 12px 0;
  color: var(--text-primary);
  font-size: 1rem;
  font-weight: 600;
}

.duplicate-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.duplicate-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 14px;
  background: var(--bg-card);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-light);
  transition: all 0.2s ease;
}

.duplicate-item:hover {
  border-color: var(--accent-primary);
}

.duplicate-id {
  color: var(--text-secondary);
  font-size: 0.9rem;
}

.duplicate-name {
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 500;
}

.inventory-section {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 0 20px;
}

.inventory-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.inventory-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.2rem;
  font-weight: 600;
}

.give-btn {
  padding: 8px 16px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.give-btn:hover {
  transform: translateY(-1px);
  box-shadow: var(--shadow-md);
}

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
  max-width: 400px;
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

.give-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.form-row label {
  color: var(--text-secondary);
  font-weight: 600;
  font-size: 0.9rem;
}

.item-input-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.form-input {
  flex: 1;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  transition: all 0.25s ease;
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

.give-error {
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.15);
  color: var(--accent-error);
  border-radius: var(--radius-md);
  margin-top: 16px;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.give-warning {
  padding: 12px 16px;
  background: rgba(234, 179, 8, 0.15);
  color: #ca8a04;
  border-radius: var(--radius-md);
  margin-top: 16px;
  border: 1px solid rgba(234, 179, 8, 0.3);
}

.give-success {
  padding: 12px 16px;
  background: rgba(34, 197, 94, 0.15);
  color: var(--accent-secondary);
  border-radius: var(--radius-md);
  margin-top: 16px;
  border: 1px solid rgba(34, 197, 94, 0.3);
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

.form-row {
  position: relative;
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

.action-section {
  display: flex;
  justify-content: flex-end;
  padding: 0 20px;
  margin-bottom: 24px;
}

.ban-btn {
  padding: 12px 24px;
  background: linear-gradient(135deg, #dc2626, #b91c1c);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 600;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.ban-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(220, 38, 38, 0.4);
}

.ban-warning {
  background: rgba(234, 179, 8, 0.15);
  border: 1px solid rgba(234, 179, 8, 0.3);
  border-radius: var(--radius-md);
  padding: 16px;
  margin-bottom: 20px;
}

.ban-warning p {
  margin: 6px 0;
  color: #92400e;
  font-size: 0.9rem;
}

.ban-submit-btn {
  padding: 12px 24px;
  background: linear-gradient(135deg, #dc2626, #b91c1c);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 600;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.ban-submit-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(220, 38, 38, 0.4);
}

.ban-submit-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
