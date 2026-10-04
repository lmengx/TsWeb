/**
 * 临时封禁时长的**唯一权威定义**（后端侧）。
 *
 * 为什么单独抽成一个文件：封禁时长这条链路上有三个运行时要判断"多久算太久"——
 * 面板（提前给提示）、后端（给可读报错）、游戏服插件（最后一道防线）。
 * 之前 JS 侧的校验与格式化都写在控制器里，上限常量散落在控制器和面板两处，
 * 改一处忘另一处就会出现"面板放行、后端拒绝"这种对不上的行为。
 * 现在 JS 侧收敛到这一个模块，并由 test/banDuration.test.js 盯住。
 *
 * 插件侧（plugin/QueryUsers.cs 的 MaxBanDurationSeconds）是 C#，无法共享本常量，
 * 它那份是防御性的最后一道；两边若不一致，插入会被插件以 400 拒绝并把原因回给面板，
 * 属于可见失败，不会静默放宽。
 */

/** 临时封禁上限：100 年（36500 天）。 */
export const MAX_BAN_DURATION_SECONDS = 100 * 365 * 24 * 60 * 60

/**
 * 校验并归一化请求里的 durationSeconds。
 * 约定：不传 / null / 空串 = 永久封禁（与插件侧"参数缺省即永久"同一口径）。
 *
 * @param {unknown} raw 请求体里的 durationSeconds 原值
 * @returns {{ ok: true, seconds: number|null } | { ok: false, error: string }}
 */
export function parseBanDurationSeconds(raw) {
  if (raw === undefined || raw === null || raw === '') {
    return { ok: true, seconds: null }
  }

  // 先卡类型：Number(true) === 1、Number([]) === 0、Number([30]) === 30 这类隐式转换
  // 会把"传错了类型"当成合法时长，封出一个谁也没打算要的期限
  if (typeof raw !== 'number' && typeof raw !== 'string') {
    return { ok: false, error: 'durationSeconds 必须是大于 0 的整数秒' }
  }

  const seconds = Number(raw)

  if (!Number.isInteger(seconds) || seconds <= 0) {
    return { ok: false, error: 'durationSeconds 必须是大于 0 的整数秒' }
  }

  if (seconds > MAX_BAN_DURATION_SECONDS) {
    return {
      ok: false,
      error: `临时封禁最长 ${MAX_BAN_DURATION_SECONDS / 86400} 天，需要更久请选永久封禁`
    }
  }

  return { ok: true, seconds }
}

/**
 * 把秒数说成人话，用于审计详情等给人看的地方。
 * 取最大的能整除的单位，避免出现"30 天"被写成"720 小时"。
 */
export function formatBanDuration(seconds) {
  if (seconds >= 86400 && seconds % 86400 === 0) return `${seconds / 86400} 天`
  if (seconds >= 3600 && seconds % 3600 === 0) return `${seconds / 3600} 小时`
  if (seconds >= 60 && seconds % 60 === 0) return `${seconds / 60} 分钟`
  return `${seconds} 秒`
}
