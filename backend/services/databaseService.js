import initSqlJs from 'sql.js'
import { getConfig } from '../config.js'

let db = null

export async function initDB() {
  const config = await getConfig()
  const fs = await import('fs/promises')
  
  const SQL = await initSqlJs({
    locateFile: file => `node_modules/sql.js/dist/${file}`
  })
  const buffer = await fs.readFile(config.database.path)
  db = new SQL.Database(new Uint8Array(buffer))
  console.log('Database initialized:', config.database.path)
  return db
}

export function getDB() {
  return db
}

export default {
  init: initDB,
  get: getDB
}
