import crypto from 'crypto'

let currentToken = null

export function generateSetupToken() {
  currentToken = crypto.randomBytes(24).toString('hex')
  return currentToken
}

export function getSetupToken() {
  return currentToken
}

export function validateSetupToken(token) {
  return currentToken && token === currentToken
}

export function clearSetupToken() {
  currentToken = null
}
