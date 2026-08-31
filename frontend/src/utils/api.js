import { getCurrentServerId } from './serverStore.js'

const handleAuthError = () => {
  localStorage.removeItem('user')
  window.location.href = '/login'
}

export const apiRequest = async (url, options = {}) => {
  const user = localStorage.getItem('user')
  let token = null

  if (user) {
    try {
      token = JSON.parse(user).token
    } catch (e) {
      console.error('Failed to parse user')
    }
  }

  const headers = {
    'Content-Type': 'application/json',
    ...options.headers
  }

  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  // 自动附加当前服务器 id（后端请求级上下文，无全局 currentServerId）
  const serverId = getCurrentServerId()
  if (serverId) {
    headers['x-server-id'] = serverId
  }

  const response = await fetch(url, {
    ...options,
    headers
  })

  if (response.status === 401) {
    handleAuthError()
    throw new Error('Unauthorized')
  }

  return response
}

export const post = async (url, data = {}) => {
  return apiRequest(url, {
    method: 'POST',
    body: JSON.stringify(data)
  })
}

export const get = async (url) => {
  return apiRequest(url, {
    method: 'GET'
  })
}

export const put = async (url, data = {}) => {
  return apiRequest(url, {
    method: 'PUT',
    body: JSON.stringify(data)
  })
}

export const patch = async (url, data = {}) => {
  return apiRequest(url, {
    method: 'PATCH',
    body: JSON.stringify(data)
  })
}

export const del = async (url) => {
  return apiRequest(url, {
    method: 'DELETE'
  })
}
