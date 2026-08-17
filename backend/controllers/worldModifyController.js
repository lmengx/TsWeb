/**
 * 简易世界修改器控制器（/api/worldmodify/*，仅 admin，x-server-id 请求级上下文）
 * 单服直连模式：不存后端配置，直接读写当前服务器插件端的 /data/worldmodify/* REST
 * - GET  /status  读取全字段当前值 + 元数据（前端数据驱动渲染）
 * - POST /apply   前端 {fields:{...}} → 插件端应用（广播 WorldInfo 即时生效）
 */
import { getCurrentServer } from '../services/tshockService.js'
import worldModifyService from '../services/worldModifyService.js'
import audit from '../services/auditLogger.js'

export const getStatus = async (req, res) => {
  try {
    const server = getCurrentServer()
    if (!server || !server.id) return res.status(409).json({ error: '当前服务器未连接或未配置（缺少 x-server-id）' })
    const data = await worldModifyService.readStatus(server)
    if (!data) return res.status(502).json({ error: '读取失败（插件端离线或未响应）' })
    if (data.error) return res.status(502).json({ error: data.error })
    res.json(data)
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

export const apply = async (req, res) => {
  try {
    const server = getCurrentServer()
    if (!server || !server.id) return res.status(409).json({ error: '当前服务器未连接或未配置（缺少 x-server-id）' })

    const fields = req.body?.fields
    if (!fields || typeof fields !== 'object' || Array.isArray(fields)) {
      return res.status(400).json({ error: 'fields 必须是 JSON 对象（{ 字段名: 值 }）' })
    }
    if (Object.keys(fields).length === 0) {
      return res.status(400).json({ error: 'fields 不能为空' })
    }

    const result = await worldModifyService.applyFields(server, fields)
    if (result.error) return res.status(502).json({ error: result.error })

    audit.record('worldModify.apply', {
      serverId: server.id,
      fields: Object.keys(fields),
      applied: result.applied,
      actor: req.user?.username
    })
    res.json(result)
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}
