<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { get, post } from '../../utils/api.js'
import InventoryViewer from '../../components/InventoryViewer.vue'
import { getAntiCheatConfig } from '../../api/antiCheatApi.js'
import { loadItemData } from '../../api/itemDataApi.js'

const itemData = ref({ list: [], dict: {} })
const antiCheatConfig = ref(null)

const isAnomalyItem = (id, stack) => {
  const config = antiCheatConfig.value
  if (!config || !config.items) return false

  const item = config.items.find(i => i.id === id)
  if (item) {
    return stack > item.maxStack
  }

  return false
}

const getItemName = (id) => {
  const item = itemData.value.list.find(i => i.id === id)
  return item ? item.chinese : null
}

const formatLocalDate = (dateStr) => {
  if (!dateStr) return '未知'
  // 服务器返回的是 UTC 时间（如 "2026-06-23T11:50:09" 或 "2026-06-23 11:50:09"），
  // 转为标准 ISO 末尾 +Z 表示 UTC，new Date 会正确解析，toLocaleString 自动转本地时区
  const normalized = dateStr.replace(' ', 'T') + 'Z'
  const date = new Date(normalized)
  if (isNaN(date.getTime())) return dateStr
  return date.toLocaleString('zh-CN')
}

const initItemData = async () => {
  itemData.value = await loadItemData()
}

const route = useRoute()
const router = useRouter()

const userDetails = ref(null)
const loading = ref(true)
const error = ref('')

const invseeInventory = ref([])
const invseeLoading = ref(false)
const invseeError = ref('')

const anomalyItems = computed(() => {
  const anomalies = []
  for (const item of invseeInventory.value) {
    if (item && item.netID && item.stack) {
      if (isAnomalyItem(item.netID, item.stack)) {
        anomalies.push({
          name: getItemName(item.netID) || `物品(${item.netID})`,
          stack: item.stack,
          netId: item.netID
        })
      }
    }
  }
  return anomalies
})

const duplicateIPLoading = ref(false)
const duplicateIPResult = ref(null)

const showGiveModal = ref(false)
const showPrefixDropdown = ref(false)
const showGiveItemDropdown = ref(false)
const giveItemSearchQuery = ref('')
const giveSelectedItemId = ref(0)
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

const showClearCharacterModal = ref(false)
const clearCharacterLoading = ref(false)
const clearCharacterError = ref('')
const clearCharacterSuccess = ref('')

const showPasswordModal = ref(false)
const newPassword = ref('')
const passwordLoading = ref(false)
const passwordError = ref('')
const passwordSuccess = ref('')
const showPasswordText = ref(false)

const showKickModal = ref(false)
const kickReason = ref('')
const kickLoading = ref(false)
const kickError = ref('')
const kickSuccess = ref('')

const showGroupModal = ref(false)
const newGroup = ref('')
const groupLoading = ref(false)
const groupError = ref('')
const groupSuccess = ref('')
const availableGroups = ref([])
const showGroupDropdown = ref(false)
const dropdownStyle = ref({})

const fetchGroups = async () => {
  try {
    const response = await get('/api/tshock/groups')
    const result = await response.json()
    if (result.groups) {
      availableGroups.value = result.groups.map(g => g.GroupName)
    }
  } catch (err) {
    console.error('Failed to fetch groups:', err)
  }
}

const showWhisperModal = ref(false)
const whisperMessage = ref('')
const whisperLoading = ref(false)
const whisperError = ref('')
const whisperSuccess = ref('')

const showTpModal = ref(false)
const tpFromPlayer = ref('')
const tpToPlayer = ref('')
const tpSelectTarget = ref('to')
const tpSearchQuery = ref('')
const tpSearchResults = ref([])
const tpSearchLoading = ref(false)
const tpLoading = ref(false)
const tpError = ref('')
const tpSuccess = ref('')

const isOnline = ref(false)
const onlinePlayers = ref([])

const playerStats = ref(null)
const statsLoading = ref(false)
const statsError = ref('')
const statsSaving = ref(false)
const statsSaveSuccess = ref('')
const statsSaveError = ref('')

const tempStats = ref({
  maxHealth: 400,
  maxMana: 200,
  questsCompleted: 0,
  extraSlot: false,
  unlockedBiomeTorches: false,
  ateArtisanBread: false,
  usedAegisCrystal: false,
  usedAegisFruit: false,
  usedArcaneCrystal: false,
  usedGalaxyPearl: false,
  usedGummyWorm: false,
  usedAmbrosia: false,
  unlockedSuperCart: false,
  enabledSuperCart: false
})

const enhancesMap = {
  extraSlot: '额外配饰槽（恶魔之心）',
  unlockedBiomeTorches: '火把神徽章',
  ateArtisanBread: '工匠面包',
  usedAegisCrystal: '埃癸斯水晶（生命再生+20%）',
  usedAegisFruit: '埃癸斯果（防御力+5）',
  usedArcaneCrystal: '奥术水晶（魔力再生+20%）',
  usedGalaxyPearl: '银河珍珠（运气+0.3）',
  usedGummyWorm: '黏性蠕虫（钓鱼技能+30）',
  usedAmbrosia: '珍馐（采矿/建造速度+15%）',
  unlockedSuperCart: '矿车升级包',
  enabledSuperCart: '启用超级矿车'
}

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

const giveItemSearchResults = computed(() => {
  if (!giveItemSearchQuery.value.trim()) return []

  const query = giveItemSearchQuery.value.trim().toLowerCase()
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

const selectGiveItem = (item) => {
  giveSelectedItemId.value = item.id
  giveItemSearchQuery.value = item.chinese
  showGiveItemDropdown.value = false
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

const showIpPopup = ref(false)
const ipList = computed(() => {
  if (!userDetails.value?.KnownIPs) return []
  // 支持逗号、制表符、空格、换行分隔
  const raw = userDetails.value.KnownIPs
  // 先尝试 JSON 数组格式
  try {
    const parsed = JSON.parse(raw)
    if (Array.isArray(parsed)) return parsed.filter(Boolean)
  } catch {}
  // 按常见分隔符拆分
  return raw.split(/[,，\t\n\s]+/).filter(Boolean)
})
const firstIP = computed(() => ipList.value[0] || '无')

// IP 地理位置查询
const ipLocations = ref({})
const ipLookupLoading = ref(false)
const firstIpLoaded = ref(false)

// 自动查询第一个 IP 的地理位置（未展开时显示）
const queryFirstIpLocation = async () => {
  const ip = ipList.value[0]
  if (!ip || firstIpLoaded.value) return
  ipLookupLoading.value = true
  try {
    const res = await fetch(`/api/ip-lookup?ip=${encodeURIComponent(ip)}`)
    const data = await res.json()
    if (data && !data.err) {
      ipLocations.value = { [ip]: data }
      firstIpLoaded.value = true
    }
  } catch {}
  ipLookupLoading.value = false
}

// 手动查询单个 IP（展开后点击按钮触发）
const querySingleIp = async (ip) => {
  if (ipLocations.value[ip]) return // 已有缓存
  ipLocations.value = { ...ipLocations.value, [ip]: { _loading: true } }
  try {
    const res = await fetch(`/api/ip-lookup?ip=${encodeURIComponent(ip)}`)
    const data = await res.json()
    if (data && !data.err) {
      ipLocations.value = { ...ipLocations.value, [ip]: data }
    } else {
      ipLocations.value = { ...ipLocations.value, [ip]: { _err: '无数据' } }
    }
  } catch {
    ipLocations.value = { ...ipLocations.value, [ip]: { _err: '查询失败' } }
  }
}

const formatIpLocation = (ip) => {
  const loc = ipLocations.value[ip]
  if (!loc || loc._loading) return ''
  if (loc._err) return loc._err
  const parts = []
  if (loc.pro) parts.push(loc.pro)
  if (loc.city && loc.city !== loc.pro) parts.push(loc.city)
  if (loc.addr) {
    const addr = loc.addr.replace(loc.pro, '').replace(loc.city || '', '').trim()
    if (addr) parts.push(addr)
  }
  return parts.join(' ') || '未知'
}

// 用户信息加载完成后自动查询第一个 IP
watch(userDetails, (val) => {
  if (val) queryFirstIpLocation()
})

const itemImageUrl = computed(() => {
  if (!giveSelectedItemId.value || giveSelectedItemId.value <= 0) return null
  return `/assets/img/img/Item_${giveSelectedItemId.value}.png`
})

const giveItemName = computed(() => {
  if (!giveSelectedItemId.value || giveSelectedItemId.value <= 0) return ''
  const itemInfo = itemData.value.dict[giveSelectedItemId.value.toString()]
  return itemInfo ? itemInfo.chinese || '' : ''
})

const giveItemEnglishName = computed(() => {
  if (!giveSelectedItemId.value || giveSelectedItemId.value <= 0) return ''
  const itemInfo = itemData.value.dict[giveSelectedItemId.value.toString()]
  return itemInfo ? itemInfo.english || '' : ''
})

const giveWikiImageUrl = computed(() => {
  if (!giveItemEnglishName.value) return ''
  const name = giveItemEnglishName.value.replace(/\s+/g, '_')
  return `https://terraria.wiki.gg/images/${name}.png`
})

const giveImageError = ref(false)

const handleGiveImageError = () => {
  giveImageError.value = true
}

const giveCurrentImageUrl = computed(() => {
  if (giveImageError.value && giveWikiImageUrl.value) {
    return giveWikiImageUrl.value
  }
  return itemImageUrl.value
})

const openGiveModal = () => {
  showGiveModal.value = true
  giveItemSearchQuery.value = ''
  giveSelectedItemId.value = 0
  giveAmount.value = 1
  givePrefixId.value = ''
  giveError.value = ''
  giveSuccess.value = ''
  showGiveItemDropdown.value = false
}

const closeGiveModal = () => {
  showGiveModal.value = false
}

const executeGive = async () => {
  if (!giveSelectedItemId.value || giveSelectedItemId.value <= 0) {
    giveError.value = '请选择有效的物品'
    return
  }

  giveLoading.value = true
  giveError.value = ''
  giveSuccess.value = ''
  giveWarning.value = ''

  const username = userDetails.value?.Username || userDetails.value?.name || route.params.username
  let command = `/give ${giveSelectedItemId.value} ${username} ${giveAmount.value}`
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
    antiCheatConfig.value = await getAntiCheatConfig()

    const response = await get(`/api/tshock/invsee?player=${encodeURIComponent(username)}`)
    const data = await response.json()

    if (data.status === '200' && data.inventory && data.inventory.inventory) {
      invseeInventory.value = data.inventory.inventory
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

const openPasswordModal = () => {
  showPasswordModal.value = true
  newPassword.value = ''
  passwordError.value = ''
  passwordSuccess.value = ''
  showPasswordText.value = false
}

const closePasswordModal = () => {
  showPasswordModal.value = false
}

const generateRandomPassword = () => {
  const chars = 'abcdefghijkmnopqrstuvwxyz0123456789'
  let password = ''
  for (let i = 0; i < 6; i++) {
    password += chars[Math.floor(Math.random() * chars.length)]
  }
  newPassword.value = password
  showPasswordText.value = true
}

const executePasswordChange = async () => {
  if (!userDetails.value) return
  
  if (!newPassword.value.trim()) {
    passwordError.value = '请输入新密码'
    return
  }

  passwordLoading.value = true
  passwordError.value = ''
  passwordSuccess.value = ''

  try {
    const username = userDetails.value.Username || userDetails.value.name
    const command = `/pwd ${username} ${newPassword.value}`

    const response = await post('/api/tshock/command', { command })
    const result = await response.json()

    if (result.error) {
      passwordError.value = result.error
    } else {
      passwordSuccess.value = result.response || '密码修改成功'
      newPassword.value = ''
    }
  } catch (err) {
    passwordError.value = err.message || '修改失败'
  }

  passwordLoading.value = false
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

const openClearCharacterModal = () => {
  showClearCharacterModal.value = true
  clearCharacterError.value = ''
  clearCharacterSuccess.value = ''
}

const closeClearCharacterModal = () => {
  showClearCharacterModal.value = false
}

const executeClearCharacter = async () => {
  if (!userDetails.value) return

  clearCharacterLoading.value = true
  clearCharacterError.value = ''
  clearCharacterSuccess.value = ''

  try {
    const account = userDetails.value.ID || userDetails.value.id
    const response = await post('/api/tshock/clearcharacter', { account })
    const result = await response.json()

    if (result.error) {
      clearCharacterError.value = result.error
    } else {
      clearCharacterSuccess.value = result.response || '角色数据已清空'
    }
  } catch (err) {
    clearCharacterError.value = err.message || '清空失败'
  }

  clearCharacterLoading.value = false
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

const openGroupModal = async () => {
  showGroupModal.value = true
  newGroup.value = userDetails.value?.Usergroup || userDetails.value?.group || ''
  groupError.value = ''
  groupSuccess.value = ''
  showGroupDropdown.value = false
  await fetchGroups()
}

const closeGroupModal = () => {
  showGroupModal.value = false
  showGroupDropdown.value = false
}

const toggleGroupDropdown = () => {
  showGroupDropdown.value = !showGroupDropdown.value
  if (showGroupDropdown.value) {
    updateDropdownPosition()
  }
}

const updateDropdownPosition = () => {
  const trigger = document.querySelector('.custom-select-trigger')
  if (trigger) {
    const rect = trigger.getBoundingClientRect()
    dropdownStyle.value = {
      position: 'fixed',
      top: `${rect.bottom + 4}px`,
      left: `${rect.left}px`,
      width: `${rect.width}px`,
      zIndex: 2000
    }
  }
}

const selectGroup = (group) => {
  newGroup.value = group
  showGroupDropdown.value = false
}

const executeGroupChange = async () => {
  if (!userDetails.value) return
  if (!newGroup.value.trim()) {
    groupError.value = '请选择或输入用户组'
    return
  }

  groupLoading.value = true
  groupError.value = ''
  groupSuccess.value = ''

  try {
    const username = userDetails.value.Username || userDetails.value.name
    const command = `/user group ${username} ${newGroup.value}`

    const response = await post('/api/tshock/command', { command })
    const result = await response.json()

    if (result.error) {
      groupError.value = result.error
    } else {
      groupSuccess.value = '用户组已更改'
      fetchUserDetails(username)
    }
  } catch (err) {
    groupError.value = err.message || '更改失败'
  }

  groupLoading.value = false
}

const openWhisperModal = () => {
  showWhisperModal.value = true
  whisperMessage.value = ''
  whisperError.value = ''
  whisperSuccess.value = ''
}

const closeWhisperModal = () => {
  showWhisperModal.value = false
}

const executeWhisper = async () => {
  if (!userDetails.value) return
  if (!whisperMessage.value.trim()) {
    whisperError.value = '请输入消息内容'
    return
  }

  whisperLoading.value = true
  whisperError.value = ''
  whisperSuccess.value = ''

  try {
    const username = userDetails.value.Username || userDetails.value.name
    const command = `/w ${username} ${whisperMessage.value}`

    const response = await post('/api/tshock/command', { command })
    const result = await response.json()

    if (result.error) {
      whisperError.value = result.error
    } else {
      whisperSuccess.value = '消息已发送'
      whisperMessage.value = ''
    }
  } catch (err) {
    whisperError.value = err.message || '发送失败'
  }

  whisperLoading.value = false
}

const openTpModal = () => {
  showTpModal.value = true
  const currentUsername = userDetails.value?.Username || userDetails.value?.name
  tpFromPlayer.value = currentUsername
  tpToPlayer.value = ''
  tpSearchQuery.value = ''
  tpSearchResults.value = []
  tpError.value = ''
  tpSuccess.value = ''
  loadOnlinePlayers()
}

const closeTpModal = () => {
  showTpModal.value = false
}

const openPlayerSelect = (target) => {
  tpSelectTarget.value = target
  tpSearchQuery.value = ''
  searchPlayers()
}

const swapTpPlayers = () => {
  const temp = tpFromPlayer.value
  tpFromPlayer.value = tpToPlayer.value
  tpToPlayer.value = temp
}

const loadOnlinePlayers = async () => {
  tpSearchLoading.value = true
  try {
    const response = await get('/api/tshock/activeusers')
    const result = await response.json()

    if (result.activeusers) {
      const names = result.activeusers.split('\t').filter(n => n.trim())
      onlinePlayers.value = names
      tpSearchResults.value = names.map(name => ({ Username: name, isOnline: true }))
    } else {
      tpSearchResults.value = []
    }
  } catch (err) {
    tpSearchResults.value = []
  }
  tpSearchLoading.value = false
}

const searchPlayers = async () => {
  if (!tpSearchQuery.value.trim()) {
    loadOnlinePlayers()
    return
  }

  tpSearchLoading.value = true
  try {
    const response = await get(`/api/tshock/userlist?username=${encodeURIComponent(tpSearchQuery.value)}`)
    const result = await response.json()

    if (result.status === '200' && result.users) {
      const onlineNames = onlinePlayers.value.map(p => p.toLowerCase())
      tpSearchResults.value = result.users.map(u => ({
        ...u,
        name: u.Username || u.name,
        isOnline: onlineNames.includes((u.Username || u.name).toLowerCase())
      })).slice(0, 10)
    } else {
      tpSearchResults.value = []
    }
  } catch (err) {
    tpSearchResults.value = []
  }
  tpSearchLoading.value = false
}

const selectTpPlayer = (player, target) => {
  if (target === 'from') {
    tpFromPlayer.value = player.name || player.Username
  } else {
    tpToPlayer.value = player.name || player.Username
  }
  tpSearchResults.value = []
  tpSearchQuery.value = ''
}

const isPlayerOnline = (playerName) => {
  return onlinePlayers.value.some(n => n.toLowerCase() === playerName.toLowerCase())
}

const executeTp = async () => {
  if (!tpFromPlayer.value || !tpToPlayer.value) {
    tpError.value = '请选择两个玩家'
    return
  }

  if (tpFromPlayer.value === tpToPlayer.value) {
    tpError.value = '不能传送自己到自己'
    return
  }

  tpLoading.value = true
  tpError.value = ''
  tpSuccess.value = ''

  try {
    const command = `/runas -f ${tpToPlayer.value} "/tphere ${tpFromPlayer.value}"`
    const response = await post('/api/tshock/command', { command })
    const result = await response.json()

    if (result.error) {
      tpError.value = result.error
    } else {
      tpSuccess.value = '传送成功'
    }
  } catch (err) {
    tpError.value = err.message || '传送失败'
  }

  tpLoading.value = false
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
    showToast('已复制到剪贴板')
  } catch (err) {
    console.error('复制失败:', err)
  }
}

// Toast 通知
const toastMessage = ref('')
let toastTimer = null

const showToast = (msg) => {
  toastMessage.value = msg
  if (toastTimer) clearTimeout(toastTimer)
  toastTimer = setTimeout(() => { toastMessage.value = '' }, 2000)
}

const copyQQ = () => {
  const qq = userDetails.value?.QQ
  if (qq) copyToClipboard(qq)
}

// ==================== 近十日在线统计 ====================
const toLocalDateString = (date) => {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

const dailyStartDate = ref(toLocalDateString(new Date(Date.now() - 4 * 86400000)))
const dailyData = ref([])
const dailyLoading = ref(false)
const dailyMaxMin = ref(1)
const totalMinutes = ref(0)
const dailyMode = ref('detail')
const todayStr = toLocalDateString(new Date())
const expandedDay = ref(todayStr)
const hourlyDetail = ref([])
const hourlyDetailLoading = ref(false)
const overviewData = ref([])
const overviewLoading = ref(false)
const overviewMaxMin = ref(1)
const overviewTotalMin = computed(() => overviewData.value.reduce((s, d) => s + d.minutes, 0))
const overviewActiveDays = computed(() => overviewData.value.filter(d => d.minutes > 0).length)
const axisLabels = computed(() => {
  const labels = []
  const data = overviewData.value
  if (!data.length) return labels
  const step = Math.max(1, Math.floor(data.length / 6))
  for (let i = 0; i < data.length; i += step) {
    labels.push(data[i].label)
  }
  if (labels.length && labels[labels.length - 1] !== data[data.length - 1].label) {
    labels.push(data[data.length - 1].label)
  }
  return labels
})

const formatDuration = (min) => {
  const h = Math.floor(min / 60)
  const m = min % 60
  if (h > 0 && m > 0) return `${h}h ${m}m`
  if (h > 0) return `${h}h`
  return `${m}m`
}

const fetchDailyStats = async (username) => {
  if (!username) return
  dailyLoading.value = true
  try {
    const res = await get(`/api/online/player?name=${encodeURIComponent(username)}&year=${new Date().getFullYear()}`)
    const json = await res.json()
    const dayMap = {}
    ;(json.days || []).forEach(d => { dayMap[d.date] = d.daily_min })

    const start = new Date(dailyStartDate.value + 'T00:00:00')
    const bars = []
    for (let i = 0; i < 5; i++) {
      const d = new Date(start)
      d.setDate(d.getDate() + i)
      const ds = toLocalDateString(d)
      bars.push({ date: ds, label: ds.slice(5), minutes: dayMap[ds] || 0 })
    }
    dailyData.value = bars
    dailyMaxMin.value = Math.max(1, ...bars.map(b => b.minutes))
    // 计算该年总游玩时长
    totalMinutes.value = Object.values(dayMap).reduce((s, v) => s + v, 0)
  } catch (e) {
    dailyData.value = []
    dailyMaxMin.value = 1
  }
  dailyLoading.value = false
  // auto-expand today after fetching
  if (expandedDay.value === todayStr) fetchHourlyDetail(todayStr)
}

const shiftDaily = (offset) => {
  const d = new Date(dailyStartDate.value + 'T00:00:00')
  d.setDate(d.getDate() + offset)
  // 不能超过今天-4（最后一天不能超过今天）
  const maxStart = new Date()
  maxStart.setDate(maxStart.getDate() - 4)
  maxStart.setHours(0, 0, 0, 0)
  if (d > maxStart) d.setTime(maxStart.getTime())
  dailyStartDate.value = toLocalDateString(d)
}

const dailyPct = (min) => {
  if (dailyMaxMin.value === 0) return 0
  return Math.round((min / dailyMaxMin.value) * 100)
}

const dailyColorClass = (min) => {
  if (min === 0) return 'level-0'
  if (min <= 30) return 'level-1'
  if (min <= 120) return 'level-2'
  if (min <= 300) return 'level-3'
  return 'level-4'
}

// 日期校验
const validateDate = (dateStr) => {
  const d = new Date(dateStr + 'T00:00:00')
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  if (d > today) {
    alert('不能选择未来的日期')
    return false
  }
  const registered = userDetails.value?.Registered
  if (registered) {
    const regDate = new Date(registered)
    regDate.setHours(0, 0, 0, 0)
    if (d < regDate) {
      alert('不能早于注册日期 (' + toLocalDateString(regDate) + ')')
      return false
    }
  }
  return true
}

// 切换模式
const toggleMode = () => {
  dailyMode.value = dailyMode.value === 'overview' ? 'detail' : 'overview'
  const username = userDetails.value?.Username || userDetails.value?.name
  if (dailyMode.value === 'overview' && username) fetchOverview()
}

// 加载30天总览数据
const fetchOverview = async () => {
  const username = userDetails.value?.Username || userDetails.value?.name
  if (!username) return
  overviewLoading.value = true
  try {
    const res = await get(`/api/online/player?name=${encodeURIComponent(username)}&year=${new Date().getFullYear()}`)
    const json = await res.json()
    const dayMap = {}
    ;(json.days || []).forEach(d => { dayMap[d.date] = d.daily_min })
    const today = new Date()
    const days = []
    for (let i = 29; i >= 0; i--) {
      const d = new Date(today)
      d.setDate(d.getDate() - i)
      const ds = toLocalDateString(d)
      days.push({ date: ds, label: ds.slice(5), minutes: dayMap[ds] || 0 })
    }
    overviewData.value = days
    overviewMaxMin.value = Math.max(1, ...days.map(d => d.minutes))
  } catch (e) { overviewData.value = [] }
  overviewLoading.value = false
}

// 点击总览某天→切换到详情模式
const overviewClickDay = (dateStr) => {
  dailyMode.value = 'detail'
  expandedDay.value = dateStr
  const d = new Date(dateStr + 'T00:00:00')
  d.setDate(d.getDate() - 2)
  const registered = userDetails.value?.Registered
  if (registered && d < new Date(registered + 'T00:00:00')) {
    d.setTime(new Date(registered + 'T00:00:00').getTime())
  }
  dailyStartDate.value = toLocalDateString(d)
  fetchHourlyDetail(dateStr)
}

// 展开某天的逐时数据
const expandDay = (dateStr) => {
  expandedDay.value = dateStr
  fetchHourlyDetail(dateStr)
}

// 加载某天的逐时在线数据
const fetchHourlyDetail = async (dateStr) => {
  hourlyDetailLoading.value = true
  try {
    const res = await get(`/api/online/hourly?date=${dateStr}`)
    const json = await res.json()
    hourlyDetail.value = json.hours || []
  } catch (e) { hourlyDetail.value = [] }
  hourlyDetailLoading.value = false
}

const isPlayerOnlineAtHour = (dateStr, hour) => {
  const targetTs = parseInt(dateStr.replace(/-/g, '')) * 100 + hour
  const h = hourlyDetail.value.find(h => h.hour_ts === targetTs)
  if (!h || !h.online_players) return false
  const username = userDetails.value?.Username || userDetails.value?.name
  return h.online_players.some(n => n.toLowerCase() === username?.toLowerCase())
}

watch(dailyStartDate, (newVal) => {
  if (!validateDate(newVal)) {
    const today = new Date()
    today.setDate(today.getDate() - 4)
    dailyStartDate.value = toLocalDateString(today)
    return
  }
  const username = userDetails.value?.Username || userDetails.value?.name
  if (username) fetchDailyStats(username)
})

const fetchPlayerStats = async (username) => {
  statsLoading.value = true
  statsError.value = ''

  try {
    const response = await get(`/api/tshock/stats?player=${encodeURIComponent(username)}`)
    const result = await response.json()

    if (result.status === '200' && result.data) {
      playerStats.value = result.data
      tempStats.value = {
        maxHealth: result.data.maxHealth || 400,
        maxMana: result.data.maxMana || 200,
        questsCompleted: result.data.questsCompleted || 0,
        extraSlot: result.data.extraSlot || false,
        unlockedBiomeTorches: result.data.unlockedBiomeTorches || false,
        ateArtisanBread: result.data.ateArtisanBread || false,
        usedAegisCrystal: result.data.usedAegisCrystal || false,
        usedAegisFruit: result.data.usedAegisFruit || false,
        usedArcaneCrystal: result.data.usedArcaneCrystal || false,
        usedGalaxyPearl: result.data.usedGalaxyPearl || false,
        usedGummyWorm: result.data.usedGummyWorm || false,
        usedAmbrosia: result.data.usedAmbrosia || false,
        unlockedSuperCart: result.data.unlockedSuperCart || false,
        enabledSuperCart: result.data.enabledSuperCart || false
      }
    } else if (result.error) {
      statsError.value = result.error
    }
  } catch (err) {
    if (err.message !== 'Unauthorized') {
      statsError.value = 'Error: ' + err.message
    }
  }

  statsLoading.value = false
}

const savePlayerStats = async () => {
  statsSaving.value = true
  statsSaveSuccess.value = ''
  statsSaveError.value = ''

  const username = userDetails.value?.Username || userDetails.value?.name || route.params.username

  try {
    const response = await post('/api/tshock/stats/set', {
      player: username,
      ...tempStats.value
    })
    const result = await response.json()

    if (result.error) {
      statsSaveError.value = result.error
    } else {
      statsSaveSuccess.value = result.response || '保存成功'
      await fetchPlayerStats(username)
      await checkOnlineStatus()
    }
  } catch (err) {
    statsSaveError.value = err.message || '保存失败'
  }

  statsSaving.value = false
}

const refreshPlayerStats = () => {
  const username = userDetails.value?.Username || userDetails.value?.name
  if (username) fetchPlayerStats(username)
}

const showImportExportModal = ref(false)
const importExportTab = ref('export')
const importJson = ref('')
const importParsedData = ref(null)
const importError = ref('')
const importLoading = ref(false)
const importSuccess = ref('')
const importConfirmData = ref(null)
const importClearUnspecified = ref(false)
const presetExportOpen = ref(false)
const presetImportOpen = ref(false)
const selectedImportMethod = ref('')

const selectImportMethod = (method) => {
  selectedImportMethod.value = method
  if (method === 'preset') {
    loadPresetList()
  }
}

const handleDropFile = (e) => {
  const file = e.dataTransfer?.files?.[0]
  if (!file) return
  const reader = new FileReader()
  reader.onload = (ev) => {
    importJson.value = ev.target?.result || ''
    parseImportData()
  }
  reader.readAsText(file)
}

const buildExportData = () => {
  const username = userDetails.value?.Username || userDetails.value?.name || 'unknown'
  return {
    exportedAt: new Date().toISOString(),
    stats: { ...tempStats.value },
    inventory: invseeInventory.value.filter(i => i && i.netID && i.netID > 0).map((i, idx) => ({
      slot: i.slot !== undefined ? i.slot : idx,
      netId: i.netID,
      stack: i.stack,
      prefix: i.prefix
    }))
  }
}

const exportPlayerDataCopy = () => {
  const data = buildExportData()
  copyToClipboard(JSON.stringify(data, null, 2))
}

const exportPlayerDataDownload = () => {
  const data = buildExportData()
  const json = JSON.stringify(data, null, 2)
  const username = userDetails.value?.Username || userDetails.value?.name || 'unknown'
  const blob = new Blob([json], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${username}.json`
  a.click()
  URL.revokeObjectURL(url)
}

const switchImportExportTab = (tab) => {
  importExportTab.value = tab
  importJson.value = ''
  importParsedData.value = null
  importError.value = ''
  importSuccess.value = ''
  importConfirmData.value = null
}

const handleImportFileUpload = (e) => {
  const file = e.target.files?.[0]
  if (!file) return
  const reader = new FileReader()
  reader.onload = (ev) => {
    importJson.value = ev.target?.result || ''
    parseImportData()
  }
  reader.readAsText(file)
  e.target.value = ''
}

const parseImportData = () => {
  importError.value = ''
  importParsedData.value = null
  importConfirmData.value = null
  importSuccess.value = ''
  if (!importJson.value.trim()) return
  try {
    const data = JSON.parse(importJson.value)
    if (!data.stats && !data.inventory) {
      importError.value = 'JSON 格式无效：缺少 stats 或 inventory 字段'
      return
    }
    if (!data.exportedAt) {
      importError.value = 'JSON 格式无效：缺少导出时间 exportedAt'
      return
    }
    const exportDate = new Date(data.exportedAt)
    if (isNaN(exportDate.getTime())) {
      importError.value = '导出时间格式无效'
      return
    }
    const daysAgo = Math.floor((Date.now() - exportDate.getTime()) / 86400000)
    importConfirmData.value = {
      hasStats: !!data.stats,
      hasInventory: !!(data.inventory && data.inventory.length > 0),
      itemCount: data.inventory?.length || 0,
      exportedAt: data.exportedAt,
      daysAgo
    }
    importParsedData.value = data
  } catch {
    importError.value = 'JSON 解析失败，请检查格式'
  }
}

// === 预设管理 ===
const presetList = ref([])
const presetLoading = ref(false)
const presetName = ref('')
const presetSaving = ref(false)
const presetSaveError = ref('')
const presetSaveSuccess = ref('')
const presetsLoaded = ref(false)

const exportAsPreset = async () => {
  const name = presetName.value.trim()
  if (!name) {
    presetSaveError.value = '请输入预设名称'
    return
  }
  presetSaving.value = true
  presetSaveError.value = ''
  presetSaveSuccess.value = ''
  try {
    const data = buildExportData()
    const res = await fetch('/api/presets/save', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, data })
    })
    const result = await res.json()
    if (result.success) {
      presetSaveSuccess.value = `预设「${result.name}」已保存`
      presetName.value = ''
      loadPresetList()
    } else {
      presetSaveError.value = result.error || '保存失败'
    }
  } catch (err) {
    presetSaveError.value = '保存失败: ' + err.message
  }
  presetSaving.value = false
}

const loadPresetList = async () => {
  presetLoading.value = true
  try {
    const res = await fetch('/api/presets/list')
    const result = await res.json()
    presetList.value = result.presets || []
    presetsLoaded.value = true
  } catch (err) {
    console.error('加载预设列表失败:', err)
  }
  presetLoading.value = false
}

const loadPreset = async (name) => {
  try {
    const res = await fetch(`/api/presets/read?name=${encodeURIComponent(name)}`)
    const result = await res.json()
    if (result.success) {
      importJson.value = JSON.stringify(result.data, null, 2)
      parseImportData()
    } else {
      importError.value = result.error || '读取预设失败'
    }
  } catch (err) {
    importError.value = '读取预设失败: ' + err.message
  }
}

const deletePreset = async (name) => {
  try {
    const res = await fetch(`/api/presets/delete?name=${encodeURIComponent(name)}`, { method: 'DELETE' })
    const result = await res.json()
    if (result.success) {
      loadPresetList()
    }
  } catch (err) {
    console.error('删除预设失败:', err)
  }
}

const executeImport = async () => {
  if (!importParsedData.value) return
  importLoading.value = true
  importError.value = ''
  importSuccess.value = ''
  const username = userDetails.value?.Username || userDetails.value?.name
  if (!username) {
    importError.value = '无法获取玩家名'
    importLoading.value = false
    return
  }
  try {
    const data = {
      stats: importParsedData.value.stats || {},
      inventory: importParsedData.value.inventory || []
    }
    const res = await post('/api/tshock/batch-edit', {
      player: username,
      data,
      clearUnspecified: importClearUnspecified.value
    })
    const result = await res.json()
    if (result.error) {
      importError.value = '导入失败: ' + result.error
    } else {
      importSuccess.value = result.response || '导入成功'
      fetchInventory(username)
      fetchPlayerStats(username)
    }
  } catch (err) {
    importError.value = '导入失败: ' + err.message
  }
  importLoading.value = false
}

const refreshInventory = () => {
  const username = userDetails.value?.Username || userDetails.value?.name
  if (username) fetchInventory(username)
}

const goBack = () => {
  router.push('/console/players')
}

const goToUser = (username) => {
  router.push(`/console/users/${username}`)
}

watch(() => route.params.username, (newUsername) => {
  if (newUsername) {
    fetchUserDetails(newUsername)
    fetchInventory(newUsername)
    fetchPlayerStats(newUsername)
    fetchDailyStats(newUsername)
    checkOnlineStatus()
  }
}, { immediate: true })

onMounted(() => {
  initItemData()
  if (route.params.username) {
    fetchUserDetails(route.params.username)
    fetchInventory(route.params.username)
    fetchPlayerStats(route.params.username)
    fetchDailyStats(route.params.username)
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
          <div class="qq-bind-status" :class="{ bound: userDetails.QQ }" @click="copyQQ" :title="userDetails.QQ ? '点击复制 QQ 号' : ''">
            <span class="qq-dot"></span>
            <span v-if="userDetails.QQ" class="qq-number">QQ:{{ userDetails.QQ }}</span>
            <span v-else>QQ:未绑定</span>
          </div>
        </template>
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
            <dd class="group-clickable" @click="openGroupModal">
              {{ userDetails.Usergroup || userDetails.group }}
              <span class="change-hint">点击修改</span>
            </dd>
          </div>
          <div class="info-item">
            <dt>UUID</dt>
            <dd class="uuid-box" @click="copyToClipboard(userDetails.UUID)" title="点击复制完整UUID">
              <span class="uuid-text">{{ (userDetails.UUID || '未知').substring(0, 5) }}</span>
            </dd>
          </div>
          <div class="info-item">
            <dt>注册时间</dt>
            <dd>{{ userDetails.Registered ? formatLocalDate(userDetails.Registered) : '未知' }}</dd>
          </div>
          <div class="info-item">
            <dt>最后访问</dt>
            <dd>{{ userDetails.LastAccessed ? formatLocalDate(userDetails.LastAccessed) : '从未访问' }}</dd>
          </div>
          <div class="info-item">
            <dt>已知IP</dt>
            <dd class="ip-display">
              <div class="ip-summary" @click="showIpPopup = !showIpPopup">
                <span class="ip-text">{{ firstIP }}</span>
                <span v-if="ipLocations[firstIP] && !ipLocations[firstIP]._loading && !ipLocations[firstIP]._err" class="ip-loc-badge">{{ formatIpLocation(firstIP) }}</span>
                <span v-if="ipList.length > 1" class="ip-count-badge">+{{ ipList.length - 1 }}</span>
                <svg class="ip-chevron" :class="{ open: showIpPopup }" viewBox="0 0 20 20" fill="currentColor" width="14" height="14">
                  <path fill-rule="evenodd" d="M5.293 7.293a1 1 0 011.414 0L10 10.586l3.293-3.293a1 1 0 111.414 1.414l-4 4a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414z" clip-rule="evenodd"/>
                </svg>
              </div>
              <!-- IP 弹出卡片 -->
              <Transition name="ip-fade">
                <div v-if="showIpPopup" class="ip-popover" @click.stop>
                  <div class="ip-popover-arrow"></div>
                  <div class="ip-popover-header">
                    <span>所有已知 IP</span>
                    <span class="ip-popover-count">{{ ipList.length }} 个</span>
                  </div>
                  <div class="ip-popover-list">
                    <div v-for="(ip, idx) in ipList" :key="idx" class="ip-popover-item">
                      <span class="ip-popover-dot"></span>
                      <div class="ip-popover-info">
                        <span class="ip-popover-addr">{{ ip }}</span>
                        <span v-if="ipLocations[ip] && ipLocations[ip]._loading" class="ip-popover-location loading">查询中...</span>
                        <span v-else-if="ipLocations[ip] && !ipLocations[ip]._err" class="ip-popover-location">{{ formatIpLocation(ip) }}</span>
                        <span v-else-if="ipLocations[ip] && ipLocations[ip]._err" class="ip-popover-location error">{{ ipLocations[ip]._err }}</span>
                      </div>
                      <button v-if="!ipLocations[ip] || ipLocations[ip]._err" class="ip-lookup-btn" @click="querySingleIp(ip)" title="查询地理位置">查询</button>
                    </div>
                    <div v-if="ipList.length === 0" class="ip-popover-empty">无 IP 记录</div>
                  </div>
                </div>
              </Transition>
              <!-- 点击外部关闭的透明层 -->
              <div v-if="showIpPopup" class="ip-backdrop" @click="showIpPopup = false"></div>
            </dd>
          </div>
        </dl>
      </div>

      <div class="detail-grid">
        <div class="detail-left">
      <div class="action-section">
        <div class="action-group">
          <h4 class="group-label">管理操作</h4>
          <div class="group-buttons">
            <button @click="openPasswordModal" class="password-btn">
              改密码
            </button>
            <button @click="openKickModal" :disabled="!isOnline" class="kick-btn" :class="{ disabled: !isOnline }">
              踢出
            </button>
            <button @click="openBanModal" class="ban-btn">
              封禁
            </button>
            <button @click="openClearCharacterModal" class="danger-btn">
              清空角色
            </button>
            <button
              @click="checkDuplicateIPs"
              :disabled="duplicateIPLoading"
              class="duplicate-check-btn"
            >
              {{ duplicateIPLoading ? '检测中...' : '检测关联账号' }}
            </button>
          </div>
        </div>
        
          <div class="action-group">
          <h4 class="group-label">互动操作</h4>
          <div class="group-buttons">
              <button @click="openTpModal" :disabled="!isOnline" class="tp-btn" :class="{ disabled: !isOnline }">
                传送
              </button>
              <button @click="openWhisperModal" :disabled="!isOnline" class="whisper-btn" :class="{ disabled: !isOnline }">
                私聊
              </button>
              <button @click="openGiveModal" class="give-btn">
                给物品
              </button>
            </div>
          </div>
        </div>
      </div>

        <div class="detail-right">
      <div class="daily-stats-section">
        <div class="daily-stats-header">
          <div class="daily-header-left">
            <button @click="toggleMode" class="mode-toggle-btn" :title="dailyMode === 'overview' ? '切换到详情' : '切换到总览'">
              {{ dailyMode === 'overview' ? '总览' : '详情' }}
            </button>
          </div>
          <h3>{{ dailyMode === 'overview' ? '30日在线趋势' : '近5日在线' }}</h3>
          <div class="playtime-header-badge" v-if="totalMinutes > 0">总游玩 <strong>{{ formatDuration(totalMinutes) }}</strong></div>
          <div class="daily-nav" v-if="dailyMode === 'detail'">
            <button @click="shiftDaily(-5)" class="daily-nav-btn" title="往前5天">◀</button>
            <input type="date" v-model="dailyStartDate" class="filter-date" />
            <button @click="shiftDaily(5)" class="daily-nav-btn" title="往后5天">▶</button>
          </div>
        </div>

        <!-- 总览模式 -->
        <div v-if="dailyMode === 'overview'" class="daily-stats-body">
          <div v-if="overviewLoading" class="loading-state"><p>加载中...</p></div>
          <div v-else class="overview-chart-container">
            <div class="overview-summary">
                  <span>近30天累计: <strong>{{ formatDuration(overviewTotalMin) }}</strong> | 活跃天数: {{ overviewActiveDays }}/30</span>
            </div>
            <div class="overview-chart">
              <div
                v-for="day in overviewData"
                :key="day.date"
                class="overview-col"
                :class="dailyColorClass(day.minutes)"
                @click="overviewClickDay(day.date)"
              >
                <div
                  class="overview-bar"
                  :style="{ height: overviewMaxMin > 0 ? Math.max(2, (day.minutes / overviewMaxMin) * 48) + 'px' : '2px' }"
                  :title="`${day.label}: ${formatDuration(day.minutes)}`"
                ></div>
              </div>
            </div>
            <div class="overview-axis">
              <span v-for="label in axisLabels" :key="label" class="axis-label">{{ label }}</span>
            </div>
          </div>
        </div>

        <!-- 详情模式 -->
        <div v-else class="daily-stats-body">
          <div v-if="dailyLoading" class="loading-state"><p>加载中...</p></div>
          <template v-else>
            <div class="daily-cards">
              <div
                v-for="day in dailyData"
                :key="day.date"
                class="daily-card"
                :class="[dailyColorClass(day.minutes), { expanded: expandedDay === day.date }]"
                @click="expandDay(day.date)"
              >
                <span class="daily-card-date">{{ day.label }}</span>
                <span class="daily-card-bar">
                  <span class="daily-card-fill" :style="{ height: dailyPct(day.minutes) + '%' }"></span>
                </span>
                <span class="daily-card-min">{{ formatDuration(day.minutes) }}</span>
              </div>
            </div>
            <div v-if="expandedDay" class="hourly-detail">
              <div class="hourly-detail-head">
                <span>{{ expandedDay }} 逐时在线</span>
              </div>
              <div v-if="hourlyDetailLoading" class="loading-state"><p>加载中...</p></div>
              <div v-else class="hourly-mini-chart">
                <div v-for="h in 24" :key="h-1" class="hourly-mini-col">
                  <div
                    class="hourly-mini-bar"
                    :class="{ online: isPlayerOnlineAtHour(expandedDay, h-1) }"
                    :title="`${String(h-1).padStart(2,'0')}:00`"
                  ></div>
                  <span class="hourly-mini-label">{{ h-1 }}</span>
                </div>
              </div>
            </div>
          </template>
        </div>
          </div>
        </div>
      </div>

      <div class="stats-section">
        <div class="stats-header">
          <h3>角色属性</h3>
          <div class="export-group" v-if="playerStats">
            <button @click="showImportExportModal = true" class="export-btn" title="导入/导出角色数据">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>
              导入/导出
            </button>
          </div>
        </div>
        
        <div v-if="statsLoading" class="loading-state">
          <p>加载中...</p>
        </div>
        
        <div v-else-if="statsError" class="error-state">
          <p>{{ statsError }}</p>
        </div>
        
        <div v-else-if="playerStats" class="stats-content">
          <div class="stats-row">
            <div class="stat-item">
              <label>血量上限</label>
              <input
                v-model.number="tempStats.maxHealth"
                type="number"
                min="100"
                max="600"
                class="stat-input"
              />
            </div>
            <div class="stat-item">
              <label>魔力上限</label>
              <input
                v-model.number="tempStats.maxMana"
                type="number"
                min="20"
                max="200"
                class="stat-input"
              />
            </div>
            <div class="stat-item">
              <label>完成任务数</label>
              <input
                v-model.number="tempStats.questsCompleted"
                type="number"
                min="0"
                class="stat-input"
              />
            </div>
          </div>
          
          <div class="enhances-section">
            <h4 class="enhances-title">角色永久增益</h4>
            <div class="enhances-grid">
              <div
                v-for="(label, key) in enhancesMap"
                :key="key"
                class="enhance-item"
              >
                <label class="enhance-label">
                  <input
                    v-model="tempStats[key]"
                    type="checkbox"
                    class="enhance-checkbox"
                  />
                  <span>{{ label }}</span>
                </label>
              </div>
            </div>
          </div>
          
          <div class="stats-actions">
            <button @click="refreshPlayerStats" :disabled="statsLoading" class="refresh-stats-btn">
              {{ statsLoading ? '刷新中...' : '刷新' }}
            </button>
            <button @click="savePlayerStats" :disabled="statsSaving" class="save-stats-btn">
              {{ statsSaving ? '保存中...' : '保存属性' }}
            </button>
            <div v-if="statsSaveSuccess" class="stats-success">
              {{ statsSaveSuccess }}
            </div>
            <div v-if="statsSaveError" class="stats-error">
              {{ statsSaveError }}
            </div>
          </div>
        </div>
      </div>

      <div class="inventory-section">
        <div class="inventory-header">
          <h3>背包信息</h3>
          <button @click="refreshInventory" :disabled="invseeLoading" class="refresh-btn">
            {{ invseeLoading ? '刷新中...' : '刷新' }}
          </button>
        </div>
        <div v-if="anomalyItems.length > 0" class="anomaly-warning">
          <div class="anomaly-title">⚠️ 异常物品检测</div>
          <div v-for="(item, index) in anomalyItems" :key="index" class="anomaly-item">
            {{ item.name }}: {{ item.stack }}个
          </div>
        </div>
        <InventoryViewer
          :inventory="invseeInventory"
          :loading="invseeLoading"
          :error="invseeError"
          :initial-username="route.params.username"
          :show-header="false"
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
                @keyup.enter="executeKick"
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

    <div v-if="showGroupModal" class="modal-overlay" @click.self="closeGroupModal">
      <div class="modal">
        <div class="modal-header">
          <h3>修改用户组</h3>
          <button @click="closeGroupModal" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="group-form">
            <div class="form-row">
              <label>选择新用户组</label>
              <div class="custom-select-wrapper">
                <div 
                  class="custom-select-trigger" 
                  @click="toggleGroupDropdown"
                >
                  <span class="select-value">{{ newGroup || '请选择用户组' }}</span>
                  <span class="select-arrow" :class="{ rotated: showGroupDropdown }">▼</span>
                </div>
              </div>
            </div>
          </div>
          
          <Teleport to="body">
            <div 
              v-if="showGroupDropdown && showGroupModal" 
              class="custom-select-dropdown-teleport"
              :style="dropdownStyle"
            >
              <div 
                v-for="group in availableGroups" 
                :key="group"
                class="custom-select-option"
                :class="{ selected: newGroup === group }"
                @click="selectGroup(group)"
              >
                {{ group }}
              </div>
            </div>
          </Teleport>
          <div v-if="groupError" class="give-error">
            {{ groupError }}
          </div>
          <div v-if="groupSuccess" class="give-success">
            {{ groupSuccess }}
          </div>
        </div>
        <div class="modal-footer">
          <button @click="closeGroupModal" class="cancel-btn">取消</button>
          <button @click="executeGroupChange" :disabled="groupLoading" class="submit-btn">
            {{ groupLoading ? '修改中...' : '确认修改' }}
          </button>
        </div>
      </div>
    </div>

    <div v-if="showWhisperModal" class="modal-overlay" @click.self="closeWhisperModal">
      <div class="modal">
        <div class="modal-header">
          <h3>私聊玩家</h3>
          <button @click="closeWhisperModal" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="whisper-form">
            <div class="form-row">
              <label>消息内容</label>
              <textarea
                v-model="whisperMessage"
                placeholder="输入要发送的消息"
                class="form-input textarea"
                rows="3"
                @keyup.enter="executeWhisper"
              ></textarea>
            </div>
          </div>
          <div v-if="whisperError" class="give-error">
            {{ whisperError }}
          </div>
          <div v-if="whisperSuccess" class="give-success">
            {{ whisperSuccess }}
          </div>
        </div>
        <div class="modal-footer">
          <button @click="closeWhisperModal" class="cancel-btn">取消</button>
          <button @click="executeWhisper" :disabled="whisperLoading" class="submit-btn">
            {{ whisperLoading ? '发送中...' : '发送' }}
          </button>
        </div>
      </div>
    </div>

    <div v-if="showTpModal" class="modal-overlay" @click.self="closeTpModal">
      <div class="modal tp-modal">
        <div class="modal-header">
          <h3>传送玩家</h3>
          <button @click="closeTpModal" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="tp-direction-section">
            <div class="tp-player-box" @click="openPlayerSelect('from')">
              <span class="tp-player-name">{{ tpFromPlayer || '点击选择' }}</span>
              <span class="tp-player-label">被传送者</span>
            </div>
            <button @click="swapTpPlayers" class="tp-swap-btn" title="互换">
              ⇄
            </button>
            <div class="tp-player-box" @click="openPlayerSelect('to')">
              <span class="tp-player-name">{{ tpToPlayer || '点击选择' }}</span>
              <span class="tp-player-label">目标位置</span>
            </div>
          </div>
          <div class="tp-direction-text">
            {{ tpFromPlayer || '?' }} → {{ tpToPlayer || '?' }}
          </div>
          <div class="tp-form">
            <div class="form-row">
              <label>搜索玩家</label>
              <input
                v-model="tpSearchQuery"
                type="text"
                placeholder="输入玩家名搜索"
                class="form-input"
                @input="searchPlayers"
              />
              <div v-if="tpSearchLoading" class="tp-search-loading">
                加载中...
              </div>
              <div v-if="tpSearchResults.length > 0" class="tp-search-results">
                <div
                  v-for="player in tpSearchResults"
                  :key="player.Username || player.name"
                  class="tp-search-item"
                  :class="{ offline: !player.isOnline }"
                  @click="player.isOnline && selectTpPlayer(player, tpSelectTarget)"
                >
                  <span class="tp-search-status" :class="{ online: player.isOnline }"></span>
                  <span class="tp-search-name">{{ player.Username || player.name }}</span>
                  <span class="tp-search-group">{{ player.Usergroup || player.group || '默认' }}</span>
                </div>
              </div>
            </div>
          </div>
          <div v-if="tpError" class="give-error">
            {{ tpError }}
          </div>
          <div v-if="tpSuccess" class="give-success">
            {{ tpSuccess }}
          </div>
        </div>
        <div class="modal-footer">
          <button @click="closeTpModal" class="cancel-btn">取消</button>
          <button @click="executeTp" :disabled="tpLoading || !tpToPlayer" class="submit-btn">
            {{ tpLoading ? '传送中...' : '确认传送' }}
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
                @keyup.enter="executeBan"
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

    <div v-if="showClearCharacterModal" class="modal-overlay" @click.self="closeClearCharacterModal">
      <div class="modal modal-danger">
        <div class="modal-header">
          <h3>⚠️ 清空角色数据</h3>
          <button @click="closeClearCharacterModal" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="ban-warning">
            <p>🚨 <strong>危险操作</strong></p>
            <p>此操作将 <strong>永久删除</strong> 该玩家在服务器的角色数据（背包、装备、建筑权限等）。</p>
            <p>玩家下次登录时将以全新角色进入，此操作 <strong>不可撤销</strong>！</p>
          </div>
          <div class="clear-char-form">
            <div class="form-row">
              <label>目标玩家 ID</label>
              <input
                :value="userDetails?.ID || userDetails?.id"
                type="text"
                class="form-input"
                disabled
              />
            </div>
            <div class="form-row">
              <label>目标玩家</label>
              <input
                :value="userDetails?.Username || userDetails?.name"
                type="text"
                class="form-input"
                disabled
              />
            </div>
          </div>
          <div v-if="clearCharacterError" class="give-error">
            {{ clearCharacterError }}
          </div>
          <div v-if="clearCharacterSuccess" class="give-success">
            {{ clearCharacterSuccess }}
          </div>
        </div>
        <div class="modal-footer">
          <button @click="closeClearCharacterModal" class="cancel-btn">取消</button>
          <button @click="executeClearCharacter" :disabled="clearCharacterLoading" class="danger-submit-btn">
            {{ clearCharacterLoading ? '执行中...' : '确认清空角色数据' }}
          </button>
        </div>
      </div>
    </div>

    <div v-if="showPasswordModal" class="modal-overlay" @click.self="closePasswordModal">
      <div class="modal">
        <div class="modal-header">
          <h3>修改密码</h3>
          <button @click="closePasswordModal" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div class="password-form">
            <div class="form-row">
              <label>新密码</label>
              <div class="password-input-wrapper">
                <div class="password-input-inner">
                  <input
                    v-model="newPassword"
                :type="showPasswordText ? 'text' : 'password'"
                    placeholder="输入新密码"
                    class="form-input"
                    @keyup.enter="executePasswordChange"
                  />
                  <button
                    class="toggle-password-btn"
                    @click="showPasswordText = !showPasswordText"
                    :title="showPasswordText ? '隐藏密码' : '显示密码'"
                    type="button"
                  >
                    {{ showPasswordText ? '隐藏' : '显示' }}
                  </button>
                </div>
                <span class="generate-pwd-badge" @click="generateRandomPassword">
                  生成随机密码
                </span>
              </div>
            </div>
          </div>
          <div v-if="passwordError" class="give-error">
            {{ passwordError }}
          </div>
          <div v-if="passwordSuccess" class="give-success">
            {{ passwordSuccess }}
          </div>
        </div>
        <div class="modal-footer">
          <button @click="closePasswordModal" class="cancel-btn">取消</button>
          <button @click="executePasswordChange" :disabled="passwordLoading" class="submit-btn">
            {{ passwordLoading ? '修改中...' : '确认修改' }}
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
              <label>物品</label>
              <div class="item-input-row">
                <div class="search-input-wrapper">
                  <input
                    v-model="giveItemSearchQuery"
                    type="text"
                    placeholder="搜索物品（名称或ID）..."
                    class="form-input"
                    @input="showGiveItemDropdown = true"
                    @focus="showGiveItemDropdown = true"
                    @blur="setTimeout(() => showGiveItemDropdown = false, 150)"
                  />
                  <div v-if="showGiveItemDropdown && giveItemSearchResults.length > 0" class="item-dropdown">
                    <div
                      v-for="item in giveItemSearchResults"
                      :key="item.id"
                      class="item-option"
                      @click="selectGiveItem(item)"
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
                  <div v-if="giveCurrentImageUrl" class="item-preview">
                    <img
                      :src="giveCurrentImageUrl"
                      :alt="giveItemName"
                      class="item-image"
                      @error="handleGiveImageError"
                    />
                  </div>
                  <div v-if="giveItemName" class="item-name">
                    {{ giveItemName }}
                  </div>
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

    <!-- 关联账号检测结果弹窗 -->
    <div v-if="duplicateIPResult" class="modal-overlay" @click.self="resetDuplicateIPResult">
      <div class="modal">
        <div class="modal-header">
          <h3>关联账号检测</h3>
          <button @click="resetDuplicateIPResult" class="close-btn">×</button>
        </div>
        <div class="modal-body">
          <div v-if="duplicateIPResult.error" class="result-error">
            <p>❌ {{ duplicateIPResult.error }}</p>
          </div>

          <div v-else-if="duplicateIPResult.count === 0" class="result-empty">
            <p>✅ 未发现关联账号</p>
          </div>

          <div v-else class="result-content">
            <h4 class="result-title">关联账号组 (共 {{ duplicateIPResult.totalAccounts }} 个账号)</h4>
            <div class="shared-ips-section">
              <span class="shared-ips-label">共享IP:</span>
              <span
                v-for="(ip, idx) in duplicateIPResult.sharedIPs"
                :key="idx"
                class="shared-ip-chip"
              >
                {{ ip }}
              </span>
            </div>
            <div class="duplicate-list">
              <div
                v-for="user in duplicateIPResult.duplicates"
                :key="user.ID"
                class="duplicate-item"
                @click="goToUser(user.Username)"
              >
                <span class="duplicate-id">ID: {{ user.ID }}</span>
                <span class="duplicate-name">{{ user.Username }}</span>
              </div>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button @click="resetDuplicateIPResult" class="cancel-btn">关闭</button>
        </div>
      </div>
    </div>
  </div>

    <!-- 导入/导出弹窗 -->
    <div v-if="showImportExportModal" class="modal-overlay" @click.self="showImportExportModal = false">
      <div class="modal ie-modal">
        <div class="modal-header">
          <h3>角色数据</h3>
          <button @click="showImportExportModal = false" class="close-btn">×</button>
        </div>

        <!-- 选项卡 -->
        <div class="ie-tabs">
          <button class="ie-tab" :class="{ active: importExportTab === 'export' }" @click="switchImportExportTab('export')">导出</button>
          <button class="ie-tab" :class="{ active: importExportTab === 'import' }" @click="switchImportExportTab('import')">导入</button>
        </div>

        <div class="modal-body">
          <!-- 导出面板 -->
          <div v-if="importExportTab === 'export'">
            <p class="ie-desc">导出 {{ userDetails?.Username || userDetails?.name }} 的属性和背包数据（不含账号信息）。</p>
            <div class="ie-actions">
              <button @click="exportPlayerDataCopy" class="ie-action-btn">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>
                复制到剪贴板
              </button>
              <button @click="exportPlayerDataDownload" class="ie-action-btn primary">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>
                下载 {{ userDetails?.Username || userDetails?.name || 'player' }}.json
              </button>
            </div>
            <div class="ie-json-preview">
              <pre>{{ JSON.stringify(buildExportData(), null, 2) }}</pre>
            </div>

            <div class="ie-preset-section">
              <div class="ie-preset-header" @click="presetExportOpen = !presetExportOpen">
                <span class="ie-preset-toggle">{{ presetExportOpen ? '▼' : '▶' }}</span>
                <span>导出为预设</span>
              </div>
              <div v-if="presetExportOpen" class="ie-preset-body">
                <div class="ie-preset-row">
                  <input v-model="presetName" type="text" class="form-input" placeholder="输入预设名称" @keyup.enter="exportAsPreset" />
                  <button @click="exportAsPreset" :disabled="presetSaving" class="ie-action-btn" style="flex-shrink:0">
                    {{ presetSaving ? '保存中...' : '保存' }}
                  </button>
                </div>
                <div v-if="presetSaveError" class="give-error" style="margin-top:8px">{{ presetSaveError }}</div>
                <div v-if="presetSaveSuccess" class="give-success" style="margin-top:8px">{{ presetSaveSuccess }}</div>
              </div>
            </div>
          </div>

          <!-- 导入面板 -->
          <div v-else>
            <p class="ie-desc">选择一种方式导入属性和背包数据到当前玩家：</p>

            <!-- 三张选择卡片 -->
            <div class="ie-method-picker">
              <div class="ie-pick-card" :class="{ active: selectedImportMethod === 'preset' }" @click="selectImportMethod('preset')">
                <span class="ie-pick-icon">📦</span>
                <span class="ie-pick-label">从预设导入</span>
                <span class="ie-pick-desc">一键快速导入</span>
              </div>
              <div class="ie-pick-card" :class="{ active: selectedImportMethod === 'paste' }" @click="selectImportMethod('paste')">
                <span class="ie-pick-icon">📋</span>
                <span class="ie-pick-label">粘贴 JSON</span>
                <span class="ie-pick-desc">手动粘贴数据</span>
              </div>
              <div class="ie-pick-card" :class="{ active: selectedImportMethod === 'upload' }" @click="selectImportMethod('upload')">
                <span class="ie-pick-icon">📁</span>
                <span class="ie-pick-label">上传文件</span>
                <span class="ie-pick-desc">选择 .json 文件</span>
              </div>
            </div>

            <!-- 预设内容 -->
            <div v-if="selectedImportMethod === 'preset'" class="ie-method-panel">
              <div v-if="presetLoading" class="loading-state" style="padding:16px"><p>加载中...</p></div>
              <div v-else-if="presetList.length === 0" class="ie-panel-empty">
                <p>暂无预设，请先在导出面板中保存预设。</p>
                <button class="ie-action-btn" @click="importExportTab = 'export'">去导出</button>
              </div>
              <div v-else class="ie-preset-list">
                <div v-for="p in presetList" :key="p.name" class="ie-preset-item" @click="loadPreset(p.name)">
                  <div class="ie-preset-item-info">
                    <span class="ie-preset-item-name">{{ p.name }}</span>
                    <span class="ie-preset-item-date">{{ new Date(p.lastModified).toLocaleString('zh-CN') }}</span>
                  </div>
                  <button class="ie-preset-item-del" @click.stop="deletePreset(p.name)" title="删除">×</button>
                </div>
              </div>
            </div>

            <!-- 粘贴内容 -->
            <div v-if="selectedImportMethod === 'paste'" class="ie-method-panel">
              <textarea v-model="importJson" class="ie-textarea" placeholder="在此粘贴 JSON 数据..." rows="8" @input="parseImportData"></textarea>
            </div>

            <!-- 上传内容 -->
            <div v-if="selectedImportMethod === 'upload'" class="ie-method-panel">
              <div class="ie-upload-zone" @click="$refs.importFileInput.click()" @dragover.prevent @drop.prevent="handleDropFile">
                <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
                <span class="ie-upload-text">点击选择或拖拽 .json 文件到此处</span>
                <input ref="importFileInput" type="file" accept=".json" class="ie-file-input" @change="handleImportFileUpload" />
              </div>
            </div>

            <div v-if="importError" class="give-error">{{ importError }}</div>

            <div v-if="importConfirmData" class="ie-import-preview">
              <div class="ie-confirm-info">
                <span class="ie-confirm-label">导出时间</span>
                <span class="ie-confirm-value">{{ importConfirmData.exportedAt }}</span>
                <span class="ie-confirm-label">距今</span>
                <span class="ie-confirm-value">{{ importConfirmData.daysAgo }} 天</span>
                <span class="ie-confirm-label">包含属性</span>
                <span class="ie-confirm-value">{{ importConfirmData.hasStats ? '是' : '否' }}</span>
                <span class="ie-confirm-label">包含物品</span>
                <span class="ie-confirm-value">{{ importConfirmData.hasInventory ? importConfirmData.itemCount + ' 件' : '否' }}</span>
              </div>
              <label class="ie-clear-check">
                <input type="checkbox" v-model="importClearUnspecified" />
                清除未指定的格子物品
              </label>
              <p v-if="importConfirmData.daysAgo > 7" class="ie-warning">数据已导出超过 7 天，部分物品或属性可能已变更。</p>
              <button @click="executeImport" :disabled="importLoading" class="submit-btn">
                {{ importLoading ? '导入中...' : '确认导入' }}
              </button>
            </div>

            <div v-if="importSuccess" class="give-success">{{ importSuccess }}</div>
          </div>
        </div>
      </div>
    </div>

  <Teleport to="body">
    <Transition name="toast-fade">
      <div v-if="toastMessage" class="toast-notification">{{ toastMessage }}</div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.user-detail-page {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  padding: 0;
  max-width: 1400px;
  margin: 0 auto;
  width: 100%;
}

.detail-layout {
  display: grid;
  grid-template-columns: 1fr 360px;
  gap: 20px;
  padding: 0 20px;
  margin-bottom: 24px;
  align-items: start;
}

.detail-grid {
  display: grid;
  grid-template-columns: 1fr 360px;
  gap: 20px;
  padding: 0 20px;
  margin-bottom: 24px;
  align-items: start;
}

.detail-left {
  display: flex;
  flex-direction: column;
  gap: 20px;
  min-width: 0;
}

.detail-right {
  display: flex;
  flex-direction: column;
  gap: 20px;
  min-width: 0;
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

.qq-bind-status {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  background: var(--bg-tertiary);
  border-radius: 20px;
  font-size: 0.8rem;
  color: var(--text-muted);
  cursor: default;
  user-select: none;
}

.qq-bind-status.bound {
  background: rgba(34, 197, 94, 0.12);
  cursor: pointer;
}

.qq-bind-status.bound:hover {
  background: rgba(34, 197, 94, 0.2);
}

.qq-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-muted);
}

.qq-bind-status.bound .qq-dot {
  background: #22c55e;
  box-shadow: 0 0 10px rgba(34, 197, 94, 0.8);
}

.qq-number {
  font-weight: 800;
  color: #22c55e;
  background: linear-gradient(90deg, #22c55e, #4ade80, #22c55e);
  background-size: 200% 100%;
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  animation: qq-shimmer 2.5s ease-in-out infinite;
  text-shadow: none;
}

@keyframes qq-shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

@keyframes qq-glow {
  0%, 100% { box-shadow: 0 0 8px rgba(234, 179, 8, 0.4); }
  50% { box-shadow: 0 0 14px rgba(234, 179, 8, 0.9); }
}

.action-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 20px 24px;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
}

.duplicate-check-btn {
  padding: 8px 16px;
  font-weight: 600;
  color: #fff;
  background: linear-gradient(90deg, #16a34a, #22c55e, #10b981, #16a34a);
  background-size: 300% 100%;
  border: none;
  border-radius: var(--radius-sm);
  cursor: pointer;
  font-size: 0.85rem;
  transition: transform 0.2s ease, box-shadow 0.2s ease;
  animation: flowLight 3s linear infinite;
  box-shadow: 0 0 8px rgba(22, 163, 74, 0.35);
}

.duplicate-check-btn:hover:not(:disabled) {
  transform: scale(1.05);
  box-shadow: 0 0 14px rgba(22, 163, 74, 0.6);
}

.duplicate-check-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
  animation: none;
}

@keyframes flowLight {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

.action-group {
  display: flex;
  flex-direction: column;
  gap: 10px;
  flex: 1;
  min-width: 0;
}

.group-label {
  margin: 0;
  font-size: 0.85rem;
  color: var(--text-secondary);
  font-weight: 600;
  text-align: center;
}

.group-buttons {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  justify-content: center;
}

.tp-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, #8b5cf6, #7c3aed);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.tp-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(139, 92, 246, 0.4);
}

.tp-btn.disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.whisper-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, #3b82f6, #2563eb);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.whisper-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(59, 130, 246, 0.4);
}

.whisper-btn.disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.password-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, #10b981, #059669);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.password-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(16, 185, 129, 0.4);
}

.password-input-wrapper {
  display: flex;
  align-items: center;
  gap: 10px;
}

.password-input-inner {
  position: relative;
  flex: 1;
}

.password-input-inner .form-input {
  width: 100%;
  padding-right: 60px;
}

.toggle-password-btn {
  position: absolute;
  right: 4px;
  top: 50%;
  transform: translateY(-50%);
  padding: 4px 10px;
  background: none;
  border: none;
  color: var(--accent-primary);
  cursor: pointer;
  font-size: 0.8rem;
  line-height: 1;
  transition: all 0.2s ease;
  white-space: nowrap;
}

.toggle-password-btn:hover {
  color: var(--accent-hover);
  text-decoration: underline;
}

.generate-pwd-badge {
  flex-shrink: 0;
  padding: 5px 12px;
  background: rgba(139, 92, 246, 0.12);
  color: #a78bfa;
  border: 1px solid rgba(139, 92, 246, 0.3);
  border-radius: 20px;
  cursor: pointer;
  font-size: 0.78rem;
  font-weight: 500;
  white-space: nowrap;
  transition: all 0.2s ease;
  user-select: none;
}

.generate-pwd-badge:hover {
  background: rgba(139, 92, 246, 0.25);
  border-color: rgba(139, 92, 246, 0.6);
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

.danger-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, #7c3aed, #6d28d9);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.danger-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(124, 58, 237, 0.4);
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

.group-clickable {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  padding: 4px 8px;
  background: rgba(99, 102, 241, 0.1);
  border: 1px solid rgba(99, 102, 241, 0.3);
  border-radius: var(--radius-sm);
  color: var(--accent-primary) !important;
  font-weight: 500;
  transition: all 0.2s ease;
}

.group-clickable:hover {
  background: rgba(99, 102, 241, 0.2);
  border-color: rgba(99, 102, 241, 0.5);
}

.change-hint {
  font-size: 0.75rem;
  color: var(--text-muted);
  font-weight: 400;
}

.uuid-box {
  cursor: pointer;
  padding: 6px 10px;
  background: rgba(99, 102, 241, 0.08);
  border: 1px solid rgba(99, 102, 241, 0.25);
  border-radius: var(--radius-sm);
  transition: all 0.2s ease;
  display: inline-block;
}

.uuid-box:hover {
  background: rgba(99, 102, 241, 0.15);
  border-color: rgba(99, 102, 241, 0.5);
}

.uuid-text {
  font-weight: 700;
  font-family: 'Courier New', monospace;
  color: var(--accent-primary);
  letter-spacing: 0.5px;
}

.ip-summary {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  padding: 4px 10px;
  border-radius: var(--radius-sm);
  transition: all 0.2s ease;
  user-select: none;
  position: relative;
}

.ip-summary:hover {
  background: var(--bg-hover);
}

.ip-display {
  position: relative;
}

.ip-display .ip-text {
  color: var(--text-primary);
  font-size: 0.95rem;
  font-weight: 500;
}

.ip-count-badge {
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.12);
  padding: 1px 7px;
  border-radius: 10px;
  line-height: 1.4;
}

.ip-loc-badge {
  font-size: 0.7rem;
  color: var(--text-muted);
  background: var(--bg-tertiary);
  padding: 1px 7px;
  border-radius: 10px;
  line-height: 1.4;
  max-width: 200px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-chevron {
  color: var(--text-muted);
  transition: transform 0.25s ease;
}

.ip-chevron.open {
  transform: rotate(180deg);
}

/* IP 弹出卡片 (popover) */
.ip-popover {
  position: absolute;
  top: calc(100% + 8px);
  left: 0;
  z-index: 999;
  background: var(--bg-card);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-lg);
  box-shadow: 0 8px 30px rgba(0, 0, 0, 0.2);
  min-width: 280px;
  max-width: 400px;
  max-height: 300px;
  display: flex;
  flex-direction: column;
  transform-origin: top left;
}

.ip-popover-arrow {
  position: absolute;
  top: -5px;
  left: 24px;
  width: 10px;
  height: 10px;
  background: var(--bg-card);
  border-left: 1px solid var(--border-light);
  border-top: 1px solid var(--border-light);
  transform: rotate(45deg);
  z-index: -1;
}

.ip-popover-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  border-bottom: 1px solid var(--border-light);
  font-weight: 600;
  font-size: 0.88rem;
}

.ip-popover-count {
  font-weight: 400;
  font-size: 0.78rem;
  color: var(--text-muted);
}

.ip-popover-list {
  padding: 6px 8px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.ip-popover-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  font-size: 0.85rem;
  padding: 6px 8px;
  border-radius: var(--radius-sm);
  transition: background 0.15s;
}

.ip-popover-item:hover {
  background: var(--bg-hover);
}

.ip-popover-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--accent-primary);
  flex-shrink: 0;
  opacity: 0.5;
  margin-top: 7px;
}

.ip-popover-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  flex: 1;
}

.ip-popover-addr {
  color: var(--text-primary);
  font-family: 'Courier New', monospace;
}

.ip-popover-location {
  font-size: 0.78rem;
  color: var(--text-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.ip-popover-location.loading {
  color: var(--text-muted);
  opacity: 0.6;
}

.ip-popover-location.error {
  color: var(--accent-error);
  opacity: 0.7;
}

.ip-lookup-btn {
  flex-shrink: 0;
  font-size: 0.72rem;
  padding: 2px 8px;
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  background: var(--bg-tertiary);
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s;
  margin-left: auto;
}

.ip-lookup-btn:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.ip-popover-empty {
  color: var(--text-muted);
  text-align: center;
  padding: 20px;
  font-size: 0.85rem;
}

/* IP 弹出卡片动画 */
.ip-fade-enter-active {
  transition: all 0.2s ease-out;
}
.ip-fade-leave-active {
  transition: all 0.15s ease-in;
}
.ip-fade-enter-from {
  opacity: 0;
  transform: translateY(-6px) scale(0.96);
}
.ip-fade-leave-to {
  opacity: 0;
  transform: translateY(-4px) scale(0.97);
}

/* 点击外部关闭的透明层 */
.ip-backdrop {
  position: fixed;
  inset: 0;
  z-index: 998;
  background: transparent;
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
  cursor: pointer;
}

.duplicate-item:hover {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.1);
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

.shared-ips-section {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  padding: 12px;
  background: rgba(239, 68, 68, 0.1);
  border-radius: var(--radius-md);
  border: 1px solid rgba(239, 68, 68, 0.2);
}

.shared-ips-label {
  color: #ef4444;
  font-weight: 600;
  font-size: 0.9rem;
}

.shared-ip-chip {
  padding: 4px 10px;
  background: rgba(239, 68, 68, 0.2);
  color: #dc2626;
  border-radius: var(--radius-sm);
  font-size: 0.85rem;
}

.inventory-section {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 20px 24px;
  margin: 0 20px 24px;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
  display: flex;
  flex-direction: column;
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
  font-size: 1.1rem;
  font-weight: 600;
}

.refresh-btn,
.refresh-stats-btn {
  padding: 10px 20px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.9rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
  white-space: nowrap;
}

.refresh-btn:hover:not(:disabled),
.refresh-stats-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: var(--shadow-md);
}

.refresh-btn:disabled,
.refresh-stats-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  transform: none;
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

.give-form,
.group-form {
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
  align-items: center;
  gap: 12px;
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

.form-input.textarea {
  resize: vertical;
  min-height: 80px;
}

.custom-select-wrapper {
  position: relative;
  width: 100%;
}

.custom-select-trigger {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.95rem;
  cursor: pointer;
  transition: all 0.25s ease;
}

.custom-select-trigger:hover {
  border-color: var(--accent-primary);
}

.custom-select-trigger:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.select-value {
  color: var(--text-primary);
}

.custom-select-trigger .select-arrow {
  color: var(--text-muted);
  font-size: 0.75rem;
  transition: transform 0.2s ease;
}

.custom-select-trigger .select-arrow.rotated {
  transform: rotate(180deg);
}

.custom-select-dropdown,
.custom-select-dropdown-teleport {
  background: var(--bg-card);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  max-height: 200px;
  overflow-y: auto;
  box-shadow: var(--shadow-lg);
}

.custom-select-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  z-index: 100;
}

.custom-select-option {
  padding: 12px 16px;
  color: var(--text-primary);
  font-size: 0.95rem;
  cursor: pointer;
  transition: all 0.2s ease;
  border-bottom: 1px solid var(--border-light);
}

.custom-select-option:last-child {
  border-bottom: none;
}

.custom-select-option:hover {
  background: rgba(99, 102, 241, 0.15);
  color: var(--accent-primary);
}

.custom-select-option.selected {
  background: rgba(99, 102, 241, 0.2);
  color: var(--accent-primary);
  font-weight: 600;
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

.search-input-wrapper {
  flex: 1;
  position: relative;
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

.danger-btn {
  padding: 12px 24px;
  background: linear-gradient(135deg, #7c3aed, #6d28d9);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 600;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.danger-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(124, 58, 237, 0.4);
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

.danger-submit-btn {
  padding: 12px 24px;
  background: linear-gradient(135deg, #7c3aed, #6d28d9);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 600;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.danger-submit-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(124, 58, 237, 0.4);
}

.danger-submit-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.modal-danger {
  border: 2px solid #7c3aed;
}

.kick-submit-btn {
  padding: 12px 24px;
  background: linear-gradient(135deg, #f59e0b, #d97706);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 600;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.kick-submit-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(245, 158, 11, 0.4);
}

.kick-submit-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.tp-modal {
  max-width: 480px;
}

.tp-direction-section {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 16px;
  margin-bottom: 16px;
}

.tp-player-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 16px 20px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
  border: 2px solid var(--border-color);
  min-width: 120px;
  transition: all 0.25s ease;
}

.tp-player-box.active {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.1);
}

.tp-player-name {
  color: var(--text-primary);
  font-weight: 600;
  font-size: 1rem;
  margin-bottom: 4px;
}

.tp-player-label {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.tp-swap-btn {
  padding: 12px 16px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 1.2rem;
  font-weight: 600;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.tp-swap-btn:hover {
  transform: scale(1.1);
  box-shadow: var(--shadow-md);
}

.tp-direction-text {
  text-align: center;
  color: var(--text-secondary);
  font-size: 0.9rem;
  margin-bottom: 20px;
  padding: 8px 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
}

.tp-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.tp-search-loading {
  padding: 8px 12px;
  color: var(--text-muted);
  font-size: 0.85rem;
}

.tp-search-results {
  margin-top: 8px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-color);
  max-height: 200px;
  overflow-y: auto;
}

.tp-search-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  cursor: pointer;
  transition: background 0.2s ease;
  border-bottom: 1px solid var(--border-light);
}

.tp-search-item:last-child {
  border-bottom: none;
}

.tp-search-item:hover:not(.offline) {
  background: var(--bg-hover);
}

.tp-search-item.offline {
  opacity: 0.5;
  cursor: not-allowed;
}

.tp-search-status {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #ef4444;
  flex-shrink: 0;
}

.tp-search-status.online {
  background: #22c55e;
}

.tp-search-name {
  color: var(--text-primary);
  font-weight: 500;
}

.tp-search-group {
  color: var(--text-muted);
  font-size: 0.85rem;
  padding: 4px 8px;
  background: var(--bg-card);
  border-radius: var(--radius-sm);
}

.anomaly-warning {
  background: linear-gradient(135deg, rgba(239, 68, 68, 0.1), rgba(220, 38, 38, 0.1));
  border: 1px solid rgba(239, 68, 68, 0.3);
  border-radius: var(--radius-lg);
  padding: 16px;
  margin-bottom: 16px;
}

.anomaly-title {
  color: #ef4444;
  font-weight: 600;
  font-size: 0.95rem;
  margin-bottom: 12px;
}

.anomaly-item {
  color: #dc2626;
  font-size: 0.9rem;
  padding: 6px 0;
  border-bottom: 1px solid rgba(239, 68, 68, 0.2);
}

.anomaly-item:last-child {
  border-bottom: none;
}

.stats-section {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 24px;
  margin: 0 20px 24px;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
}

.stats-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
}

.export-group {
  display: flex;
  gap: 6px;
}

.export-btn {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 6px 12px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  color: var(--text-secondary);
  font-size: 0.8rem;
  cursor: pointer;
  transition: all 0.2s ease;
  white-space: nowrap;
}

.export-btn:hover {
  background: var(--bg-hover);
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.stats-header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.1rem;
  font-weight: 600;
}

.online-warning {
  font-size: 0.85rem;
  color: #f59e0b;
  padding: 6px 12px;
  background: rgba(245, 158, 11, 0.15);
  border-radius: var(--radius-sm);
}

.stats-content {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.stats-row {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 16px;
}

.stat-item {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
  border: 1px solid var(--border-light);
}

.stat-item label {
  color: var(--text-secondary);
  font-weight: 600;
  font-size: 0.9rem;
}

.stat-input {
  padding: 10px 14px;
  background: var(--bg-card);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 1rem;
  font-weight: 500;
  transition: all 0.25s ease;
}

.stat-input:focus {
  outline: none;
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.enhances-section {
  padding: 16px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-lg);
  border: 1px solid var(--border-light);
}

.enhances-title {
  margin: 0 0 16px 0;
  color: var(--text-secondary);
  font-size: 0.95rem;
  font-weight: 600;
}

.enhances-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
  gap: 12px;
}

.enhance-item {
  display: flex;
  align-items: center;
}

.enhance-label {
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;
  padding: 10px 12px;
  background: var(--bg-card);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-light);
  transition: all 0.2s ease;
  width: 100%;
}

.enhance-label:hover {
  background: var(--bg-hover);
  border-color: var(--accent-primary);
}

.enhance-checkbox {
  width: 18px;
  height: 18px;
  cursor: pointer;
  accent-color: var(--accent-primary);
  flex-shrink: 0;
}

.enhance-label span {
  color: var(--text-primary);
  font-size: 0.9rem;
  flex: 1;
}

.stats-actions {
  display: flex;
  align-items: center;
  gap: 16px;
  justify-content: flex-end;
}

.save-stats-btn {
  padding: 12px 24px;
  background: linear-gradient(135deg, #10b981, #059669);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  font-size: 0.95rem;
  font-weight: 500;
  transition: all 0.25s ease;
  box-shadow: var(--shadow-sm);
}

.save-stats-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(16, 185, 129, 0.4);
}

.save-stats-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.stats-success {
  color: #10b981;
  font-size: 0.9rem;
  font-weight: 500;
}

.stats-error {
  color: #ef4444;
  font-size: 0.9rem;
  font-weight: 500;
}

.daily-stats-section {
  background: var(--bg-card);
  border-radius: var(--radius-xl);
  padding: 16px 18px;
  box-shadow: var(--shadow-lg);
  border: 1px solid var(--border-light);
}

.daily-stats-header {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 12px;
}

.daily-stats-header h3 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--text-primary);
  flex: 1;
  min-width: 0;
}

.daily-header-left { 
  display: flex;
  align-items: center;
}

.mode-toggle-btn {
  padding: 3px 8px;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-sm, 6px);
  background: var(--bg-input);
  color: var(--text-primary);
  cursor: pointer;
  font-size: 0.72rem;
  transition: all 0.15s;
  white-space: nowrap;
}

.mode-toggle-btn:hover {
  background: var(--bg-hover);
  border-color: var(--accent-primary);
}

.playtime-header-badge {
  font-size: 0.75rem;
  color: var(--accent-secondary);
  font-weight: 600;
  white-space: nowrap;
  padding: 3px 10px;
  background: rgba(34, 197, 94, 0.1);
  border-radius: 20px;
  border: 1px solid rgba(34, 197, 94, 0.2);
  line-height: 1.4;
}

.playtime-header-badge strong {
  font-weight: 700;
}

.daily-nav {
  display: flex;
  align-items: center;
  gap: 6px;
}

.daily-nav-btn {
  width: 28px; height: 28px;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-sm, 6px);
  background: var(--bg-input);
  color: var(--text-secondary);
  cursor: pointer;
  font-size: 0.7rem;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s;
}

.daily-nav-btn:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
  border-color: var(--accent-primary);
}

.filter-date {
  padding: 4px 8px;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-sm, 6px);
  background: var(--bg-input);
  color: var(--text-primary);
  font-size: 0.72rem;
  width: 115px;
}

.daily-stats-body { min-height: 60px; }

/* 总览 */
.overview-chart-container {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.overview-summary {
  font-size: 0.72rem;
  color: var(--text-secondary);
  line-height: 1.4;
}

.overview-summary strong {
  color: var(--text-primary);
  font-weight: 600;
}

.overview-chart {
  display: flex;
  align-items: flex-end;
  gap: 1px;
  height: 40px;
}

.overview-col {
  flex: 1;
  display: flex;
  align-items: flex-end;
  cursor: pointer;
}

.overview-bar {
  width: 100%;
  border-radius: 1px 1px 0 0;
  min-height: 2px;
  transition: opacity 0.15s;
}

.overview-col:hover .overview-bar { opacity: 0.7; }

/* 颜色在 level-* class 上（父元素） */
.level-0 .overview-bar, .level-0 .daily-card-fill { background: var(--border-light); }
.level-1 .overview-bar, .level-1 .daily-card-fill { background: #9be9a8; }
.level-2 .overview-bar, .level-2 .daily-card-fill { background: #40c463; }
.level-3 .overview-bar, .level-3 .daily-card-fill { background: #30a14e; }
.level-4 .overview-bar, .level-4 .daily-card-fill { background: #216e39; }

.overview-axis {
  display: flex;
  justify-content: space-between;
  padding: 0 1px;
}

.axis-label {
  font-size: 0.55rem;
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

/* 详情模式 */

.daily-cards {
  display: flex;
  gap: 4px;
}

.daily-card {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 3px;
  padding: 6px 2px 4px;
  border-radius: var(--radius-md, 8px);
  background: var(--bg-tertiary);
  border: 1px solid var(--border-light);
  cursor: pointer;
  transition: all 0.2s;
}

.daily-card:hover { border-color: var(--accent-primary); }
.daily-card.expanded {
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 1px var(--accent-primary);
}

.daily-card-date {
  font-size: 0.6rem;
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
}

.daily-card-bar {
  width: 100%;
  height: 36px;
  border-radius: 3px;
  background: var(--bg-input);
  display: flex;
  align-items: flex-end;
  overflow: hidden;
}

.daily-card-fill {
  width: 100%;
  border-radius: 3px;
  min-height: 2px;
  transition: height 0.3s ease;
}

.daily-card-min {
  font-size: 0.6rem;
  font-weight: 500;
  color: var(--text-primary);
  font-variant-numeric: tabular-nums;
}

/* 逐时详情（卡片下方全宽行） */
.hourly-detail {
  margin-top: 8px;
  padding: 8px 10px;
  background: var(--bg-tertiary);
  border-radius: var(--radius-md, 8px);
  border: 1px solid var(--border-light);
}

.hourly-detail-head {
  font-size: 0.68rem;
  color: var(--text-secondary);
  margin-bottom: 6px;
}

.hourly-mini-chart {
  display: flex;
  align-items: flex-end;
  gap: 1px;
  height: 32px;
}

.hourly-mini-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-end;
}

.hourly-mini-bar {
  width: 100%;
  border-radius: 1px 1px 0 0;
  background: var(--border-light);
  min-height: 2px;
  height: 100%;
}

.hourly-mini-bar.online {
  background: #40c463;
}

.hourly-mini-label {
  font-size: 0.4rem;
  color: var(--text-muted);
  margin-top: 1px;
}

/* Toast */
.toast-notification {
  position: fixed;
  bottom: 40px;
  left: 50%;
  transform: translateX(-50%);
  padding: 14px 28px;
  background: #1f2937;
  color: #f3f4f6;
  border: 1px solid rgba(99, 102, 241, 0.4);
  border-radius: 12px;
  font-size: 0.95rem;
  font-weight: 600;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
  z-index: 9999;
  pointer-events: none;
}
.toast-fade-enter-active { transition: all 0.3s ease; }
.toast-fade-leave-active { transition: all 0.25s ease; }
.toast-fade-enter-from { opacity: 0; transform: translateX(-50%) translateY(20px); }
.toast-fade-leave-to { opacity: 0; transform: translateX(-50%) translateY(-10px); }

/* 导入/导出弹窗 */
.ie-modal {
  max-width: 640px;
}

.ie-tabs {
  display: flex;
  border-bottom: 1px solid var(--border-light);
}

.ie-tab {
  flex: 1;
  padding: 12px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  color: var(--text-muted);
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s ease;
}

.ie-tab:hover {
  color: var(--text-primary);
}

.ie-tab.active {
  color: var(--accent-primary);
  border-bottom-color: var(--accent-primary);
}

.ie-desc {
  font-size: 0.85rem;
  color: var(--text-secondary);
  margin: 0 0 16px;
  line-height: 1.5;
}

.ie-actions {
  display: flex;
  gap: 10px;
  margin-bottom: 16px;
}

.ie-action-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 10px 18px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s ease;
}

.ie-action-btn:hover {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.1);
  color: var(--accent-primary);
}

.ie-action-btn.primary {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  border-color: transparent;
  color: white;
}

.ie-action-btn.primary:hover {
  box-shadow: 0 4px 12px rgba(99, 102, 241, 0.4);
}

.ie-json-preview {
  max-height: 300px;
  overflow-y: auto;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  padding: 14px;
}

.ie-json-preview pre {
  margin: 0;
  font-size: 0.75rem;
  font-family: 'Courier New', monospace;
  color: var(--text-primary);
  white-space: pre-wrap;
  word-break: break-all;
  line-height: 1.5;
}

.ie-import-methods {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-bottom: 16px;
}

.ie-textarea {
  width: 100%;
  padding: 12px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: 0.85rem;
  font-family: 'Courier New', monospace;
  resize: vertical;
  transition: border-color 0.2s ease;
  box-sizing: border-box;
}

.ie-textarea:focus {
  outline: none;
  border-color: var(--accent-primary);
}

.ie-file-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 10px 18px;
  background: var(--bg-tertiary);
  border: 1px dashed var(--border-color);
  border-radius: var(--radius-md);
  color: var(--text-secondary);
  font-size: 0.85rem;
  cursor: pointer;
  transition: all 0.2s ease;
  width: fit-content;
}

.ie-file-label:hover {
  border-color: var(--accent-primary);
  color: var(--accent-primary);
}

.ie-file-input {
  display: none;
}

.ie-import-preview {
  margin-top: 16px;
}

.ie-confirm-info {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 8px 16px;
  padding: 14px;
  background: var(--bg-tertiary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  margin-bottom: 16px;
}

.ie-confirm-label {
  font-size: 0.85rem;
  color: var(--text-secondary);
  font-weight: 600;
}

.ie-confirm-value {
  font-size: 0.85rem;
  color: var(--text-primary);
}

.ie-warning {
  font-size: 0.8rem;
  color: #f59e0b;
  padding: 8px 12px;
  background: rgba(245, 158, 11, 0.1);
  border-radius: var(--radius-sm);
  margin: 0 0 12px;
}

.ie-clear-check {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.85rem;
  color: var(--text-secondary);
  cursor: pointer;
  padding: 8px 12px;
  margin-bottom: 12px;
  background: rgba(239, 68, 68, 0.06);
  border: 1px solid rgba(239, 68, 68, 0.15);
  border-radius: var(--radius-sm);
  transition: all 0.2s ease;
  user-select: none;
}

.ie-clear-check:hover {
  background: rgba(239, 68, 68, 0.12);
  border-color: rgba(239, 68, 68, 0.3);
}

.ie-clear-check input[type="checkbox"] {
  accent-color: #ef4444;
  width: 16px;
  height: 16px;
  cursor: pointer;
}

/* 预设 */
.ie-preset-section {
  margin-top: 16px;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  overflow: hidden;
}

.ie-preset-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  cursor: pointer;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-primary);
  user-select: none;
  transition: background 0.2s;
}

.ie-preset-header:hover {
  background: var(--bg-hover);
}

.ie-preset-toggle {
  font-size: 0.7rem;
  color: var(--text-muted);
  width: 14px;
  flex-shrink: 0;
}

.ie-preset-body {
  padding: 12px 14px;
  border-top: 1px solid var(--border-light);
}

.ie-preset-row {
  display: flex;
  gap: 8px;
}

.ie-preset-row .form-input {
  flex: 1;
}

.ie-preset-empty {
  font-size: 0.8rem;
  color: var(--text-muted);
  padding: 8px 0;
  text-align: center;
}

.ie-preset-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-height: 200px;
  overflow-y: auto;
}

.ie-preset-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 10px;
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition: background 0.15s;
}

.ie-preset-item:hover {
  background: var(--bg-hover);
}

.ie-preset-item-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  flex: 1;
}

.ie-preset-item-name {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-primary);
}

.ie-preset-item-date {
  font-size: 0.7rem;
  color: var(--text-muted);
}

.ie-preset-item-del {
  background: none;
  border: none;
  color: var(--text-muted);
  font-size: 1.1rem;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: var(--radius-sm);
  transition: all 0.15s;
  line-height: 1;
}

.ie-preset-item-del:hover {
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
}

/* 导入方式卡片 */
.ie-method-card {
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  overflow: hidden;
}

.ie-method-card + .ie-method-card {
  margin-top: 12px;
}

.ie-method-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  background: var(--bg-tertiary);
  border-bottom: 1px solid var(--border-light);
}

.ie-method-icon {
  font-size: 1rem;
  line-height: 1;
}

.ie-method-title {
  flex: 1;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-primary);
}

.ie-method-toggle {
  font-size: 0.75rem;
  color: var(--accent-primary);
  background: none;
  border: 1px solid var(--border-light);
  border-radius: var(--radius-sm);
  padding: 2px 10px;
  cursor: pointer;
  transition: all 0.15s;
}

/* 导入方式三卡片选择器 */
.ie-method-picker {
  display: flex;
  gap: 10px;
  margin-bottom: 16px;
}

.ie-pick-card {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 18px 8px;
  background: var(--bg-tertiary);
  border: 2px solid var(--border-light);
  border-radius: var(--radius-lg);
  cursor: pointer;
  transition: all 0.2s ease;
}

.ie-pick-card:hover {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.06);
}

.ie-pick-card.active {
  border-color: var(--accent-primary);
  background: rgba(99, 102, 241, 0.1);
  box-shadow: 0 0 0 1px var(--accent-primary);
}

.ie-pick-icon {
  font-size: 1.6rem;
  line-height: 1;
}

.ie-pick-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-primary);
}

.ie-pick-desc {
  font-size: 0.72rem;
  color: var(--text-muted);
}

.ie-method-panel {
  border: 1px solid var(--border-light);
  border-radius: var(--radius-md);
  overflow: hidden;
  margin-bottom: 16px;
}

.ie-panel-empty {
  text-align: center;
  padding: 24px;
  color: var(--text-muted);
  font-size: 0.85rem;
}

.ie-panel-empty p {
  margin: 0 0 12px;
}

.ie-upload-zone {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  padding: 32px 20px;
  cursor: pointer;
  color: var(--text-muted);
  transition: all 0.2s;
}

.ie-upload-zone:hover {
  background: rgba(99, 102, 241, 0.04);
  color: var(--accent-primary);
}

.ie-upload-text {
  font-size: 0.85rem;
}

.ie-method-panel .ie-textarea {
  border: none;
  border-radius: 0;
}

.ie-method-panel .ie-textarea:focus {
  box-shadow: none;
}

.ie-method-panel .ie-preset-list {
  padding: 8px;
}
</style>