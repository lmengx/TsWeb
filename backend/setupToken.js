import crypto from 'crypto'

let currentToken = null

export function generateSetupToken() {
  currentToken = crypto.randomBytes(24).toString('hex')
  return currentToken
}

export function validateSetupToken(token) {
  return currentToken && token === currentToken
}

/**
 * 作废当前 Setup Token（一次性语义）
 *
 * Setup Token 的唯一用途是「首次初始化」：无账户时校验令牌、创建初始管理员。
 * 初始管理员创建成功后必须立即作废，否则它会一直有效：该 Token 会打印在后端控制台、
 * 并以 ?token= 形式出现在 URL 中，一旦泄漏，/probe、/auto-read、/auto-remote 等
 * setupOrAdmin 端点会长期暴露（其中 /probe 还会把参数拼进 shell 命令）。
 *
 * 作废后不影响已登录管理：这些端点同时接受 admin JWT，前端创建管理员后即改用 JWT。
 * 若需重新进入初始化流程（例如误删账户库），可用后端控制台命令重新生成 Token。
 */
export function clearSetupToken() {
  currentToken = null
}
