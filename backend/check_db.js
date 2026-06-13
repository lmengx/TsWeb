import sqlite3 from 'sqlite3';
const db = new sqlite3.Database('../tshock.sqlite');

console.log('Tables in database:');
db.all("SELECT name FROM sqlite_master WHERE type='table'", (err, tables) => {
  if (err) {
    console.error(err);
    process.exit(1);
  }
  tables.forEach(table => {
    console.log('-', table.name);
  });
  
  console.log('\nUsers table columns:');
  db.all("PRAGMA table_info(Users)", (err, columns) => {
    if (err) {
      console.error(err);
      process.exit(1);
    }
    columns.forEach(col => {
      console.log('-', col.name, '(', col.type, ')');
    });
    
    console.log('\nSample data from Users table:');
    db.all("SELECT * FROM Users LIMIT 5", (err, rows) => {
      if (err) {
        console.error(err);
        process.exit(1);
      }
      rows.forEach(row => {
        console.log(row);
      });
      db.close();
    });
  });
});