<script setup>
import { ref, onMounted } from 'vue'
import { get } from '../utils/api.js'

const emit = defineEmits(['configured', 'need-setup'])

const loading = ref(true)
const configured = ref(false)
const configStatus = ref({
  databasePath: false,
  host: false,
  port: false,
  apiKey: false
})

const checkConfig = async () => {
  loading.value = true
  try {
    const response = await get('/api/config/status')
    const result = await response.json()
    configured.value = result.configured
    configStatus.value = result.tshock
    if (result.configured) {
      emit('configured')
    } else {
      emit('need-setup')
    }
  } catch (err) {
    console.error('Failed to check config:', err)
  }
  loading.value = false
}

onMounted(() => {
  checkConfig()
})

defineExpose({ checkConfig })
</script>

<template>
  <div class="setup-check">
    <div v-if="loading" class="loading-state">
      <p>检查配置中...</p>
    </div>
  </div>
</template>

<style scoped>
.setup-check {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px;
}

.loading-state {
  text-align: center;
  color: var(--text-muted);
}
</style>