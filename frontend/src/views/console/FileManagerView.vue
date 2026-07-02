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
            v-model="editorContent"
            class="code-editor"
            spellcheck="false"
            :readonly="!canWrite"
          ></textarea>
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
import { ref, onMounted } from 'vue'
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
  color: #e0e0e0;
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
  color: #fff;
}
.path-badge {
  font-size: 12px;
  background: #2a2d35;
  padding: 3px 10px;
  border-radius: 4px;
  color: #999;
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
  background: #1e1f26;
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.panel-header {
  padding: 10px 14px;
  background: #252830;
  font-size: 13px;
  font-weight: 600;
  color: #aaa;
  border-bottom: 1px solid #333;
}
.tree-scroll {
  flex: 1;
  overflow-y: auto;
  padding: 4px 0;
}

/* 右侧编辑器 */
.editor-panel {
  flex: 1;
  background: #1e1f26;
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.editor-filename {
  color: #e0e0e0;
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
  background: #12131a;
  color: #d4d4d4;
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
  background: #252830;
  border-top: 1px solid #333;
}
.btn-save {
  padding: 6px 20px;
  background: #4a9eff;
  color: #fff;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 13px;
  transition: background 0.15s;
}
.btn-save:hover:not(:disabled) {
  background: #6aafff;
}
.btn-save:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.save-message {
  font-size: 12px;
}
.save-message.success {
  color: #8fecb0;
}
.save-message.error {
  color: #f48771;
}
.editor-empty {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #666;
  font-size: 14px;
}

.loading-text,
.empty-text {
  padding: 20px;
  text-align: center;
  color: #666;
  font-size: 13px;
}
</style>
