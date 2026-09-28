<script setup>
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { get } from '../../utils/api.js'
import { getUnverifiedList } from '../../utils/unverifiedApi.js'
import PlayerList from '../../components/PlayerList.vue'

const router = useRouter()

// 鉴权失败（401）由 utils/api.js 统一清登录态并跳转登录页。
// 这里按 error.status 判断（而不是匹配 message 文本），避免文案一改判断就静默失效。
const isAuthError = (error) => !!error && error.status === 401

// ═══ 玩家列表数据源（单一列表 + 服务端分页）═══
// 不再分「在线 / 所有玩家」两个 Tab：统一走分页接口，在线玩家由插件端排序置顶
// （QueryUsers.QueryUsersList：在线优先 + ID 升序），每页最多 100 条。
// onlineOnly 作为「仅在线」筛选参数保留，等价于原「在线」Tab 的能力。
const currentPage = ref(1)
const pageSize = 100
const keyword = ref('')
const hasCharacter = ref(false)
const onlineOnly = ref(false)

const users = ref([])               // 当前页数据
const total = ref(0)                // 过滤后的总数
const onlineCount = ref(0)          // 当前在线账号数（与筛选无关）
const usersLoading = ref(false)
const unverifiedPlayers = ref([])
const unverifiedLoading = ref(false)

// 统一的列表请求（翻页 / 搜索 / 筛选 / 轮询都走这里）
const fetchUsers = async (isSilent = false) => {
  if (!isSilent) usersLoading.value = true
  try {
    const params = new URLSearchParams({
      page: String(currentPage.value),
      pageSize: String(pageSize)
    })
    if (keyword.value.trim()) params.set('keyword', keyword.value.trim())
    if (hasCharacter.value) params.set('hasCharacter', 'true')
    if (onlineOnly.value) params.set('onlineOnly', 'true')

    const res = await get(`/api/tshock/users?${params.toString()}`)
    const result = await res.json()

    const newTotal = result.total || 0

    // 静默轮询期间在线状态变化可能使总页数减少：当前页越界时不写入本页数据，
    // 直接回到第 1 页（currentPage 的 watch 会立刻重新请求），避免闪现空列表。
    if (currentPage.value > 1 && (currentPage.value - 1) * pageSize >= newTotal) {
      currentPage.value = 1
      return
    }

    users.value = result.users || []
    total.value = newTotal
    if (typeof result.onlineCount === 'number') onlineCount.value = result.onlineCount
  } catch (error) {
    if (!isAuthError(error)) {
      console.error('Failed to fetch users:', error)
      if (!isSilent) {
        users.value = []
        total.value = 0
      }
    }
  }
  usersLoading.value = false
}

const fetchUnverified = async (isSilent = false) => {
  if (!isSilent) unverifiedLoading.value = true
  try {
    const res = await getUnverifiedList()
    const result = await res.json()
    unverifiedPlayers.value = result.players || []
  } catch (error) {
    if (!isAuthError(error)) {
      console.error('Failed to fetch unverified players:', error)
      if (!isSilent) unverifiedPlayers.value = []
    }
  }
  unverifiedLoading.value = false
}

const refresh = () => {
  fetchUsers()
  fetchUnverified()
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

const handleOnlineFilterChange = (checked) => {
  onlineOnly.value = checked
}

const handleGoToUserDetail = (username) => {
  router.push(`/console/users/${encodeURIComponent(username)}`)
}

const handleGoToUnverified = (nickname) => {
  router.push(`/console/unverified/${encodeURIComponent(nickname)}`)
}

// 搜索/筛选变化 → 回到第 1 页并重新请求
// （页码确实变化时由 currentPage 的 watch 发起请求，避免同一次筛选改动发两次请求）
watch([keyword, hasCharacter, onlineOnly], () => {
  if (currentPage.value !== 1) currentPage.value = 1
  else fetchUsers()
})

// 翻页 → 重新请求
watch(currentPage, () => {
  fetchUsers()
})

let pollTimer = null
onMounted(() => {
  fetchUsers()
  fetchUnverified()
  // 每 10 秒静默刷新当前页，保持在线状态与在线数实时
  pollTimer = setInterval(() => fetchUsers(true), 10000)
})

// 清理必须与 onMounted 同级注册在 setup 作用域内（不嵌套在 onMounted 回调里）
onUnmounted(() => {
  clearInterval(pollTimer)
  clearTimeout(searchTimer)
})
</script>

<template>
  <PlayerList
    :users="users"
    :total="total"
    :online-count="onlineCount"
    :current-page="currentPage"
    :page-size="pageSize"
    :loading="usersLoading"
    :unverified-players="unverifiedPlayers"
    :unverified-loading="unverifiedLoading"
    @refresh="refresh"
    @page-change="handlePageChange"
    @search-change="handleSearchChange"
    @char-filter-change="handleCharFilterChange"
    @online-filter-change="handleOnlineFilterChange"
    @go-to-user-detail="handleGoToUserDetail"
    @go-to-unverified="handleGoToUnverified"
  />
</template>
