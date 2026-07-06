import jwt from 'jsonwebtoken'
import bcrypt from 'bcrypt'
import forge from 'node-forge'
import { getConfig } from '../config.js'
import tshockService from '../services/tshockService.js'
import { validateSetupToken } from '../setupToken.js'

let JWT_SECRET = 'your-secret-key-here-change-in-production'
let CHALLENGE_EXPIRE = 120000

const serverKeyPairs = new Map()

async function getJwtSecret() {
  if (JWT_SECRET === 'your-secret-key-here-change-in-production') {
    const config = await getConfig()
    JWT_SECRET = config.security.jwtSecret || 'your-secret-key-here-change-in-production'
    CHALLENGE_EXPIRE = config.security.challengeExpire || 120000
  }
  return JWT_SECRET
}

async function getTokenExpire() {
  const config = await getConfig()
  return config.security.tokenExpire || '1h'
}

function generateServerKeyPair() {
  const keys = forge.pki.rsa.generateKeyPair(2048)
  const keyId = forge.util.bytesToHex(forge.random.getBytes(16))
  const expiresAt = Date.now() + CHALLENGE_EXPIRE
  
  serverKeyPairs.set(keyId, {
    privateKey: keys.privateKey,
    publicKey: keys.publicKey,
    expiresAt
  })
  
  setTimeout(() => {
    serverKeyPairs.delete(keyId)
  }, CHALLENGE_EXPIRE)
  
  return {
    keyId,
    publicKey: forge.pki.publicKeyToPem(keys.publicKey),
    expiresAt
  }
}

export const getServerKey = (req, res) => {
  const keyData = generateServerKeyPair()
  res.json(keyData)
}

export const setupLogin = async (req, res) => {
  const token = req.query.token
  if (!token || !validateSetupToken(token)) {
    return res.status(403).json({ error: '无效的 Setup Token' })
  }

  try {
    const secret = await getJwtSecret()
    const expire = await getTokenExpire()
    const jwtToken = jwt.sign(
      { username: 'admin', usergroup: 'superadmin' },
      secret,
      { expiresIn: expire }
    )
    res.json({ success: true, token: jwtToken, userGroup: 'superadmin' })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

export const login = async (req, res) => {
  const { keyId, encryptedPassword, clientPublicKeyPem, username } = req.body
  
  if (!keyId || !encryptedPassword || !clientPublicKeyPem || !username) {
    return res.status(400).json({ error: 'Missing required fields' })
  }

  const serverKeyPair = serverKeyPairs.get(keyId)
  if (!serverKeyPair || Date.now() > serverKeyPair.expiresAt) {
    return res.status(400).json({ error: 'Invalid or expired server key' })
  }

  serverKeyPairs.delete(keyId)

  try {
    const encryptedBytes = forge.util.decode64(encryptedPassword)
    const decryptedPassword = serverKeyPair.privateKey.decrypt(encryptedBytes, 'RSA-OAEP')

    if (!tshockService.getConnectionStatus()) {
      return res.status(503).json({ error: 'Server not connected', redirect: true })
    }

    const result = await tshockService.getUserPassword(username)
    
    if (!result || result.error || !result.password) {
      if (result && result.error && (result.error.includes('fetch') || result.error.includes('timeout') || result.error.includes('Connection'))) {
        return res.status(503).json({ error: 'Server not connected', redirect: true })
      }
      await new Promise(r => setTimeout(r, 500))
      return res.status(401).json({ error: 'User not found' })
    }

    const storedHash = String(result.password)
    const userGroup = String(result.usergroup || 'default')
    
    if (!storedHash || storedHash.length < 10) {
      await new Promise(r => setTimeout(r, 500))
      return res.status(401).json({ error: 'User not found' })
    }

    const isValid = await bcrypt.compare(String(decryptedPassword), storedHash)
    
    if (isValid) {
      const secret = await getJwtSecret()
      const expire = await getTokenExpire()
      const token = jwt.sign({ username, usergroup: userGroup }, secret, { expiresIn: expire })
      
      try {
        const clientPublicKey = forge.pki.publicKeyFromPem(clientPublicKeyPem)
        const encryptedToken = forge.util.encode64(clientPublicKey.encrypt(token, 'RSA-OAEP'))
        res.json({ success: true, encryptedToken, userGroup })
      } catch (e) {
        res.json({ success: true, token, userGroup })
      }
    } else {
      await new Promise(r => setTimeout(r, 500))
      return res.status(401).json({ error: 'Wrong password' })
    }
  } catch (error) {
    console.error('Login error:', error)
    return res.status(500).json({ error: 'Server error' })
  }
}

export const getUserInfo = (req, res) => {
  res.json({
    username: req.user.username,
    usergroup: req.user.usergroup
  })
}
