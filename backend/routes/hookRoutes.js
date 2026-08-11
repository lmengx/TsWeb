import { Router } from 'express'
import { backupReceiver, identityReceiver, qqUuidReceiver, uuidCheckReceiver } from '../controllers/hookController.js'

const router = Router()

// ═══════════════════════════════════════════════════════════
// Webhook 回传端点（插件 → 后端）
// 命名空间独立：/hook/，不与其他 API 混用
// 不使用 JWT（无用户上下文），改用 HMAC-SHA256 签名 + X-Server-Id 鉴权
// ═══════════════════════════════════════════════════════════

// 自动备份通知：POST /hook/backup（插件备份完成 → 验签 → SSE 拉取到 data/backup/{serverId}/）
router.post('/backup', backupReceiver)

// QQ 账号绑定上报：POST /hook/identity（玩家绑定已有账号 → 服务器上报 用户名/QQ/密码哈希/uuidList → 台账 → 广播全量）
router.post('/identity', identityReceiver)

// 登录新设备 UUID 上报：POST /hook/qq-uuid（登录成功且新设备 → 台账追加 → 推单条到启用 syncUUID 的服）
router.post('/qq-uuid', qqUuidReceiver)

// UUID 免密判定查询：POST /hook/uuid-check（插件连接期缓存 miss 时确认设备是否已授权）
router.post('/uuid-check', uuidCheckReceiver)

// 文件回传（预留）：POST /hook/files/upload
// 后续文件备份通道在此挂载，同样走签名验证

export default router
