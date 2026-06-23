let cachedItemData = null

export async function loadItemData() {
  if (cachedItemData) {
    return cachedItemData
  }
  
  try {
    const response = await fetch('/ID.json')
    const data = await response.json()
    cachedItemData = data
    return cachedItemData
  } catch (error) {
    console.error('Failed to load item data:', error)
    return { list: [], dict: {} }
  }
}

export function getItemNameById(id) {
  if (!cachedItemData) return null
  
  const item = cachedItemData.list.find(i => i.id === id)
  return item ? item.cn : null
}

export function clearItemDataCache() {
  cachedItemData = null
}