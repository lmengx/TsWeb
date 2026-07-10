import onlineService from '../services/onlineService.js'

export const getHourlyOnline = async (req, res) => {
  const { date } = req.query
  if (!date) {
    return res.status(400).json({ error: 'date parameter is required (yyyy-MM-dd)' })
  }
  const result = await onlineService.getHourlyOnline(date)
  res.json(result)
}

export const getRanking = async (req, res) => {
  const mode = req.query.mode || 'today'
  const result = await onlineService.getRanking(mode)
  res.json(result)
}

export const getPlayerCalendar = async (req, res) => {
  const { name, year } = req.query
  if (!name) {
    return res.status(400).json({ error: 'name parameter is required' })
  }
  const yearNum = parseInt(year) || new Date().getFullYear()
  const result = await onlineService.getPlayerCalendar(name, yearNum)
  res.json(result)
}

export const getRankingStats = async (req, res) => {
  const type = req.query.type || 'online'
  const page = parseInt(req.query.page) || 1
  const pageSize = parseInt(req.query.pageSize) || 10
  const result = await onlineService.getRankingStats(type, page, pageSize)
  res.json(result)
}
