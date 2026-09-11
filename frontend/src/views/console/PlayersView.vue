<script setup>
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { get } from '../../utils/api.js'
import { getUnverifiedList } from '../../utils/unverifiedApi.js'
import PlayerList from '../../components/PlayerList.vue'

const router = useRouter()

// ═══ 玩家列表数据源（服务端分页）═══
// 在线 Tab：onlineOnly 接口一次返回全部在线玩家，10 秒轮询保持实时；
// 所有玩家 Tab：分页接口（page/pageSize/keyword/hasCharacter），翻页/搜索/筛选时重新请求。
const activeTab = ref('all')        // 'online' | 'all'
const currentPage = ref(1)
const pageSize = 100
const keyword = ref('')
const hasCharacter = ref(false)

const users = ref([])               // 所有玩家 Tab：当前页数据
const total = ref(0)                // 所有玩家 Tab：过滤后的总数
const onlineUsers = ref([])         // 在线 Tab：全部在线玩家
const usersLoading = ref(false)
const unverifiedPlayers = ref([])
const unverifiedLoading = ref(false)

// 所有玩家 Tab：分页请求
const fetchAll = async (isSilent = false) => {
  if (!isSilent) usersLoading.value = true
  try {
    const params = new URLSearchParams({
      page: String(currentPage.value),
      pageSize: String(pageSize)
    })
    if (keyword.value.trim()) params.set('keyword', keyword.value.trim())
    if (hasCharacter.value) params.set('hasCharacter', 'true')

    const res = await get(`/api/tshock/users?${params.toString()}`)
    const result = await res.json()

    users.value = result.users || []
    total.value = result.total || 0
  } catch (error) {
    if (error.message !== 'Unauthorized') {
      console.error('Failed to fetch users:', error)
      if (!isSilent) {
        users.value = []
        total.value = 0
      }
    }
  }
  usersLoading.value = false
}

// 在线 Tab：全部在线玩家（10 秒轮询）
const fetchOnline = async (isSilent = true) => {
  try {
    const res = await get('/api/tshock/users?onlineOnly=true')
    const result = await res.json()
    onlineUsers.value = result.users || []
  } catch (error) {
    if (error.message !== 'Unauthorized') {
      console.error('Failed to fetch online users:', error)
    }
  }
}

const fetchUnverified = async (isSilent = false) => {
  if (!isSilent) unverifiedLoading.value = true
  try {
    const res = await getUnverifiedList()
    const result = await res.json()
    unverifiedPlayers.value = result.players || []
  } catch (error) {
    if (error.message !== 'Unauthorized') {
      console.error('Failed to fetch unverified players:', error)
      if (!isSilent) unverifiedPlayers.value = []
    }
  }
  unverifiedLoading.value = false
}

const refresh = () => {
  fetchAll()
  fetchOnline()
  fetchUnverified()
}

const handleTabChange = (tab) => {
  activeTab.value = tab
}

const handlePageChange = (page) => {
  currentPage.value = page
}

let searchTimer = null
const handleSearchChange = (query) => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    keyword.value = query
  }, 300)
}

const handleCharFilterChange = (checked) => {
  hasCharacter.value = checked
}

const handleGoToUserDetail = (username) => {
  router.push(`/console/users/${encodeURIComponent(username)}`)
}

const handleGoToUnverified = (nickname) => {
  router.push(`/console/unverified/${encodeURIComponent(nickname)}`)
}

// 搜索/筛选变化 → 回到第 1 页并重新请求
watch([keyword, hasCharacter], () => {
  currentPage.value = 1
  fetchAll()
})

// 翻页 → 重新请求
watch(currentPage, () => {
  fetchAll()
})

let onlineTimer = null
onMounted(() => {
  fetchAll()
  fetchOnline()
  fetchUnverified()
  // 在线玩家每 10 秒静默轮询，保持实时；所有玩家 Tab 手动/翻页/搜索时刷新
  onlineTimer = setInterval(() => fetchOnline(), 10000)
  onUnmounted(() => {
    clearInterval(onlineTimer)
    clearTimeout(searchTimer)
  })
})
</script>

<template>
  <PlayerList
    :users="users"
    :online-users="onlineUsers"
    :total="total"
    :current-page="currentPage"
    :page-size="pageSize"
    :active-tab="activeTab"
    :loading="usersLoading"
    :unverified-players="unverifiedPlayers"
    :unverified-loading="unverifiedLoading"
    @refresh="refresh"
    @tab-change="handleTabChange"
    @page-change="handlePageChange"
    @search-change="handleSearchChange"
    @char-filter-change="handleCharFilterChange"
    @go-to-user-detail="handleGoToUserDetail"
    @go-to-unverified="handleGoToUnverified"
  />
</template>
