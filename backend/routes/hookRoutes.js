import { Router } from 'express'
import { backupReceiver, identityReceiver } from '../controllers/hookController.js'

const router = Router()

// ═══════════════════════════════════════════════════════════
// Webhook 回传端点（插件 → 后端）
// 命名空间独立：/hook/，不与其他 API 混用
// 不使用 JWT（无用户上下文），改用 HMAC-SHA256 签名 + X-Server-Id 鉴权
// ═══════════════════════════════════════════════════════════

// 自动备份通知：POST /hook/backup（插件备份完成 → 验签 → SSE 拉取到 data/backup/{serverId}/）
router.post('/backup', backupReceiver)

// QQ 账号绑定上报：POST /hook/identity（玩家绑定已有账号 → 服务器上报 用户名/QQ/密码哈希 → 台账 → 广播全量）
router.post('/identity', identityReceiver)

// 文件回传（预留）：POST /hook/files/upload
// 后续文件备份通道在此挂载，同样走签名验证

export default router
