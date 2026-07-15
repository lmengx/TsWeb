import { chromium } from 'playwright'

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

function escapeHtml(s: string): string {
  return s.replace(/&/g, '&').replace(/</g, '<').replace(/>/g, '>').replace(/"/g, '"')
}
