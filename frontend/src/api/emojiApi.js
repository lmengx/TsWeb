import { get, post } from '../utils/api.js'

// 表情指令 API（通过后端代理转发到 TShock /data/emoji/*）

export async function getEmoteConfig() {
  const res = await get('/api/tshock/data/emoji/config')
  return res.json()
}

export async function saveEmoteConfig(config) {
  const res = await post('/api/tshock/data/emoji/config/set', { config: JSON.stringify(config) })
  return res.json()
}

// 常用表情 ID 预设（来自 EmoteID.cs，ID → 中文名）
export const EMOTE_PRESETS = [
  { id: 0, name: '爱心' },
  { id: 1, name: '愤怒' },
  { id: 2, name: '哭泣' },
  { id: 3, name: '惊讶/警惕' },
  { id: 4, name: '雨' },
  { id: 5, name: '闪电' },
  { id: 6, name: '彩虹' },
  { id: 7, name: '戒指' },
  { id: 8, name: '中毒' },
  { id: 9, name: '燃烧' },
  { id: 10, name: '沉默' },
  { id: 11, name: '诅咒' },
  { id: 12, name: '蜜蜂' },
  { id: 13, name: '史莱姆' },
  { id: 15, name: '大笑' },
  { id: 16, name: '恐惧' },
  { id: 17, name: '音符' },
  { id: 18, name: '血月' },
  { id: 19, name: '日食' },
  { id: 20, name: '南瓜月' },
  { id: 21, name: '雪人军团' },
  { id: 36, name: '石头剪刀布' },
  { id: 39, name: '克苏鲁之眼' },
  { id: 40, name: '世界吞噬者' },
  { id: 41, name: '克苏鲁之脑' },
  { id: 42, name: '蜂后' },
  { id: 43, name: '骷髅王' },
  { id: 44, name: '血肉墙' },
  { id: 45, name: '毁灭者' },
  { id: 46, name: '机械骷髅王' },
  { id: 47, name: '双子魔眼' },
  { id: 48, name: '世纪之花' },
  { id: 49, name: '石巨人' },
  { id: 50, name: '猪龙鱼公爵' },
  { id: 51, name: '史莱姆王' },
  { id: 52, name: '拜月教教徒' },
  { id: 53, name: '月亮领主' },
  { id: 61, name: '僵尸' },
  { id: 62, name: '兔子' },
  { id: 73, name: '生命药水' },
  { id: 74, name: '魔力药水' },
  { id: 78, name: '剑' },
  { id: 79, name: '钓鱼竿' },
  { id: 80, name: '捕虫网' },
  { id: 81, name: '雷管' },
  { id: 84, name: '墓碑' },
  { id: 85, name: '金币堆' },
  { id: 86, name: '钻戒' },
  { id: 87, name: '困惑' },
  { id: 88, name: '亲吻' },
  { id: 89, name: '睡觉' },
  { id: 90, name: '镐' },
  { id: 91, name: '跑步' },
  { id: 92, name: '踢' },
  { id: 93, name: '战斗' },
  { id: 94, name: '吃饭' },
  { id: 95, name: '晴天' },
  { id: 96, name: '多云' },
  { id: 97, name: '暴风雨' },
  { id: 98, name: '暴雪' },
  { id: 99, name: '陨石' },
  { id: 134, name: '悲伤' },
  { id: 135, name: '生气' },
  { id: 136, name: '开心' },
  { id: 143, name: '光之女皇' },
  { id: 144, name: '史莱姆皇后' }
]
