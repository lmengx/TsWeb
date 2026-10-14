import test from 'node:test'
import assert from 'node:assert/strict'
import { setBossConfig } from '../controllers/configController.js'
import { TShockService } from '../services/tshockService.js'
import audit from '../services/auditLogger.js'
import { getEventMeta } from '../services/auditEvents.js'

function mockRequest(t, body, result) {
  const requests = []
  const events = []
  const responses = []
  t.mock.method(TShockService.prototype, 'setBossConfig', async params => {
    requests.push(params)
    return result
  })
  t.mock.method(audit, 'safeRecord', (event, context) => events.push({ event, context }))
  const req = { body, user: { username: '管理甲' }, headers: { 'x-server-id': 'server-a' } }
  const res = { json: value => responses.push(value) }
  return { req, res, requests, events, responses }
}

for (const policy of ['allow', 'deny', 'auto']) {
  test(`全部 ${policy} 只下发一次，只记录一条全部事件`, async t => {
    const fixture = mockRequest(t, { summonTarget: 'all', summonPolicy: policy },
      { status: '200', target: 'all', policy })
    await setBossConfig(fixture.req, fixture.res)
    assert.equal(fixture.requests.length, 1)
    assert.equal(fixture.events.length, 1)
    assert.equal(fixture.events[0].event, 'config.boss.summon')
    assert.equal(fixture.events[0].context.target, '全部 Boss')
    assert.equal(fixture.events[0].context.policy, policy)
    assert.equal(fixture.events[0].context.serverId, 'server-a')
    assert.equal(fixture.responses[0].status, '200')
    assert.ok(getEventMeta(fixture.events[0].event))
  })
}

test('单个 Boss 记录目标、操作，操作者来自登录身份', async t => {
  const fixture = mockRequest(t, { summonTarget: '血肉墙', summonPolicy: 'deny', actor: '伪造身份' },
    { status: '200', target: '血肉墙', policy: 'deny' })
  await setBossConfig(fixture.req, fixture.res)
  assert.equal(fixture.requests[0].actor, '管理甲')
  assert.equal(fixture.events.length, 1)
  assert.equal(fixture.events[0].context.actor, '管理甲')
  assert.equal(fixture.events[0].context.target, '血肉墙')
  assert.equal(fixture.events[0].context.action, '禁止召唤')
})

test('服务器拒绝或保存失败时，不记录成功操作', async t => {
  const fixture = mockRequest(t, { summonTarget: 'all', summonPolicy: 'deny' },
    { status: '500', error: '保存失败' })
  await setBossConfig(fixture.req, fixture.res)
  assert.equal(fixture.events.length, 0)
  assert.equal(fixture.responses[0].status, '500')
})

test('防逃课与其他 Boss 配置修改仍记录一次配置事件', async t => {
  const settings = { quitLimitEnabled: true, ghostRespawnSeconds: 30, lateCompEnabled: true }
  const fixture = mockRequest(t, settings, { status: '200' })
  await setBossConfig(fixture.req, fixture.res)
  assert.equal(fixture.events.length, 1)
  assert.equal(fixture.events[0].event, 'config.boss.set')
  assert.deepEqual(fixture.events[0].context.detail, settings)
})
