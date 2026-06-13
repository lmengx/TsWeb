<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { get } from '../../utils/api.js'
import PlayerList from '../../components/PlayerList.vue'

const router = useRouter()

const users = ref([])
const activeUsers = ref([])
const usersLoading = ref(false)

const fetchUsers = async () => {
  usersLoading.value = true
  try {
    const [usersRes, activeRes] = await Promise.all([
      get('/api/tshock/users'),
      get('/api/tshock/activeusers')
    ])
    const usersResult = await usersRes.json()
    const activeResult = await activeRes.json()
    
    users.value = usersResult.users || []
    
    if (activeResult.activeusers) {
      const names = activeResult.activeusers.split('\t').filter(n => n.trim())
      activeUsers.value = names
    } else {
      activeUsers.value = []
    }
  } catch (error) {
    if (error.message !== 'Unauthorized') {
      console.error('Failed to fetch users:', error)
      users.value = []
      activeUsers.value = []
    }
  }
  usersLoading.value = false
}

const handleGoToUserDetail = (username) => {
  router.push(`/console/users/${encodeURIComponent(username)}`)
}

onMounted(() => {
  fetchUsers()
})
</script>

<template>
  <PlayerList
    :users="users"
    :active-users="activeUsers"
    :loading="usersLoading"
    @refresh="fetchUsers"
    @go-to-user-detail="handleGoToUserDetail"
  />
</template>
