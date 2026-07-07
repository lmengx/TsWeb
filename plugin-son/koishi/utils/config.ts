import fs from 'fs'
import path from 'path'

export interface AppConfig {
  目标群号: number
  服务器地址: string
  接口密钥: string
}

let config: AppConfig | null = null

export function getConfig(): AppConfig {
  if (config) return config

  const configPath = path.resolve(__dirname, '../config.json')
  const raw = fs.readFileSync(configPath, 'utf-8')
  config = JSON.parse(raw)
  console.log('[TShock] 配置加载完成，目标群：', config.目标群号)
  return config
}
