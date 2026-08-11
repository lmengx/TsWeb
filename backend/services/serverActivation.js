/**
 * 服务器激活/停用统一入口：注册 REST 实例 + 建立/释放 SSE 常连。
 * 所有「添加 / 编辑 / 轮换密钥 / 删除」服务器的接口必须走这里，避免遗漏任一链路。
 *
 * 背景：日志推送主通道是 后端→插件的 SSE 常连（无条件建立，见 sseConnection.js）；
 * 历史 webhook 附加通道已废弃移除。
 * 历史上 serverController 与 setupRoutes 各自 addServer 后只 registerServer，漏掉了 SSE 常连，
 * 导致运行时添加的服务器永远收不到插件日志。此模块统一补齐。
 */
import { registerServer, unregisterServer } from './tshockService.js'
import { connect, disconnect } from './sseConnection.js'

/**
 * 激活（添加 / 更新 / 轮换密钥后调用）：
 *  - 注册/更新 tshockService REST 实例（registerServer 内部会重启心跳探活）
 *  - 建立后端 → 插件的 SSE 常驻长连接（connect 内部先断开旧连接；含指数退避自动重连）
 * @param {object} server 配置中的服务器对象（须含 id/enabled/host/port/apiKey/pushSecret）
 */
export function activateServer(server) {
  registerServer(server)
  if (server.enabled) {
    connect(server)
  } else {
    // 停用状态：释放常连（注册表保留，便于重新启用）
    disconnect(server.id)
  }
  return server
}

/**
 * 停用（删除服务器时调用）：注销 REST 实例 + 释放 SSE 常连。
 * @param {string} id 服务器 id
 */
export function deactivateServer(id) {
  unregisterServer(id)
  disconnect(id)
}
