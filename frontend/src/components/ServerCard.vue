<script setup>
defineProps({
  server: { type: Object, required: true },
  isCurrent: { type: Boolean, default: false }
})

defineEmits(['switch-current', 'test', 'edit', 'remove', 'toggle-sync'])
</script>

<template>
  <div class="server-card" :class="{ current: isCurrent, offline: !server.connected }">
    <div class="card-glow"></div>

    <!-- 头部 -->
    <div class="card-head">
      <span class="server-dot" :class="{ online: server.connected }"></span>
      <span class="card-name">{{ server.name || server.host }}</span>
      <span v-if="isCurrent" class="current-tag">当前</span>
    </div>

    <!-- 信息 -->
    <div class="card-info">
      <div class="info-line">
        <span class="label">地址</span>
        <span class="value mono">{{ server.host }}:{{ server.port }}</span>
      </div>
      <div class="info-line">
        <span class="label">状态</span>
        <span class="value" :class="server.connected ? 'ok-text' : 'bad-text'">
          {{ server.connected ? '在线' : '离线' }}
        </span>
      </div>
      <div class="info-line">
        <span class="label">API Key</span>
        <span class="value" :class="server.hasApiKey ? 'ok-text' : 'warn-text'">
          {{ server.hasApiKey ? '已配置' : '未配置' }}
        </span>
      </div>
      <div class="info-line">
        <span class="label">启用</span>
        <span class="value" :class="server.enabled === false ? 'bad-text' : ''">
          {{ server.enabled === false ? '停用' : '启用' }}
        </span>
      </div>
      <div v-if="server.note" class="info-line note">{{ server.note }}</div>
    </div>

    <!-- 同步开关（直接展示在卡片上，点击即时保存） -->
    <div class="card-sync">
      <label class="sync-item">
        <span class="sync-label">是否同步qq注册</span>
        <input
          type="checkbox"
          class="sync-check"
          :checked="server.syncQQAccounts === true"
          @change="$emit('toggle-sync', server, 'syncQQAccounts', $event.target.checked)"
        />
        <span class="sync-switch"></span>
      </label>
      <label class="sync-item">
        <span class="sync-label">是否上传与接收uuid</span>
        <input
          type="checkbox"
          class="sync-check"
          :checked="server.syncUUID === true"
          @change="$emit('toggle-sync', server, 'syncUUID', $event.target.checked)"
        />
        <span class="sync-switch"></span>
      </label>
      <label class="sync-item">
        <span class="sync-label">跨服聊天</span>
        <input
          type="checkbox"
          class="sync-check"
          :checked="server.crossChat === true"
          @change="$emit('toggle-sync', server, 'crossChat', $event.target.checked)"
        />
        <span class="sync-switch"></span>
      </label>
    </div>

    <!-- 操作 -->
    <div class="card-actions">
      <button
        class="act-btn primary"
        :disabled="isCurrent"
        :title="isCurrent ? '当前已是此服务器' : '切换到该服务器'"
        @click="$emit('switch-current', server.id)"
      >设为当前</button>
      <button class="act-btn" title="测试连接" @click="$emit('test', server.id)">测试</button>
      <button class="act-btn" title="编辑配置" @click="$emit('edit', server)">编辑</button>
      <button class="act-btn danger" title="删除服务器" @click="$emit('remove', server)">删除</button>
    </div>
  </div>
</template>

<style scoped>
.server-card {
  position: relative;
  overflow: hidden;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 14px;
  padding: 18px 18px 14px;
  box-shadow: var(--shadow-sm);
  transition: border-color .25s ease, box-shadow .25s ease, transform .25s ease;
}
.server-card:hover {
  transform: translateY(-2px);
  border-color: var(--border-light);
  box-shadow: var(--shadow-lg);
}
/* 当前服务器：渐变边框 + 光效 */
.server-card.current {
  border-color: var(--accent-primary);
  box-shadow: 0 0 0 1px var(--accent-primary), 0 6px 20px rgba(99, 102, 241, .18);
}
.card-glow {
  position: absolute;
  top: 0; left: 0; right: 0;
  height: 3px;
  background: linear-gradient(90deg, transparent, var(--accent-primary), transparent);
  opacity: 0;
  transition: opacity .3s ease;
}
.server-card.current .card-glow { opacity: 1; }

.card-head { display: flex; align-items: center; gap: 8px; margin-bottom: 14px; }
.server-dot {
  width: 10px; height: 10px; border-radius: 50%;
  background: #ef4444; flex-shrink: 0;
  box-shadow: 0 0 0 2px rgba(239, 68, 68, .15);
}
.server-dot.online {
  background: #22c55e;
  box-shadow: 0 0 8px rgba(34, 197, 94, .7);
}
.card-name {
  flex: 1;
  font-weight: 700; color: var(--text-primary); font-size: 1rem;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.current-tag {
  background: linear-gradient(135deg, var(--accent-primary), #4f46e5);
  color: #fff; font-size: .72rem; font-weight: 700;
  padding: 2px 10px; border-radius: 20px;
  box-shadow: 0 2px 8px rgba(99, 102, 241, .3);
  flex-shrink: 0;
}

.card-info { display: flex; flex-direction: column; gap: 7px; margin-bottom: 14px; font-size: .84rem; }
.info-line { display: flex; align-items: center; gap: 8px; color: var(--text-muted); }
.info-line .label { color: var(--text-muted); font-size: .78rem; width: 56px; flex-shrink: 0; }
.info-line .value { color: var(--text-primary); font-weight: 500; }
.info-line .mono { font-family: monospace; font-size: .82rem; }
.ok-text { color: #22c55e !important; }
.bad-text { color: #ef4444 !important; }
.warn-text { color: #f59e0b !important; }
.info-line.note { color: var(--text-muted); font-style: italic; padding-left: 64px; }

/* 同步开关 */
.card-sync {
  display: flex; flex-direction: column; gap: 8px;
  margin-bottom: 12px; padding: 10px 12px;
  background: var(--bg-tertiary); border: 1px solid var(--border-color); border-radius: 10px;
}
.sync-item {
  display: flex; align-items: center; justify-content: space-between; gap: 8px;
  cursor: pointer; user-select: none;
}
.sync-label { font-size: .8rem; color: var(--text-primary); font-weight: 500; }
.sync-check { position: absolute; opacity: 0; width: 0; height: 0; }
.sync-switch {
  position: relative; flex-shrink: 0;
  width: 34px; height: 19px; border-radius: 20px;
  background: var(--border-color); transition: background .2s ease;
}
.sync-switch::after {
  content: ''; position: absolute; top: 2px; left: 2px;
  width: 15px; height: 15px; border-radius: 50%;
  background: #fff; transition: transform .2s ease;
  box-shadow: 0 1px 3px rgba(0,0,0,.25);
}
.sync-check:checked + .sync-switch { background: var(--accent-primary); }
.sync-check:checked + .sync-switch::after { transform: translateX(15px); }
.sync-check:focus-visible + .sync-switch { box-shadow: 0 0 0 2px rgba(99,102,241,.4); }

.card-actions { display: flex; gap: 6px; flex-wrap: wrap; border-top: 1px solid var(--border-light); padding-top: 12px; }
.act-btn {
  border: 1px solid var(--border-color); background: var(--bg-tertiary); color: var(--text-primary);
  padding: 5px 11px; border-radius: 8px; cursor: pointer; font-size: .78rem;
  transition: all .18s ease;
}
.act-btn:hover { border-color: var(--accent-primary); color: var(--accent-primary); }
.act-btn:disabled { opacity: .45; cursor: not-allowed; }
.act-btn.primary {
  background: var(--accent-primary); border-color: var(--accent-primary); color: #fff; font-weight: 600;
}
.act-btn.primary:hover { opacity: .9; color: #fff; }
.act-btn.danger:hover { border-color: #ef4444; color: #ef4444; }
</style>
