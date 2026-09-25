import audit from '../services/auditLogger.js'
import { renameUser, deleteUser, setUserUuid, batchOperate } from '../services/userAdminService.js'
import { getAccount as getBackendAccount } from '../services/accountService.js'

// ═══════════════════════════════════════════════════════════
// 玩家账号管理接口（/api/useradmin/*）
// 权限：requireManager（admin + subadmin）；删除后端账户时额外校验非当前登录者
// ═══════════════════════════════════════════════════════════

/**
 * 账号改名：POST /api/useradmin/rename
 * body: { username, newName, mode: 'qq'|'server'|'both', serverId? }
 */
export const rename = async (req, res) => {
  try {
    const { username, newName, mode, serverId } = req.body || {}
    const result = await renameUser({ username, newName, mode: mode || 'qq', serverId: serverId || '' })
    audit.record('useradmin.rename', {
      from: username,
      to: newName,
      mode: mode || 'qq',
      serverId: serverId || '',
      actor: req.user?.username || 'unknown',
      ...(result.failed?.length ? { detail: { failed: result.failed } } : {})
    })
    res.json({ status: 'ok', ...result })
  } catch (err) {
    res.status(400).json({ status: '400', error: err.message })
  }
}

/**
 * 删除账号：POST /api/useradmin/delete
 * body: { username, deleteCharacter?, unbindQq?, deleteBackendAccount?, deleteBans? }
 * 铁律：deleteBackendAccount=true 时不允许删除当前登录的后端账户
 */
export const remove = async (req, res) => {
  try {
    const { username, deleteCharacter, unbindQq, deleteBackendAccount, deleteBans } = req.body || {}

    if (deleteBackendAccount) {
      const me = req.user?.username
      // 大小写不敏感比较当前登录者与目标
      if (me && String(me).toLowerCase() === String(username || '').toLowerCase()) {
        return res.status(400).json({ status: '400', error: '不能删除当前登录的后端管理账户' })
      }
      const be = await getBackendAccount(username)
      if (!be) {
        return res.status(404).json({ status: '404', error: '后端不存在该管理账户（或未指定删除后端账户）' })
      }
    }

    const result = await deleteUser({
      username,
      deleteCharacter: deleteCharacter !== false,
      deleteBans: deleteBans !== false,
      unbindQq: unbindQq !== false,
      deleteBackendAccount: !!deleteBackendAccount
    })
    audit.record('useradmin.delete', {
      username,
      deleteCharacter: deleteCharacter !== false,
      unbindQq: unbindQq !== false,
      deleteBackendAccount: !!deleteBackendAccount,
      actor: req.user?.username || 'unknown',
      ...(result.failed?.length ? { detail: { failed: result.failed } } : {})
    })
    res.json({ status: 'ok', ...result })
  } catch (err) {
    res.status(400).json({ status: '400', error: err.message })
  }
}

/**
 * UUID 清除/替换：POST /api/useradmin/uuid
 * body: { username, uuid, broadcast?, serverId? }
 */
export const uuid = async (req, res) => {
  try {
    const { username, uuid, broadcast, serverId } = req.body || {}
    if (username === undefined || uuid === undefined) {
      return res.status(400).json({ status: '400', error: '缺少参数: username / uuid' })
    }
    const result = await setUserUuid({
      username,
      uuid: String(uuid),
      serverId: serverId || '',
      broadcast: broadcast !== false
    })
    audit.record('useradmin.uuid', {
      username,
      cleared: !String(uuid),
      broadcast: broadcast !== false,
      actor: req.user?.username || 'unknown',
      ...(result.failed?.length ? { detail: { failed: result.failed } } : {})
    })
    res.json({ status: 'ok', ...result })
  } catch (err) {
    res.status(400).json({ status: '400', error: err.message })
  }
}

/**
 * 批量账号操作：POST /api/useradmin/batch
 * body: { action: 'group'|'ban'|'password'|'unbind'|'kick'|'message', users: [], params?: {} }
 */
export const batch = async (req, res) => {
  try {
    const { action, users, params } = req.body || {}
    const list = Array.isArray(users) ? users : []
    if (!action || list.length === 0) {
      return res.status(400).json({ status: '400', error: '缺少参数: action / users' })
    }
    if (list.length > 100) {
      return res.status(400).json({ status: '400', error: '批量操作单次上限 100 个用户' })
    }
    const result = await batchOperate({ action, users: list, params: params || {} })
    audit.record('useradmin.batch', {
      action,
      total: result.total,
      ok: result.ok,
      actor: req.user?.username || 'unknown',
      ...(result.failed?.length ? { detail: { failed: result.failed } } : {})
    })
    res.json({ status: 'ok', ...result })
  } catch (err) {
    res.status(400).json({ status: '400', error: err.message })
  }
}
