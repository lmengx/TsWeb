import voteService from '../services/voteService.js'
import audit from '../services/auditLogger.js'

// ═══════════════════════════════════════════════════════════
// 投票控制器
// ═══════════════════════════════════════════════════════════

// ── 管理端（requireAdmin）──

/** 创建轮次：标题 / 说明 / 初始选项 / 每用户可投数 / 初始权重 / 加权规则 / 提案开关+上限 / 截止时间 */
export const createRound = async (req, res) => {
  try {
    const { title, description, options, maxVotesPerUser, baseWeight, allowProposals, maxProposalsPerUser, weightRules, endAt } = req.body
    const round = await voteService.createRound({
      title, description, options, maxVotesPerUser, baseWeight, allowProposals, maxProposalsPerUser, weightRules, endAt
    })
    audit.record('vote.round.create', { id: round.id, title: round.title, actor: req.user?.username })
    res.json({ success: true, round })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

/** 管理视图：全部轮次（含计票与状态） */
export const listAdminRounds = async (_req, res) => {
  try {
    const rounds = await voteService.listRounds({ includeClosed: true })
    res.json({ rounds })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/**
 * 编辑轮次（发布后修改发起参数）：标题/说明/截止任意状态可改；
 * 规则类参数（可投数/权重/加权规则/提案开关/未绑定开关）仅进行中可改，且只影响后续投票
 */
export const updateRound = async (req, res) => {
  try {
    const round = await voteService.updateRound(req.params.id, req.body || {})
    audit.record('vote.round.update', { id: round.id, title: round.title, changedKeys: Object.keys(req.body || {}), actor: req.user?.username })
    res.json({ success: true, round })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

/** 管理员添加选项（仅进行中轮次） */
export const addOption = async (req, res) => {
  try {
    const { text } = req.body || {}
    if (!text) return res.status(400).json({ error: '缺少选项内容' })
    const round = await voteService.getRound(req.params.id)
    if (!round) return res.status(404).json({ error: '轮次不存在' })
    const option = await voteService.addOption(round.id, String(text), req.user?.username)
    audit.record('vote.round.option.add', {
      roundId: round.id, title: round.title, optionId: option.id, text: option.text, actor: req.user?.username
    })
    res.json({ success: true, option })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

/** 管理员删除选项（仅进行中轮次；连带真实删除指向它的选票） */
export const removeOption = async (req, res) => {
  try {
    const round = await voteService.getRound(req.params.id)
    if (!round) return res.status(404).json({ error: '轮次不存在' })
    const result = await voteService.removeOption(round.id, req.params.optionId, req.user?.username)
    audit.record('vote.round.option.remove', {
      roundId: round.id, title: round.title, optionId: result.option.id,
      text: result.option.text, removedVotes: result.removedVotes, actor: req.user?.username
    })
    res.json({ success: true, option: result.option, removedVotes: result.removedVotes })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

/** 手动结束轮次 */
export const closeRound = async (req, res) => {
  try {
    const round = await voteService.closeRound(req.params.id, req.user?.username)
    audit.record('vote.round.close', { id: round.id, title: round.title, actor: req.user?.username })
    res.json({ success: true, round })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

/**
 * 管理端明细：投票明细 + 提案明细（匿名对管理员无效，全部显示）
 */
export const getRoundDetail = async (req, res) => {
  try {
    const detail = await voteService.getRoundDetail(req.params.id)
    if (!detail) return res.status(404).json({ error: '轮次不存在' })
    res.json({ success: true, round: detail })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/** 删除轮次（连带投票记录） */
export const deleteRound = async (req, res) => {
  try {
    const ok = await voteService.deleteRound(req.params.id)
    if (!ok) return res.status(404).json({ error: '轮次不存在' })
    audit.record('vote.round.delete', { id: req.params.id, actor: req.user?.username })
    res.json({ success: true })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/** 归档轮次（从玩家页隐藏，可逆） */
export const archiveRound = async (req, res) => {
  try {
    const round = await voteService.archiveRound(req.params.id, req.user?.username)
    audit.record('vote.round.archive', { id: round.id, title: round.title, actor: req.user?.username })
    res.json({ success: true, round })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

/** 取消归档（重新对玩家页可见） */
export const unarchiveRound = async (req, res) => {
  try {
    const round = await voteService.unarchiveRound(req.params.id)
    audit.record('vote.round.unarchive', { id: round.id, title: round.title, actor: req.user?.username })
    res.json({ success: true, round })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

// ── 玩家端（requirePlayer / 公开）──

/** 公开：轮次 + 结果（匿名可看，不含个人状态、不暴露投票明细；仅未归档的活跃中轮次） */
export const listPublicRounds = async (_req, res) => {
  try {
    const rounds = await voteService.listRounds({ includeClosed: true, excludeArchived: true })
    res.json({ rounds })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/** 我的状态：未归档轮次 + 个人已投/剩余可投/提案状态/本轮权重（登录后前端调用补充） */
export const listMyState = async (req, res) => {
  try {
    const rounds = await voteService.listRounds({ includeClosed: true, excludeArchived: true, username: req.user.username })
    res.json({ rounds })
  } catch (err) {
    res.status(500).json({ error: err.message })
  }
}

/** 投一票 */
export const castVote = async (req, res) => {
  try {
    const { optionId } = req.body
    if (!optionId) return res.status(400).json({ error: '缺少 optionId' })
    const round = await voteService.getRound(req.params.id)
    if (!round) return res.status(404).json({ error: '轮次不存在' })
    const result = await voteService.castVote(round, req.user.username, req.user.qq || '', optionId)
    audit.record('vote.cast', { roundId: round.id, username: req.user.username, optionId, weight: result.vote.weight })
    res.json({ success: true, ...result })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}

/** 提交自定义提案（匿名可选） */
export const propose = async (req, res) => {
  try {
    const { text, anonymous } = req.body
    if (!text) return res.status(400).json({ error: '缺少提案内容' })
    const round = await voteService.getRound(req.params.id)
    if (!round) return res.status(404).json({ error: '轮次不存在' })
    const result = await voteService.propose(round, req.user.username, String(text), !!anonymous)
    audit.record('vote.propose', { roundId: round.id, username: req.user.username, optionId: result.option.id, anonymous: !!anonymous })
    res.json({ success: true, ...result })
  } catch (err) {
    res.status(400).json({ error: err.message })
  }
}
