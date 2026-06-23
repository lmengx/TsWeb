<script setup>
import { ref, onMounted } from 'vue'

const files = ref([])
const loading = ref(true)

const getToken = () => {
  const user = localStorage.getItem('user')
  if (user) {
    try {
      return JSON.parse(user).token
    } catch (e) {
      console.error('Failed to parse user')
      return null
    }
  }
  return null
}

const fetchResources = async () => {
  loading.value = true
  try {
    const token = getToken()
    const headers = {}
    if (token) {
      headers['Authorization'] = `Bearer ${token}`
    }
    const response = await fetch('/api/resources/list', { headers })
    const result = await response.json()
    if (result.status === '200') {
      files.value = result.files
    }
  } catch (error) {
    console.error('Failed to fetch resources:', error)
  } finally {
    loading.value = false
  }
}

const downloadFile = (filename) => {
  const token = getToken()
  if (token) {
    const link = document.createElement('a')
    link.href = `/api/resources/download/${encodeURIComponent(filename)}`
    link.download = filename
    link.target = '_blank'
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
  } else {
    window.open(`/api/resources/download/${encodeURIComponent(filename)}`, '_blank')
  }
}

const formatDate = (dateString) => {
  const date = new Date(dateString)
  return date.toLocaleString('zh-CN')
}

onMounted(() => {
  fetchResources()
})
</script>

<template>
  <div class="resource-download-page">
    <div class="page-header">
      <div class="header-content">
        <div class="header-icon">
          <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
            <polyline points="7 10 12 15 17 10"></polyline>
            <line x1="12" x2="12" y1="15" y2="3"></line>
          </svg>
        </div>
        <div class="header-text">
          <h2>资源下载</h2>
          <p>下载服务器资源和工具文件</p>
        </div>
      </div>
      <button class="refresh-btn" @click="fetchResources">
        <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="23 4 23 10 17 10"></polyline>
          <polyline points="1 20 1 14 7 14"></polyline>
          <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
        </svg>
        <span>{{ loading ? '加载中...' : '刷新' }}</span>
      </button>
    </div>

    <div class="resources-container">
      <div v-if="loading" class="loading">
        <div class="spinner"></div>
        <span>加载资源列表...</span>
      </div>

      <div v-else-if="files.length === 0" class="empty-state">
        <div class="empty-icon-wrapper">
          <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
            <polyline points="7 10 12 15 17 10"></polyline>
            <line x1="12" x2="12" y1="15" y2="3"></line>
          </svg>
        </div>
        <p>暂无资源文件</p>
        <p class="empty-hint">请联系管理员添加资源文件</p>
      </div>

      <div v-else class="files-list">
        <div class="files-header">
          <span class="files-count">共 {{ files.length }} 个文件</span>
        </div>
        
        <div class="files-grid">
          <div
            v-for="file in files"
            :key="file.name"
            class="file-card"
          >
            <div class="file-icon-wrapper">
              <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"></path>
                <polyline points="14 2 14 8 20 8"></polyline>
                <line x1="16" x2="8" y1="13" y2="13"></line>
                <line x1="16" x2="8" y1="17" y2="17"></line>
              </svg>
            </div>
            
            <div class="file-info">
              <h3 class="file-name">{{ file.name }}</h3>
              <div class="file-meta">
                <span class="meta-item">
                  <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M12 20h9"></path>
                    <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"></path>
                  </svg>
                  {{ file.sizeFormatted }}
                </span>
                <span class="meta-item">
                  <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <circle cx="12" cy="12" r="10"></circle>
                    <polyline points="12 6 12 12 16 14"></polyline>
                  </svg>
                  {{ formatDate(file.lastModified) }}
                </span>
              </div>
            </div>
            
            <div class="file-actions">
              <span class="file-type-badge">{{ file.type }}</span>
              <button class="download-btn" @click="downloadFile(file.name)">
                <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                  <polyline points="7 10 12 15 17 10"></polyline>
                  <line x1="12" x2="12" y1="15" y2="3"></line>
                </svg>
                下载
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.resource-download-page {
  padding: 24px;
  width: 100%;
  max-width: 1200px;
  margin: 0 auto;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 24px;
  flex-wrap: wrap;
  gap: 16px;
}

.header-content {
  display: flex;
  align-items: center;
  gap: 16px;
}

.header-icon {
  width: 56px;
  height: 56px;
  background: linear-gradient(135deg, #22c55e, #14b8a6);
  border-radius: var(--radius-xl);
  display: flex;
  align-items: center;
  justify-content: center;
  color: white;
}

.header-text {
  display: flex;
  flex-direction: column;
}

.header-text h2 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.5rem;
  font-weight: 700;
}

.header-text p {
  margin: 4px 0 0 0;
  color: var(--text-secondary);
  font-size: 0.95rem;
}

.refresh-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 20px;
  background: rgba(99, 102, 241, 0.1);
  border: 1px solid rgba(99, 102, 241, 0.3);
  border-radius: var(--radius-lg);
  color: var(--accent-primary);
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.25s ease;
}

.refresh-btn:hover:not(:disabled) {
  background: rgba(99, 102, 241, 0.2);
  transform: translateY(-1px);
}

.refresh-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.resources-container {
  background: rgba(255, 255, 255, 0.04);
  backdrop-filter: blur(20px);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: var(--radius-2xl);
  overflow: hidden;
}

.loading {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 80px 20px;
  gap: 16px;
  color: var(--text-muted);
}

.spinner {
  width: 48px;
  height: 48px;
  border: 3px solid rgba(99, 102, 241, 0.2);
  border-top-color: var(--accent-primary);
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 80px 20px;
  color: var(--text-muted);
}

.empty-icon-wrapper {
  width: 100px;
  height: 100px;
  background: rgba(156, 163, 175, 0.1);
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 20px;
}

.empty-icon-wrapper svg {
  opacity: 0.5;
}

.empty-state p {
  margin: 0;
  font-size: 1.1rem;
}

.empty-hint {
  margin-top: 8px !important;
  font-size: 0.9rem !important;
  color: var(--text-muted);
}

.files-list {
  padding: 24px;
}

.files-header {
  margin-bottom: 20px;
}

.files-count {
  font-size: 0.95rem;
  color: var(--text-secondary);
  font-weight: 500;
}

.files-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
  gap: 16px;
}

.file-card {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 20px;
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: var(--radius-xl);
  transition: all 0.3s ease;
}

.file-card:hover {
  background: rgba(255, 255, 255, 0.08);
  border-color: rgba(99, 102, 241, 0.3);
  transform: translateY(-2px);
}

.file-icon-wrapper {
  width: 56px;
  height: 56px;
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  border-radius: var(--radius-lg);
  display: flex;
  align-items: center;
  justify-content: center;
  color: white;
  flex-shrink: 0;
}

.file-info {
  flex: 1;
  min-width: 0;
}

.file-name {
  margin: 0 0 8px 0;
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-meta {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 0.85rem;
  color: var(--text-muted);
}

.file-actions {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8px;
  flex-shrink: 0;
}

.file-type-badge {
  padding: 4px 10px;
  background: rgba(156, 163, 175, 0.15);
  border-radius: 8px;
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.download-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  background: linear-gradient(135deg, #22c55e, #14b8a6);
  color: white;
  border: none;
  border-radius: var(--radius-md);
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.25s ease;
}

.download-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(34, 197, 94, 0.3);
}

@media (max-width: 768px) {
  .resource-download-page {
    padding: 16px;
  }
  
  .page-header {
    flex-direction: column;
    align-items: flex-start;
  }
  
  .header-text h2 {
    font-size: 1.3rem;
  }
  
  .files-grid {
    grid-template-columns: 1fr;
  }
  
  .file-card {
    flex-direction: column;
    text-align: center;
    align-items: center;
  }
  
  .file-info {
    text-align: center;
  }
  
  .file-meta {
    justify-content: center;
  }
  
  .file-actions {
    flex-direction: row;
    align-items: center;
    justify-content: center;
    width: 100%;
  }
  
  .file-type-badge {
    order: 2;
  }
  
  .download-btn {
    order: 1;
  }
}
</style>