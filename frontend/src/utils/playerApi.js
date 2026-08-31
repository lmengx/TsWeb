// ═══════════════════════════════════════════════════════════
// 玩家端（QQ 台账登录）请求封装
// token 存 localStorage.user_player，与 admin/subadmin 的 user 完全隔离：
//   - 玩家 token 绝不写入 'user'（否则会覆盖管理端登录态）
//   - 管理端 token 也绝不写入 'user_player'
// 401 → 清除 user_player 并回玩家页（不污染管理端登录态）
// ═══════════════════════════════════════════════════════════

const handlePlayerAuthError = () => {
  localStorage.removeItem('user_player')
  window.location.href = '/vote'
}

export const playerRequest = async (url, options = {}) => {
  const raw = localStorage.getItem('user_player')
  let token = null
  if (raw) {
    try { token = JSON.parse(raw).token } catch (e) { /* 忽略解析失败 */ }
  }

  const headers = {
    'Content-Type': 'application/json',
    ...options.headers
  }
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  const response = await fetch(url, { ...options, headers })

  if (response.status === 401) {
    handlePlayerAuthError()
    throw new Error('Unauthorized')
  }

  return response
}

export const playerGet = async (url) => {
  return playerRequest(url, { method: 'GET' })
}

export const playerPost = async (url, data = {}) => {
  return playerRequest(url, {
    method: 'POST',
    body: JSON.stringify(data)
  })
}
