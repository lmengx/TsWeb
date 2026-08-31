import { chromium } from 'playwright'
import { readFileSync, existsSync } from 'fs'
import { join } from 'path'

let _browser: import('playwright').Browser | null = null

async function getBrowser(): Promise<import('playwright').Browser> {
  if (_browser?.isConnected()) return _browser
  _browser = await chromium.launch({ args: ['--no-sandbox'] })
  return _browser
}

/** 将完整 HTML 渲染为 PNG 图片 Buffer */
export async function renderHtml(html: string, scale: number = 2, selector: string = 'body'): Promise<Buffer> {
  const browser = await getBrowser()
  const context = await browser.newContext({ deviceScaleFactor: scale })
  const page = await context.newPage()
  try {
    await page.setContent(html, { waitUntil: 'networkidle' })
    const el = page.locator(selector)
    const box = await el.boundingBox()
    if (!box) throw new Error(`Element "${selector}" not found`)
    const buf = await page.screenshot({
      clip: { x: box.x, y: box.y, width: box.width, height: box.height },
      type: 'png',
    })
    return buf
  } finally {
    await page.close()
    await context.close()
  }
}

// ══════════════════════════════════════════════════════════
//  玩家信息卡片
// ══════════════════════════════════════════════════════════

/** 玩家信息卡片 HTML */
export function playerInfoCard(data: {
  player: string
  qq: string
  group: string
  registered: string
  online_minutes: number
  deaths: number
  fishing_quests: number
}): string {
  const hours = Math.floor(data.online_minutes / 60)
  const mins = data.online_minutes % 60
  const onlineStr = hours > 0 ? `${hours}小时${mins}分钟` : `${mins}分钟`

  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{
  font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif;
  background:linear-gradient(135deg,#0f0c29,#302b63,#24243e);
  min-height:100vh;display:flex;align-items:center;justify-content:center;
  padding:12px
}
.card{
  width:320px;
  background:rgba(255,255,255,0.08);
  backdrop-filter:blur(20px);
  -webkit-backdrop-filter:blur(20px);
  border-radius:18px;
  padding:24px 22px;
  border:1px solid rgba(255,255,255,0.12);
  box-shadow:0 25px 50px -12px rgba(0,0,0,0.6);
  position:relative;
  overflow:hidden;
}
.card::before{
  content:'';position:absolute;top:-60%;right:-30%;
  width:300px;height:300px;
  background:radial-gradient(circle,rgba(124,58,237,0.25),transparent 70%);
  pointer-events:none;
}
.card::after{
  content:'';position:absolute;bottom:-40%;left:-20%;
  width:250px;height:250px;
  background:radial-gradient(circle,rgba(59,130,246,0.2),transparent 70%);
  pointer-events:none;
}
.header{
  margin-bottom:20px;position:relative;z-index:1;
}
.title{
  color:#fff;font-size:22px;font-weight:700;letter-spacing:0.5px;
}
.subtitle{
  color:rgba(255,255,255,0.5);font-size:13px;margin-top:2px;
}
.info-grid{
  display:grid;grid-template-columns:1fr 1fr;gap:0;
  position:relative;z-index:1;
}
.info-item{
  padding:12px 0;border-bottom:1px solid rgba(255,255,255,0.06);
}
.info-item:nth-last-child(-n+2){border-bottom:none}
.label{
  font-size:13px;color:rgba(255,255,255,0.4);
  text-transform:uppercase;letter-spacing:0.8px;margin-bottom:5px;
}
.value{
  font-size:17px;color:rgba(255,255,255,0.9);
  font-weight:500;
}
.value.accent{color:#a78bfa}
.value.gold{color:#fbbf24}
.value.green{color:#34d399}
.value.blue{color:#60a5fa}
.full-row{
  grid-column:1/-1;
  padding:12px 0;border-bottom:1px solid rgba(255,255,255,0.06);
}
.footer{
  margin-top:14px;padding-top:12px;
  border-top:1px solid rgba(255,255,255,0.08);
  display:flex;justify-content:space-between;align-items:center;
  position:relative;z-index:1;
}
.footer-qq{
  color:rgba(255,255,255,0.3);
  font-size:14px;font-family:"JetBrains Mono","Consolas",monospace;
}
.footer-badge{
  background:rgba(124,58,237,0.2);
  color:#a78bfa;font-size:12px;
  padding:5px 14px;border-radius:20px;
  border:1px solid rgba(124,58,237,0.3);
  letter-spacing:1px;
}
</style>
</head>
<body>
<div class="card">
  <div class="header">
    <div>
      <div class="title">玩家信息</div>
      <div class="subtitle">Player Profile</div>
    </div>
  </div>
  <div class="info-grid">
    <div class="info-item">
      <div class="label">角色名</div>
      <div class="value accent">${escapeHtml(data.player)}</div>
    </div>
    <div class="info-item">
      <div class="label">用户组</div>
      <div class="value blue">${escapeHtml(data.group)}</div>
    </div>
    <div class="info-item">
      <div class="label">在线时长</div>
      <div class="value green">${escapeHtml(onlineStr)}</div>
    </div>
    <div class="info-item">
      <div class="label">死亡次数</div>
      <div class="value gold">${data.deaths} 次</div>
    </div>
    <div class="info-item full-row">
      <div class="label">钓鱼任务</div>
      <div class="value">${data.fishing_quests} 次</div>
    </div>
    <div class="info-item full-row">
      <div class="label">注册时间</div>
      <div class="value" style="color:rgba(255,255,255,0.7)">${escapeHtml(data.registered)}</div>
    </div>
  </div>
  <div class="footer">
    <span class="footer-qq">QQ ${escapeHtml(data.qq)}</span>
    <span class="footer-badge">TSHOCK</span>
  </div>
</div>
</body>
</html>`
}

// ══════════════════════════════════════════════════════════
//  Boss 进度卡片
// ══════════════════════════════════════════════════════════

const assetsDir = join(__dirname, '..', 'assets', 'boss')

const bossImageMap: Record<string, string> = {
  '史莱姆王': 'King_Slime.png',
  '克苏鲁之眼': 'Eye_of_Cthulhu.png',
  '世界吞噬者': 'Eater_of_Worlds.webp',
  '克苏鲁之脑': 'Brain_of_Cthulhu.png',
  '蜂后': 'QueenBee.png',
  '巨鹿': 'Deerclops.png',
  '骷髅王': 'Skeletron.png',
  '血肉墙': 'Wall_of_Flesh.png',
  '史莱姆皇后': 'Queen_Slime.png',
  '毁灭者': 'The_Destroyer.png',
  '机械骷髅王': 'Skeletron_Prime.png',
  '双子魔眼': 'The_Twins.png',
  '世纪之花': 'Plantera.png',
  '石巨人': 'Golem.png',
  '猪龙鱼公爵': 'Duke_Fishron.png',
  '光之女皇': 'Empress_of_Light.png',
  '拜月教教徒': 'Lunatic_Cultist.png',
  '月亮领主': 'Moon_Lord.png',
}

const eventImageMap: Record<string, string> = {
  '哥布林入侵': 'Goblin.webp',
  '海盗入侵': 'Flying_Dutchman.png',
  '日食': 'eclipse.webp',
  '火星人入侵': 'Martian_Saucer.png',
  '冰雪女王': 'Ice_Queen.png',
  '南瓜王': 'Pumpking.png',
}

/** 加载图片为 base64 data URI，文件不存在返回空字符串 */
function loadImageBase64(filename: string): string {
  const filePath = join(assetsDir, filename)
  if (!existsSync(filePath)) return ''
  const buf = readFileSync(filePath)
  const ext = filename.split('.').pop()?.toLowerCase()
  const mime = ext === 'webp' ? 'image/webp' : 'image/png'
  return `data:${mime};base64,${buf.toString('base64')}`
}

interface BossData {
  Name: string
  NPCID: number
  KillCount: number
  IsKilled: boolean
}

interface EventData {
  Name: string
  EventID: number
  IsCompleted: boolean
}

interface BossProgressData {
  TotalBossCount: number
  KilledCount: number
  BossProgressPercent: number
  Bosses: BossData[]
  TotalEventCount: number
  CompletedEventCount: number
  EventProgressPercent: number
  Events: EventData[]
}

/** 生成 Boss 进度 HTML 卡片 */
export function bossProgressCard(data: BossProgressData): string {
  // 服务器名（后端返回结构附带 server 字段，明确当前进度来自哪个服）
  const serverName = (data as any).server?.name || ''
  const serverTag = serverName
    ? `<div class="server-head"><span class="server-name">${escapeHtml(serverName)}</span></div>`
    : ''
  const bossCards = data.Bosses.map(b => {
    const imgFile = bossImageMap[b.Name] || ''
    const src = imgFile ? loadImageBase64(imgFile) : ''
    const killed = b.IsKilled
    return `<div class="bc ${killed ? 'done' : ''}">
      <div class="bc-img">
        ${src ? `<img src="${src}" alt="${escapeHtml(b.Name)}">` : '<div class="bc-placeholder">?</div>'}
        <div class="bc-badge ${killed ? 'bc-ok' : 'bc-no'}">${killed ? '✓' : '✗'}</div>
      </div>
      <div class="bc-name">${escapeHtml(b.Name)}</div>
      ${killed ? `<div class="bc-count">${b.KillCount} 击杀</div>` : ''}
    </div>`
  }).join('\n')

  const eventCards = data.Events.map(e => {
    const imgFile = eventImageMap[e.Name] || ''
    const src = imgFile ? loadImageBase64(imgFile) : ''
    const done = e.IsCompleted
    return `<div class="bc ${done ? 'done' : ''}">
      <div class="bc-img">
        ${src ? `<img src="${src}" alt="${escapeHtml(e.Name)}">` : '<div class="bc-placeholder">?</div>'}
        <div class="bc-badge ${done ? 'bc-ok' : 'bc-no'}">${done ? '✓' : '✗'}</div>
      </div>
      <div class="bc-name">${escapeHtml(e.Name)}</div>
    </div>`
  }).join('\n')

  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{
  font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif;
  background:linear-gradient(135deg,#0f0c29,#302b63,#24243e);
  min-height:100vh;padding:20px
}
.wrap{width:680px;margin:0 auto}
.server-head{text-align:center;margin-bottom:10px}
.server-name{
  display:inline-block;font-size:14px;font-weight:700;color:#fff;
  background:rgba(139,92,246,0.25);border:1px solid rgba(139,92,246,0.4);
  padding:4px 18px;border-radius:20px;letter-spacing:1px
}
.section{margin-bottom:24px}
.section-head{
  display:flex;justify-content:space-between;align-items:center;margin-bottom:10px
}
.section-head h3{
  font-size:18px;font-weight:700;color:#fff
}
.pct{
  padding:4px 14px;border-radius:20px;font-size:13px;font-weight:600;color:#fff
}
.pct.green{background:linear-gradient(135deg,#10b981,#34d399)}
.pct.purple{background:linear-gradient(135deg,#8b5cf6,#a78bfa)}
.bar{
  height:6px;background:rgba(255,255,255,0.08);border-radius:3px;overflow:hidden;margin-bottom:16px
}
.bar-inner{height:100%;border-radius:3px;transition:width 0.5s}
.bar-inner.green{background:linear-gradient(90deg,#10b981,#34d399)}
.bar-inner.purple{background:linear-gradient(90deg,#8b5cf6,#a78bfa)}
.grid{
  display:grid;grid-template-columns:repeat(6,1fr);gap:8px
}
.bc{
  background:rgba(255,255,255,0.06);
  border-radius:10px;border:1px solid rgba(255,255,255,0.08);
  overflow:hidden;text-align:center
}
.bc.done{border-color:rgba(16,185,129,0.35)}
.bc-img{
  position:relative;height:80px;
  background:linear-gradient(135deg,#1a1a2e,#16213e);
  display:flex;align-items:center;justify-content:center
}
.bc-img img{
  width:70%;height:70%;object-fit:contain;
  filter:drop-shadow(0 2px 4px rgba(0,0,0,0.4))
}
.bc-placeholder{
  width:60%;height:60%;display:flex;align-items:center;justify-content:center;
  background:rgba(255,255,255,0.08);border-radius:8px;
  color:rgba(255,255,255,0.3);font-size:28px
}
.bc-badge{
  position:absolute;top:4px;right:4px;
  width:22px;height:22px;border-radius:50%;
  display:flex;align-items:center;justify-content:center;
  font-size:12px;font-weight:700;color:#fff;
  box-shadow:0 1px 4px rgba(0,0,0,0.4)
}
.bc-ok{background:linear-gradient(135deg,#10b981,#059669)}
.bc-no{background:linear-gradient(135deg,#ef4444,#dc2626)}
.bc-name{
  padding:6px 4px;font-size:12px;font-weight:600;color:rgba(255,255,255,0.85)
}
.bc-count{
  font-size:10px;color:rgba(255,255,255,0.4);padding-bottom:6px
}
</style>
</head>
<body>
<div class="wrap">${serverTag}
  <div class="section">
    <div class="section-head">
      <h3>Boss 击杀进度</h3>
      <span class="pct green">${data.KilledCount}/${data.TotalBossCount}</span>
    </div>
    <div class="bar"><div class="bar-inner green" style="width:${data.BossProgressPercent}%"></div></div>
    <div class="grid">${bossCards}</div>
  </div>
  <div class="section">
    <div class="section-head">
      <h3>事件进度</h3>
      <span class="pct purple">${data.CompletedEventCount}/${data.TotalEventCount}</span>
    </div>
    <div class="bar"><div class="bar-inner purple" style="width:${data.EventProgressPercent}%"></div></div>
    <div class="grid">${eventCards}</div>
  </div>
</div>
</body>
</html>`
}

// ══════════════════════════════════════════════════════════
//  在线列表卡片
// ══════════════════════════════════════════════════════════

interface OnlinePlayer {
  nickname: string
  username?: string
  group?: string
  active?: boolean
}

interface OnlineStatusData {
  name?: string
  world?: string
  playercount: number
  maxplayers: number
  uptime?: string
  players?: OnlinePlayer[]
}

/** 生成在线列表 HTML 卡片 */
export function onlineListCard(data: OnlineStatusData): string {
  const online = data.playercount ?? 0
  const max = data.maxplayers ?? 0
  const players = (data.players || []).filter(p => p && p.nickname)
  const pct = max > 0 ? Math.min(100, Math.round((online / max) * 100)) : 0
  const pctColor = pct >= 80 ? '#ef4444' : pct >= 50 ? '#f59e0b' : '#34d399'
  const serverName = data.name || 'Terraria 服务器'
  const worldName = data.world || ''

  const rows = players.length
    ? players.map(p => `<span class="chip">${escapeHtml(p.nickname)}</span>`).join('')
    : `<div class="empty">🛋️ 当前无人在线</div>`

  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{
  font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif;
  background:linear-gradient(135deg,#0f0c29,#302b63,#24243e);
  min-height:100vh;padding:20px
}
.wrap{
  width:520px;margin:0 auto;
  background:rgba(255,255,255,0.06);
  border:1px solid rgba(255,255,255,0.1);
  border-radius:18px;
  padding:24px 22px;
  box-shadow:0 25px 50px -12px rgba(0,0,0,0.6)
}
.head{display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:18px}
.head-title{font-size:22px;font-weight:700;color:#fff;letter-spacing:0.5px}
.head-sub{color:rgba(255,255,255,0.5);font-size:13px;margin-top:2px}
.online-pill{
  display:flex;align-items:center;gap:8px;
  background:rgba(255,255,255,0.08);
  border:1px solid rgba(255,255,255,0.12);
  padding:8px 14px;border-radius:20px
}
.online-dot{width:10px;height:10px;border-radius:50%;background:${pctColor};
  box-shadow:0 0 8px ${pctColor}}
.online-num{font-size:17px;font-weight:700;color:#fff}
.online-total{font-size:12px;color:rgba(255,255,255,0.5)}
.occ-bar{height:6px;background:rgba(255,255,255,0.08);border-radius:3px;overflow:hidden;margin-bottom:20px}
.occ-inner{height:100%;border-radius:3px;background:${pctColor};transition:width 0.5s}
.list{display:flex;flex-wrap:wrap;gap:6px}
.chip{
  background:rgba(255,255,255,0.07);
  border:1px solid rgba(255,255,255,0.1);
  border-radius:20px;
  padding:4px 12px;
  font-size:12px;font-weight:600;
  color:rgba(255,255,255,0.9);
  white-space:nowrap
}
.empty{
  text-align:center;color:rgba(255,255,255,0.5);
  font-size:15px;padding:28px 0
}
.foot{margin-top:18px;padding-top:14px;border-top:1px solid rgba(255,255,255,0.08);
  display:flex;justify-content:space-between;align-items:center}
.foot-name{color:rgba(255,255,255,0.35);font-size:12px}
.foot-tag{color:rgba(255,255,255,0.35);font-size:12px;font-family:"JetBrains Mono","Consolas",monospace}
</style>
</head>
<body>
<div class="wrap">
  <div class="head">
    <div>
      <div class="head-title">${escapeHtml(serverName)}</div>
      <div class="head-sub">${worldName ? escapeHtml(worldName) : 'Terraria World'}</div>
    </div>
    <div class="online-pill">
      <span class="online-dot"></span>
      <span class="online-num">${online}</span>
      <span class="online-total">/ ${max}</span>
    </div>
  </div>
  <div class="occ-bar"><div class="occ-inner" style="width:${pct}%"></div></div>
  <div class="list">${rows}</div>
  <div class="foot">
    <span class="foot-name">在线玩家列表</span>
    <span class="foot-tag">LIVE</span>
  </div>
</div>
</body>
</html>`
}

function escapeHtml(s: string): string {
  return s.replace(/&/g, '&').replace(/</g, '<').replace(/>/g, '>').replace(/"/g, '"')
}

// ══════════════════════════════════════════════════════════
//  多服在线卡片（后端 /api/bot/online 返回结构）
// ══════════════════════════════════════════════════════════

interface MultiOnlineServer {
  id: string
  name: string
  online: number | null
  max: number | null
  players: string[] | null
}

interface MultiOnlineData {
  mode: string
  mainServer?: { id: string; name: string } | null
  servers?: MultiOnlineServer[]
}

/** 生成多服在线列表 HTML 卡片（每服一块；非主服在 main 模式下仅显示人数） */
export function multiOnlineCard(data: MultiOnlineData): string {
  const servers = data.servers || []
  const blocks = servers.map(s => {
    const online = s.online ?? '?'
    const max = s.max ?? '?'
    const mainTag = data.mainServer?.id === s.id ? ' · 主服' : ''
    const names = s.players || []
    let rows = ''
    if (names.length) {
      rows = names.map(n => `<span class="chip">${escapeHtml(n)}</span>`).join('')
    } else if (s.players === null) {
      // 仅显示人数：大数字徽章（不重复展示玩家名）
      rows = `<div class="only-badge"><span class="ob-num">${online}</span><span class="ob-txt">人在线</span></div>`
    } else {
      rows = `<div class="row muted">🛋️ 当前无人在线</div>`
    }
    return `<div class="sv">
      <div class="sv-head">
        <div class="sv-name">${escapeHtml(s.name)}${mainTag}</div>
        <div class="sv-count">${online} / ${max}</div>
      </div>
      <div class="sv-list">${rows}</div>
    </div>`
  }).join('\n')

  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{
  font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif;
  background:linear-gradient(135deg,#0f0c29,#302b63,#24243e);
  min-height:100vh;padding:20px
}
.wrap{width:520px;margin:0 auto;display:flex;flex-direction:column;gap:14px}
.sv{
  background:rgba(255,255,255,0.06);
  border:1px solid rgba(255,255,255,0.1);
  border-radius:16px;
  padding:16px 18px
}
.sv-head{display:flex;justify-content:space-between;align-items:center;margin-bottom:10px}
.sv-name{font-size:16px;font-weight:700;color:#fff}
.sv-count{
  font-size:14px;font-weight:700;color:#fff;
  background:rgba(255,255,255,0.08);
  border:1px solid rgba(255,255,255,0.12);
  padding:4px 10px;border-radius:14px
}
.sv-list{display:flex;flex-wrap:wrap;gap:6px}
.chip{
  background:rgba(255,255,255,0.07);
  border:1px solid rgba(255,255,255,0.1);
  border-radius:20px;
  padding:4px 12px;
  font-size:12px;font-weight:600;
  color:rgba(255,255,255,0.9);
  white-space:nowrap
}
.row.muted{color:rgba(255,255,255,0.4);font-weight:400}
.only-badge{
  width:100%;display:flex;align-items:baseline;justify-content:center;gap:6px;
  background:rgba(99,102,241,0.15);border:1px solid rgba(99,102,241,0.3);
  border-radius:12px;padding:10px 0
}
.ob-num{font-size:24px;font-weight:800;color:#a5b4fc}
.ob-txt{font-size:13px;color:rgba(255,255,255,0.7)}
</style>
</head>
<body>
<div class="wrap">${blocks}</div>
</body>
</html>`
}

// ══════════════════════════════════════════════════════════
//  投票卡片（列表 / 详情）
//  后端 /api/bot/votes 返回结构：
//    mode:'list'   → rounds[]（含 options 计票 votes/score）
//    mode:'single' → round
// ══════════════════════════════════════════════════════════

function fmtDateTime(t?: string | null): string {
  if (!t) return '长期有效'
  const d = new Date(t)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getMonth() + 1}月${d.getDate()}日 ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

interface VoteOptionData {
  id: string
  text: string
  type?: string
  proposer?: string
  anonymous?: boolean
  votes?: number
  score?: number
}

interface VoteRoundData {
  id: string
  title: string
  description?: string
  status?: string
  createdAt?: string
  endAt?: string | null
  closedAt?: string | null
  maxVotesPerUser?: number
  baseWeight?: number
  weightRules?: { field?: string; op?: string; threshold?: number; weight?: number }[]
  options?: VoteOptionData[]
}

/** 投票列表卡片：多个轮次 → 标题+状态+选项数/票数 + 底部小字指定提示 */
export function voteListCard(rounds: VoteRoundData[]): string {
  const rows = rounds.map((r, i) => {
    const opts = r.options || []
    const totalVotes = opts.reduce((s, o) => s + (Number(o.votes) || 0), 0)
    const open = r.status === 'open'
    const badge = `<span class="rv-badge ${open ? 'open' : 'closed'}">${open ? '进行中' : '已结束'}</span>`
    return `<div class="rv">
      <div class="rv-num">${i + 1}</div>
      <div class="rv-main">
        <div class="rv-title">${escapeHtml(r.title)} ${badge}</div>
        <div class="rv-meta">${opts.length} 个选项 · ${totalVotes} 票 · 截止 ${escapeHtml(fmtDateTime(r.endAt))}</div>
      </div>
    </div>`
  }).join('\n')

  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif;
  background:linear-gradient(135deg,#0f0c29,#302b63,#24243e);min-height:100vh;padding:20px}
.wrap{width:520px;margin:0 auto;display:flex;flex-direction:column;gap:10px}
.head{display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:6px}
.head-title{font-size:22px;font-weight:700;color:#fff;letter-spacing:0.5px}
.head-sub{color:rgba(255,255,255,0.45);font-size:12px;margin-top:2px;letter-spacing:1px}
.rv{background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.1);
  border-radius:14px;padding:14px 16px;display:flex;gap:12px;align-items:flex-start}
.rv-num{flex-shrink:0;width:26px;height:26px;border-radius:50%;
  background:rgba(59,130,246,0.25);border:1px solid rgba(59,130,246,0.4);
  color:#93c5fd;font-size:13px;font-weight:700;display:flex;align-items:center;justify-content:center}
.rv-main{flex:1;min-width:0}
.rv-title{font-size:15px;font-weight:700;color:#fff;display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.rv-badge{font-size:11px;font-weight:700;padding:2px 10px;border-radius:20px}
.rv-badge.open{background:rgba(16,185,129,0.18);color:#34d399;border:1px solid rgba(16,185,129,0.35)}
.rv-badge.closed{background:rgba(239,68,68,0.15);color:#f87171;border:1px solid rgba(239,68,68,0.3)}
.rv-meta{font-size:12px;color:rgba(255,255,255,0.45);margin-top:5px}
.tip{text-align:center;color:rgba(255,255,255,0.4);font-size:12px;padding:6px 0 2px}
</style>
</head>
<body>
<div class="wrap">
  <div class="head"><div><div class="head-title">投票列表</div><div class="head-sub">VOTE LIST</div></div></div>
  ${rows}
  <div class="tip">发送「投票 名称」查看指定投票详情</div>
</div>
</body>
</html>`
}

/** 投票详情卡片：单轮次 → 状态/标题/说明/时间 + 选项计票进度条 + 规则说明 */
export function voteDetailCard(round: VoteRoundData): string {
  const open = round.status === 'open'
  const opts = round.options || []
  const totalScore = opts.reduce((s, o) => s + (Number(o.score) || 0), 0)
  const optionRows = opts.map(o => {
    const pct = totalScore > 0 ? Math.round((Number(o.score || 0) / totalScore) * 1000) / 10 : 0
    let tag = ''
    if (o.type === 'custom') {
      tag = o.anonymous
        ? '<span class="vo-tag anon">匿名提案</span>'
        : `<span class="vo-tag">${escapeHtml(o.proposer || '')} 提案</span>`
    }
    return `<div class="vo">
      <div class="vo-head">
        <span class="vo-text">${escapeHtml(o.text)}</span>
        ${tag}
        <span class="vo-score">${Number(o.score || 0)} 分 · ${Number(o.votes || 0)} 票</span>
      </div>
      <div class="vo-bar"><div class="vo-fill" style="width:${pct}%"></div></div>
      <div class="vo-pct">${pct}%</div>
    </div>`
  }).join('\n')

  const ruleDesc = (round.weightRules || [])
    .filter(r => r && r.field)
    .map(r => `游玩时长 ${r.op} ${r.threshold}h 加 ${r.weight} 分`)
    .join('，')
  const ruleLine = ruleDesc
    ? `基础 ${Number(round.baseWeight ?? 1)} 分 · ${ruleDesc}`
    : `基础 ${Number(round.baseWeight ?? 1)} 分`

  const badge = `<span class="vd-badge ${open ? 'open' : 'closed'}">${open ? '进行中' : '已结束'}</span>`
  const timeLine = open
    ? `<div class="vd-meta"><b>截止</b> ${escapeHtml(fmtDateTime(round.endAt))}</div>`
    : round.closedAt
      ? `<div class="vd-meta"><b>已结束于</b> ${escapeHtml(fmtDateTime(round.closedAt))}</div>`
      : '<div class="vd-meta"><b>已结束</b></div>'

  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","Noto Sans SC",sans-serif;
  background:linear-gradient(135deg,#0f0c29,#302b63,#24243e);min-height:100vh;padding:20px}
.wrap{width:520px;margin:0 auto;background:rgba(255,255,255,0.06);
  border:1px solid rgba(255,255,255,0.1);border-radius:18px;padding:22px 20px;
  box-shadow:0 25px 50px -12px rgba(0,0,0,0.6)}
.vd-top{display:flex;align-items:center;gap:10px;margin-bottom:8px}
.vd-badge{font-size:11px;font-weight:700;padding:3px 12px;border-radius:20px;letter-spacing:0.5px}
.vd-badge.open{background:rgba(16,185,129,0.18);color:#34d399;border:1px solid rgba(16,185,129,0.35)}
.vd-badge.closed{background:rgba(239,68,68,0.15);color:#f87171;border:1px solid rgba(239,68,68,0.3)}
.vd-title{font-size:20px;font-weight:700;color:#fff;line-height:1.4;word-break:break-word}
.vd-desc{margin-top:8px;padding:10px 12px;background:rgba(59,130,246,0.1);
  border-left:3px solid #3b82f6;border-radius:0 8px 8px 0;
  color:rgba(255,255,255,0.75);font-size:13px;line-height:1.7;white-space:pre-wrap;word-break:break-word}
.vd-meta{font-size:12px;color:rgba(255,255,255,0.45);margin-top:8px}
.vd-meta b{color:#60a5fa;font-weight:600}
.vd-rules{margin-top:10px;padding-top:10px;border-top:1px solid rgba(255,255,255,0.08);
  font-size:12px;color:rgba(255,255,255,0.45)}
.vd-options{margin-top:12px;display:flex;flex-direction:column;gap:14px}
.vo-head{display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.vo-text{font-size:14px;font-weight:600;color:rgba(255,255,255,0.92);flex:1;min-width:0;word-break:break-word}
.vo-tag{font-size:10px;font-weight:600;padding:2px 8px;border-radius:8px;
  background:rgba(59,130,246,0.18);color:#93c5fd;border:1px solid rgba(59,130,246,0.3);white-space:nowrap}
.vo-tag.anon{background:rgba(107,114,128,0.2);color:rgba(255,255,255,0.6);border-color:rgba(107,114,128,0.3)}
.vo-score{font-size:12px;color:#93c5fd;font-weight:700;white-space:nowrap}
.vo-bar{margin-top:6px;height:8px;background:rgba(255,255,255,0.08);border-radius:4px;overflow:hidden}
.vo-fill{height:100%;border-radius:4px;background:linear-gradient(90deg,#3b82f6,#60a5fa);transition:width .4s}
.vo-pct{font-size:11px;color:rgba(255,255,255,0.4);text-align:right;margin-top:2px}
</style>
</head>
<body>
<div class="wrap">
  <div class="vd-top">${badge}<span style="font-size:12px;color:rgba(255,255,255,0.35)">${escapeHtml(fmtDateTime(round.createdAt))} 发起</span></div>
  <div class="vd-title">${escapeHtml(round.title)}</div>
  ${round.description ? `<div class="vd-desc">${escapeHtml(round.description)}</div>` : ''}
  ${timeLine}
  <div class="vd-rules">每用户可投 ${Number(round.maxVotesPerUser ?? 1)} 票 · ${ruleLine}</div>
  <div class="vd-options">${optionRows}</div>
</div>
</body>
</html>`
}

