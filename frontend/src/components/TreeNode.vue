<template>
  <div class="tree-node">
    <div class="tree-item" :class="{ loading: loadingChildren }" @click="handleClick">
      <span class="tree-icon">{{ isDir ? (expanded ? '📂' : '📁') : '📄' }}</span>
      <span class="tree-name" :title="node.name">{{ node.name }}</span>
      <span v-if="loadingChildren" class="loading-spinner"></span>
    </div>
    <div v-if="isDir && expanded" class="tree-children">
      <div v-if="loadingChildren" class="loading-text">加载中...</div>
      <TreeNode
        v-for="child in childrenList"
        :key="child.name"
        :node="child"
        :parent-path="parentPath + child.name + (child.type === 'dir' ? '/' : '')"
        @select="(e) => $emit('select', e)"
      />
      <div v-if="!loadingChildren && childrenList.length === 0" class="empty-text">空目录</div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from 'vue'
import { listDir } from '../utils/fileApi.js'

const props = defineProps({
  node: { type: Object, required: true },
  parentPath: { type: String, required: true }
})

const emit = defineEmits(['select'])

const expanded = ref(false)
const loadingChildren = ref(false)
const childrenList = ref([])
const loaded = ref(false)

const isDir = props.node.type === 'dir'
const isFile = props.node.type === 'file'

async function handleClick() {
  if (isFile) {
    emit('select', { path: props.parentPath, isDir: false })
    return
  }

  // 目录：切换展开
  expanded.value = !expanded.value

  // 首次展开才加载子内容
  if (expanded.value && !loaded.value) {
    loadingChildren.value = true
    try {
      const dirPath = props.parentPath.endsWith('/') ? props.parentPath : props.parentPath + '/'
      const result = await listDir(dirPath)
      childrenList.value = (result.entries || []).sort((a, b) => {
        // 目录排在文件前
        if (a.type !== b.type) return a.type === 'dir' ? -1 : 1
        return a.name.localeCompare(b.name)
      })
      loaded.value = true
    } catch (e) {
      console.error('Failed to list directory:', e)
      childrenList.value = []
    } finally {
      loadingChildren.value = false
    }
  }
}
</script>

<style scoped>
.tree-node {
  padding-left: 0;
}
.tree-item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 12px;
  cursor: pointer;
  border-radius: 4px;
  font-size: 13px;
  transition: background 0.15s;
  margin: 1px 4px;
  color: var(--text-primary);
}
.tree-item:hover {
  background: var(--bg-hover);
}
.tree-icon {
  font-size: 14px;
  flex-shrink: 0;
}
.tree-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
}
.tree-children {
  padding-left: 16px;
}
.loading-text, .empty-text {
  padding: 4px 12px;
  font-size: 12px;
  color: var(--text-muted);
}
.loading-spinner {
  width: 12px;
  height: 12px;
  border: 2px solid var(--border-color);
  border-top-color: var(--accent-primary);
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
  flex-shrink: 0;
}
@keyframes spin {
  to { transform: rotate(360deg); }
}
</style>
