/**
 * banDuration 单元测试（node --test，无第三方依赖）。
 *
 * 覆盖的是这条链路上最容易出错、也最难靠手工点出来的几种情况：
 * 永久与临时的分界（不传 = 永久，而不是"传个很大数字当永久"）、
 * 隐式类型转换把脏输入当成合法时长、以及上限边界。
 */
import test from 'node:test'
import assert from 'node:assert/strict'

import {
  MAX_BAN_DURATION_SECONDS,
  parseBanDurationSeconds,
  formatBanDuration
} from '../lib/banDuration.js'

test('不传时长 = 永久封禁（保持旧调用方语义）', () => {
  for (const raw of [undefined, null, '']) {
    assert.deepEqual(parseBanDurationSeconds(raw), { ok: true, seconds: null })
  }
})

test('合法的临时时长按原值通过', () => {
  assert.deepEqual(parseBanDurationSeconds(3600), { ok: true, seconds: 3600 })
  assert.deepEqual(parseBanDurationSeconds(86400), { ok: true, seconds: 86400 })
  assert.deepEqual(parseBanDurationSeconds(2592000), { ok: true, seconds: 2592000 })
  // 前端走 JSON，数字类型；但也允许数字字符串
  assert.deepEqual(parseBanDurationSeconds('3600'), { ok: true, seconds: 3600 })
})

test('非正数、小数、非数字一律拒绝', () => {
  for (const raw of [0, -1, 1.5, '0', '-60', 'abc', ' ', NaN, Infinity]) {
    const result = parseBanDurationSeconds(raw)
    assert.equal(result.ok, false, `期望 ${String(raw)} 被拒绝`)
    assert.match(result.error, /大于 0 的整数秒/)
  }
})

test('拒绝隐式类型转换：true/false/数组/对象不能被当成时长', () => {
  // Number(true) === 1、Number([]) === 0、Number([30]) === 30，若不卡类型就会当成合法秒数
  for (const raw of [true, false, [], [30], {}, { seconds: 60 }]) {
    const result = parseBanDurationSeconds(raw)
    assert.equal(result.ok, false, `期望 ${JSON.stringify(raw)} 被拒绝`)
  }
})

test('上限边界：正好 100 年放行，多 1 秒拒绝', () => {
  assert.deepEqual(parseBanDurationSeconds(MAX_BAN_DURATION_SECONDS), {
    ok: true,
    seconds: MAX_BAN_DURATION_SECONDS
  })

  const over = parseBanDurationSeconds(MAX_BAN_DURATION_SECONDS + 1)
  assert.equal(over.ok, false)
  // 报错里要给出人看得懂的天数，管理员才知道该改成多少
  assert.match(over.error, /36500 天/)
})

test('时长上限就是 100 年（36500 天）', () => {
  assert.equal(MAX_BAN_DURATION_SECONDS, 100 * 365 * 24 * 60 * 60)
  assert.equal(MAX_BAN_DURATION_SECONDS / 86400, 36500)
})

test('formatBanDuration 取最大可整除单位', () => {
  assert.equal(formatBanDuration(3600), '1 小时')
  assert.equal(formatBanDuration(86400), '1 天')
  assert.equal(formatBanDuration(2592000), '30 天')
  assert.equal(formatBanDuration(60), '1 分钟')
  assert.equal(formatBanDuration(5400), '90 分钟')
  assert.equal(formatBanDuration(45), '45 秒')
})
