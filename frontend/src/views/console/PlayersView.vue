<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { get } from '../../utils/api.js'
import { getUnverifiedList } from '../../utils/unverifiedApi.js'
import PlayerList from '../../components/PlayerList.vue'

const router = useRouter()

const users = ref([])
const activeUsers = ref([])
const usersLoading = ref(false)
const unverifiedPlayers = ref([])
const unverifiedLoading = ref(false)

const fetchUsers = async () => {
  usersLoading.value = true
  unverifiedLoading.value = true
  try {
    const [usersRes, activeRes, unverifiedRes] = await Promise.all([
      get('/api/tshock/users'),
      get('/api/tshock/activeusers'),
      getUnverifiedList()
    ])
    const usersResult = await usersRes.json()
    const activeResult = await activeRes.json()
    const unverifiedResult = await unverifiedRes.json()
    
    users.value = usersResult.users || []
    
    if (activeResult.activeusers) {
      const names = activeResult.activeusers.split('\t').filter(n => n.trim())
      activeUsers.value = names
    } else {
      activeUsers.value = []
    }
    
    unverifiedPlayers.value = unverifiedResult.players || []
  } catch (error) {
    if (error.message !== 'Unauthorized') {
      console.error('Failed to fetch users:', error)
      users.value = []
      activeUsers.value = []
      unverifiedPlayers.value = []
    }
  }
  usersLoading.value = false
  unverifiedLoading.value = false
}

const handleGoToUserDetail = (username) => {
  router.push(`/console/users/${encodeURIComponent(username)}`)
}

const handleGoToUnverified = (nickname) => {
  router.push(`/console/unverified/${encodeURIComponent(nickname)}`)
}

onMounted(() => {
  fetchUsers()
})
</script>

<template>
  <PlayerList
    :users="users"
    :active-users="activeUsers"
    :unverified-players="unverifiedPlayers"
    :loading="usersLoading"
    :unverified-loading="unverifiedLoading"
    @refresh="fetchUsers"
    @go-to-user-detail="handleGoToUserDetail"
    @go-to-unverified="handleGoToUnverified"
  />
</template>
