import { readFileSync, existsSync } from 'fs'
import { join } from 'path'
import { escapeHtml, frame } from '../frame'

// 图标目录：源码/编译产物同级时按相对路径定位；
// 若插件被打包到别处（__dirname 变化），可用环境变量显式指定。
const assetsDir =
  process.env.TSWEB_BOSS_ASSETS_DIR || join(__dirname, '..', '..', '..', 'assets', 'boss')

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

/** 加载图片为 base64 data URI（卡片不得外链资源），文件不存在返回空字符串 */
function loadImageBase64(filename: string): string {
  if (!filename) return ''
  const filePath = join(assetsDir, filename)
  if (!existsSync(filePath)) return ''
  const buf = readFileSync(filePath)
  const ext = filename.split('.').pop()?.toLowerCase()
  const mime = ext === 'webp' ? 'image/webp' : 'image/png'
  return `data:${mime};base64,${buf.toString('base64')}`
}

export interface BossData {
  Name: string
  NPCID: number
  KillCount: number
  IsKilled: boolean
}

export interface EventData {
  Name: string
  EventID: number
  IsCompleted: boolean
}

export interface BossProgressData {
  TotalBossCount: number
  KilledCount: number
  BossProgressPercent: number
  Bosses: BossData[]
  TotalEventCount: number
  CompletedEventCount: number
  EventProgressPercent: number
  Events: EventData[]
  /** 后端附带：当前进度所属服务器 */
  server?: { name?: string } | null
}

/** Boss 击杀 / 事件进度卡片（调用方截图选择器：.wrap） */
export function bossProgressCard(data: BossProgressData): string {
  const serverName = data.server?.name || ''
  const serverTag = serverName
    ? `<div class="server-head"><span class="server-name">${escapeHtml(serverName)}</span></div>`
    : ''

  const tile = (name: string, done: boolean, imgMap: Record<string, string>, count?: number) => {
    const src = loadImageBase64(imgMap[name] || '')
    return `<div class="bc ${done ? 'done' : ''}">
      <div class="bc-img">
        ${src ? `<img src="${src}" alt="${escapeHtml(name)}">` : '<div class="bc-placeholder">?</div>'}
        <div class="bc-badge ${done ? 'bc-ok' : 'bc-no'}">${done ? '✓' : '✗'}</div>
      </div>
      <div class="bc-name">${escapeHtml(name)}</div>
      ${done && count ? `<div class="bc-count">${count} 击杀</div>` : ''}
    </div>`
  }

  const bossCards = (data.Bosses || [])
    .map(b => tile(b.Name, !!b.IsKilled, bossImageMap, b.KillCount))
    .join('\n')
  const eventCards = (data.Events || [])
    .map(e => tile(e.Name, !!e.IsCompleted, eventImageMap))
    .join('\n')

  return frame(`${serverTag}
  <div class="section">
    <div class="section-head">
      <h3>Boss 击杀进度</h3>
      <span class="pct green">${Number(data.KilledCount) || 0}/${Number(data.TotalBossCount) || 0}</span>
    </div>
    <div class="bar"><div class="bar-inner green" style="width:${Number(data.BossProgressPercent) || 0}%"></div></div>
    <div class="grid">${bossCards}</div>
  </div>
  <div class="section">
    <div class="section-head">
      <h3>事件进度</h3>
      <span class="pct purple">${Number(data.CompletedEventCount) || 0}/${Number(data.TotalEventCount) || 0}</span>
    </div>
    <div class="bar"><div class="bar-inner purple" style="width:${Number(data.EventProgressPercent) || 0}%"></div></div>
    <div class="grid">${eventCards}</div>
  </div>`, { wrapClass: 'w-wide' })
}
