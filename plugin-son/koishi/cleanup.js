var fs = require('fs');
['debug.ts','tempMsg.ts','utils/reply.ts','plugins/temp.ts'].forEach(f => {
  var p = 'C:/Users/lyt/Documents/GitHub/TsWeb/plugin-son/koishi/' + f;
  try { fs.unlinkSync(p); console.log('deleted:', f); } catch(e) { console.log('skip:', f, e.message); }
});
console.log('--- remaining plugins ---');
fs.readdirSync('C:/Users/lyt/Documents/GitHub/TsWeb/plugin-son/koishi/plugins').forEach(f => console.log('  plugins/' + f));
console.log('--- remaining utils ---');
fs.readdirSync('C:/Users/lyt/Documents/GitHub/TsWeb/plugin-son/koishi/utils').forEach(f => console.log('  utils/' + f));
