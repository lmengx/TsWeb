// 临时脚本：模拟前端完整登录流程（RSA-OAEP 挑战 → player-login → player/me → 越权探测）
import forge from 'node-forge'

const BASE = 'http://localhost:3000'
const USERNAME = 'TestVoter'
const QQ = '100860001'
const PASSWORD = 'TestPass12345'

let ok = true
const check = (name, pass, extra = '') => {
  console.log(`${pass ? '✅' : '❌'} ${name}${extra ? ' | ' + extra : ''}`)
  ok = ok && pass
}

// 1. 获取服务器 RSA 挑战
const keyRes = await fetch(`${BASE}/api/auth/get-server-key`)
const keyData = await keyRes.json()
check('get-server-key', keyRes.status === 200 && !!keyData.keyId, `status=${keyRes.status}`)

// 2. 模拟前端：生成客户端密钥对 + 公钥加密密码
const clientKeys = forge.pki.rsa.generateKeyPair(2048)
const clientPublicKeyPem = forge.pki.publicKeyToPem(clientKeys.publicKey)
const serverPublicKey = forge.pki.publicKeyFromPem(keyData.publicKey)
const encryptedPassword = forge.util.encode64(serverPublicKey.encrypt(PASSWORD, 'RSA-OAEP'))

// 3. player-login（QQ 号通道）
const loginRes = await fetch(`${BASE}/api/auth/player-login`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    account: QQ,
    encryptedPassword,
    clientPublicKeyPem,
    keyId: keyData.keyId
  })
})
const loginData = await loginRes.json()
check('player-login (QQ 号)', loginRes.status === 200 && loginData.success, `status=${loginRes.status} body=${JSON.stringify(loginData).slice(0, 120)}`)

if (loginRes.status !== 200) {
  console.log('\n[结论] 登录失败，见上方状态码。若为 401，且文件已确认写入 → 运行中后端持有台账内存缓存，需要重启后端才能识别新用户。')
  process.exit(ok ? 0 : 1)
}

// 4. 解密 token（模拟前端 RSA-OAEP 解密）
let token = loginData.token
if (loginData.encryptedToken) {
  token = clientKeys.privateKey.decrypt(forge.util.decode64(loginData.encryptedToken), 'RSA-OAEP')
}
check('解密 token', !!token, `payload 前 80 字符: ${token.slice(0, 80)}...`)

// 5. 解析 JWT payload 确认字段
const [headerB64, payloadB64] = token.split('.')
const payload = JSON.parse(Buffer.from(payloadB64, 'base64url').toString('utf8'))
check('JWT payload 字段', payload.username === USERNAME && payload.usergroup === 'player' && payload.qq === QQ,
  `username=${payload.username} usergroup=${payload.usergroup} qq=${payload.qq} exp=${new Date(payload.exp * 1000).toISOString()}`)

// 6. player/me（带 token）
const meRes = await fetch(`${BASE}/api/auth/player/me`, {
  headers: { 'Authorization': `Bearer ${token}` }
})
const meData = await meRes.json()
check('player/me', meRes.status === 200 && meData.username === USERNAME,
  `status=${meRes.status} weight=${meData.weight} playtime=${meData.playtimeMinutes}min`)

// 7. 越权探测：player token 打管理接口 → 必须 403
const adminRes = await fetch(`${BASE}/api/tshock/users`, {
  headers: { 'Authorization': `Bearer ${token}` }
})
check('越权探测 /api/tshock/users → 403', adminRes.status === 403, `实际=${adminRes.status}`)

// 8. 越权探测：player token 打 selfinfo（已加固）→ 必须 403
const siRes = await fetch(`${BASE}/api/user/selfinfo`, {
  headers: { 'Authorization': `Bearer ${token}` }
})
check('越权探测 /api/user/selfinfo → 403', siRes.status === 403, `实际=${siRes.status}`)

console.log(ok ? '\n=== 登录链路全部通过 ===' : '\n=== 存在失败 ===')
process.exit(ok ? 0 : 1)
