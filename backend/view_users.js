import initSqlJs from 'sql.js'
import fs from 'fs/promises'

async function main() {
  const SQL = await initSqlJs({
    locateFile: file => `node_modules/sql.js/dist/${file}`
  })
  const buffer = await fs.readFile('c:\\Users\\lyt\\Documents\\GitHub\\TsWeb\\tshock.sqlite')
  const db = new SQL.Database(new Uint8Array(buffer))
  
  const result = db.exec("SELECT Username, Password FROM Users")
  console.log('Users data:')
  if (result.length > 0 && result[0].values) {
    result[0].values.forEach((row, index) => {
      console.log(`User ${index + 1}:`)
      console.log(`  Username: ${row[0]}`)
      console.log(`  Password: ${row[1]}`)
      console.log(`  Password length: ${row[1] ? row[1].length : 0}`)
    })
  }
  
  db.close()
}

main().catch(console.error)
