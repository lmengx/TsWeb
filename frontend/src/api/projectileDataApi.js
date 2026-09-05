let cachedProjectileData = null

export async function loadProjectileData() {
  if (cachedProjectileData) {
    return cachedProjectileData
  }

  try {
    const response = await fetch('/ProjectileData.json')
    const data = await response.json()
    cachedProjectileData = data
    return cachedProjectileData
  } catch (error) {
    console.error('Failed to load projectile data:', error)
    return { list: [], dict: {} }
  }
}

export function getProjectileNameById(id) {
  if (!cachedProjectileData) return null
  const item = cachedProjectileData.dict[String(id)]
  return item ? item.chinese : null
}

export function clearProjectileDataCache() {
  cachedProjectileData = null
}
