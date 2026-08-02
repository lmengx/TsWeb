<template>
  <div class="file-manager">
    <div class="page-header">
      <h2>📄 配置文件管理</h2>
      <span class="path-badge">根目录: TShock 程序目录</span>
    </div>

    <div class="file-manager-body">
      <!-- 左侧文件树 -->
      <div class="file-tree-panel">
        <div class="panel-header">文件列表</div>
        <div class="tree-scroll">
          <div v-if="loadingTree" class="loading-text">加载中...</div>
          <div v-else-if="tree.length === 0" class="empty-text">无可访问的文件</div>
          <TreeNode
            v-for="node in tree"
            :key="node.name"
            :node="node"
            :parent-path="node.name + '/'"
            @select="onFileSelect"
          />
        </div>
      </div>

      <!-- 右侧编辑器 -->
      <div class="editor-panel">
        <div class="panel-header" v-if="currentFile">
          <span class="editor-filename">{{ currentFile }}</span>
          <span class="editor-badge" :class="{ readonly: !canWrite }">
            {{ canWrite ? '可编辑' : '只读' }}
          </span>
        </div>

        <div class="editor-content" v-if="currentFile">
          <textarea
            ref="editorRef"
            v-model="editorContent"
            class="code-editor"
            spellcheck="false"
            :readonly="!canWrite"
            @scroll="updateScrollState"
          ></textarea>
          <!-- 悬浮按钮组：⬇ 滚动到最下方(已在底部/内容不足时隐藏)；刷新文件内容 -->
          <div class="editor-float-btns" v-if="currentFile">
            <button
              v-if="showScrollBtn"
              class="scroll-bottom-btn"
              title="滚动到最下方"
              @click="scrollToBottom"
            >⬇</button>
            <button
              class="refresh-btn"
              title="刷新文件内容"
              :disabled="refreshing"
              @click="handleRefresh"
            >
              <svg
                viewBox="0 0 24 24"
                width="16"
                height="16"
                fill="none"
                stroke="currentColor"
                stroke-width="2.2"
                stroke-linecap="round"
                stroke-linejoin="round"
              >
                <path d="M21 12a9 9 0 1 1-2.64-6.36"></path>
                <polyline points="21 3 21 9 15 9"></polyline>
              </svg>
            </button>
          </div>
          <div class="editor-toolbar" v-if="canWrite">
            <button class="btn btn-save" @click="handleSave" :disabled="saving">
              {{ saving ? '保存中...' : '保存' }}
            </button>
            <span v-if="saveMessage" class="save-message" :class="saveStatus">{{ saveMessage }}</span>
          </div>
        </div>

        <div class="editor-empty" v-else>
          <p>点击左侧文件查看内容</p>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, nextTick } from 'vue'
import { getAccessRules, readFile, writeFile } from '../../utils/fileApi.js'
import TreeNode from '../../components/TreeNode.vue'

const tree = ref([])
const loadingTree = ref(true)
const currentFile = ref(null)
const editorContent = ref('')
const canWrite = ref(false)
const saving = ref(false)
const saveMessage = ref('')
const saveStatus = ref('')
const editorRef = ref(null)
const atBottom = ref(true)      // 是否处于底部附近(刷新后智能滚动依据)
const showScrollBtn = ref(false) // 是否显示"滚动到底部"按钮
const refreshing = ref(false)    // 是否正在刷新文件

/** 底部判定容差(px)：在此范围内视为已到底部 */
const BOTTOM_TOLERANCE = 20

/**
 * 更新滚动状态：内容不足以滚动 或 已在底部附近 时不显示"滚动到底部"按钮
 */
function updateScrollState() {
  const el = editorRef.value
  if (!el) {
    showScrollBtn.value = false
    return
  }
  const { scrollTop, clientHeight, scrollHeight } = el
  const maxScroll = scrollHeight - clientHeight
  atBottom.value = maxScroll <= 0 || scrollTop >= maxScroll - BOTTOM_TOLERANCE
  showScrollBtn.value = !atBottom.value
}

/**
 * 滚动到编辑器最下方（日志文件打开时默认在顶部，可一键跳到最新内容）
 */
function scrollToBottom() {
  nextTick(() => {
    if (editorRef.value) {
      editorRef.value.scrollTop = editorRef.value.scrollHeight
      updateScrollState()
    }
  })
}

/**
 * 刷新当前文件内容
 * 刷新前若已在底部附近(或内容不足)，刷新后自动滚动到底部；否则保持原滚动位置
 */
async function handleRefresh() {
  if (!currentFile.value || refreshing.value) return

  const el = editorRef.value
  const prevScrollTop = el ? el.scrollTop : 0
  const wasNearBottom = atBottom.value

  refreshing.value = true
  saveMessage.value = ''
  saveStatus.value = ''
  try {
    const result = await readFile(currentFile.value)
    if (result.content !== undefined) {
      editorContent.value = result.content
      canWrite.value = result.canWrite === true
    } else {
      throw new Error(result.error || '读取失败')
    }
    await nextTick()
    if (el && wasNearBottom) {
      el.scrollTop = el.scrollHeight // 原在底部 → 刷新后仍跳到底部(看到最新内容)
    } else if (el) {
      el.scrollTop = prevScrollTop // 原在中间/顶部 → 保持原位置
    }
    updateScrollState()
  } catch (e) {
    saveMessage.value = `❌ 刷新失败: ${e.message}`
    saveStatus.value = 'error'
  } finally {
    refreshing.value = false
  }
}

onMounted(async () => {
  try {
    const result = await getAccessRules()
    tree.value = result.tree || []
  } catch (e) {
    console.error('Failed to load file tree:', e)
  } finally {
    loadingTree.value = false
  }
})

async function onFileSelect({ path, isDir }) {
  if (isDir) return

  currentFile.value = path
  editorContent.value = ''
  saveMessage.value = ''
  saveStatus.value = ''
  canWrite.value = false

  try {
    const result = await readFile(path)
    if (result.content !== undefined) {
      editorContent.value = result.content
      canWrite.value = result.canWrite === true
    } else {
      editorContent.value = `// 错误: ${result.error || '读取失败'}`
    }
  } catch (e) {
    editorContent.value = `// 读取失败: ${e.message}`
  } finally {
    // 内容渲染完成后更新按钮显示状态
    await nextTick()
    updateScrollState()
  }
}

async function handleSave() {
  if (!currentFile.value) return
  saving.value = true
  saveMessage.value = ''
  saveStatus.value = ''

  try {
    const result = await writeFile(currentFile.value, editorContent.value)
    if (result.message || result.status === '200') {
      saveMessage.value = '✅ 保存成功'
      saveStatus.value = 'success'
    } else {
      saveMessage.value = `❌ ${result.error || '保存失败'}`
      saveStatus.value = 'error'
    }
  } catch (e) {
    saveMessage.value = `❌ ${e.message}`
    saveStatus.value = 'error'
  } finally {
    saving.value = false
    setTimeout(() => { saveMessage.value = '' }, 3000)
  }
}
</script>

<style scoped>
.file-manager {
  height: 100%;
  display: flex;
  flex-direction: column;
  color: var(--text-primary);
}

.page-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 0 0 16px 0;
}
.page-header h2 {
  margin: 0;
  font-size: 20px;
  color: var(--text-primary);
}
.path-badge {
  font-size: 12px;
  background: var(--bg-tertiary);
  padding: 3px 10px;
  border-radius: 4px;
  color: var(--text-muted);
}

.file-manager-body {
  display: flex;
  flex: 1;
  gap: 16px;
  overflow: hidden;
}

/* 左侧文件树 */
.file-tree-panel {
  width: 280px;
  min-width: 200px;
  background: var(--bg-secondary);
  border-radius: var(--radius-md);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid var(--border-light);
}
.panel-header {
  padding: 10px 14px;
  background: var(--bg-tertiary);
  font-size: 13px;
  font-weight: 600;
  color: var(--text-secondary);
  border-bottom: 1px solid var(--border-color);
}
.tree-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 4px 0;
}

/* 右侧编辑器 */
.editor-panel {
  flex: 1;
  background: var(--bg-secondary);
  border-radius: var(--radius-md);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid var(--border-light);
  position: relative;
}
.editor-filename {
  color: var(--text-primary);
  font-weight: 500;
}
.editor-badge {
  margin-left: auto;
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 4px;
  background: #2d7d46;
  color: #8fecb0;
}
.editor-badge.readonly {
  background: #3d3535;
  color: #c99;
}
.editor-content {
  flex: 1;
  display: flex;
  flex-direction: column;
}
.code-editor {
  flex: 1;
  width: 100%;
  background: var(--bg-primary);
  color: var(--text-primary);
  border: none;
  outline: none;
  resize: none;
  padding: 16px;
  font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
  font-size: 13px;
  line-height: 1.6;
  tab-size: 2;
}
.code-editor:read-only {
  opacity: 0.7;
}
.editor-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 16px;
  background: var(--bg-tertiary);
  border-top: 1px solid var(--border-color);
}
.btn-save {
  padding: 6px 20px;
  background: var(--accent-primary);
  color: #fff;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 13px;
  transition: opacity 0.15s;
}
.btn-save:hover:not(:disabled) {
  opacity: 0.85;
}
.btn-save:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.save-message {
  font-size: 12px;
}
.save-message.success {
  color: var(--accent-secondary);
}
.save-message.error {
  color: var(--accent-error);
}
/* 悬浮按钮组：滚动到底部 + 刷新（横向摆放） */
.editor-float-btns {
  position: absolute;
  right: 18px;
  bottom: 64px;
  display: flex;
  flex-direction: row;
  gap: 8px;
  z-index: 5;
}
.scroll-bottom-btn,
.refresh-btn {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  border: 1px solid var(--border-light);
  background: var(--bg-tertiary);
  color: var(--text-primary);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 2px 10px rgba(0, 0, 0, 0.35);
  opacity: 0.75;
  transition: all 0.2s;
}
.scroll-bottom-btn {
  font-size: 16px;
  line-height: 1;
}
.scroll-bottom-btn:hover,
.refresh-btn:hover:not(:disabled) {
  opacity: 1;
  background: var(--accent-primary);
  color: #fff;
}
.refresh-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}
.editor-empty {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-muted);
  font-size: 14px;
}

.loading-text,
.empty-text {
  padding: 20px;
  text-align: center;
  color: var(--text-muted);
  font-size: 13px;
}
</style>
