import { Router } from 'express'
import tshockService from '../services/tshockService.js'
import { enableAntiCheat, pushDefaultToPlugin } from '../services/anticheatDefaults.js'
import { verifyToken, requireManager } from '../middlewares/authMiddleware.js'

const router = Router()

// 反作弊配置：admin + subadmin（用户确认 subadmin 可用）
router.use(verifyToken, requireManager)

// 打开反作弊（启用检测开关 关→开 时调用）：
//   item / proj 任一为 true 时，若插件端无有效配置 → 下发后端默认配置（启用=true）；
//   已有配置 → 仅翻转插件端启用开关。返回逐项结果。
router.post('/enable', async (req, res) => {
  const { item, proj } = req.body || {}
  if (!item && !proj) {
    return res.status(400).json({ status: '400', error: '缺少 item / proj 启用标记' })
  }
  const results = {}
  try {
    if (item) results.item = await enableAntiCheat('item')
    if (proj) results.proj = await enableAntiCheat('proj')
    const failed = Object.values(results).some(r => r?.status === 'error')
    res.status(failed ? 502 : 200).json({ status: failed ? '502' : '200', results })
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

// 手动下发默认配置（管理端「应用默认配置」按钮，可选）
router.post('/apply-defaults', async (req, res) => {
  const { item, proj } = req.body || {}
  const kinds = []
  if (item) kinds.push('item')
  if (proj) kinds.push('proj')
  if (kinds.length === 0) {
    return res.status(400).json({ status: '400', error: '缺少 item / proj 标记' })
  }
  const results = {}
  try {
    for (const kind of kinds) results[kind] = await pushDefaultToPlugin(kind)
    const failed = Object.values(results).some(r => r?.status === 'error')
    res.status(failed ? 502 : 200).json({ status: failed ? '502' : '200', results })
  } catch (err) {
    res.status(500).json({ status: '500', error: err.message })
  }
})

router.get('/config', (req, res) => {
  tshockService.getItemConfig()
    .then(data => {
      if (data.error) {
        res.status(500).json({ status: '500', error: data.error })
      } else {
        res.json({ status: '200', config: data })
      }
    })
    .catch(error => {
      res.status(500).json({ status: '500', error: error.message })
    })
})

router.get('/proj-config', (req, res) => {
  tshockService.getProjConfig()
    .then(data => {
      if (data.error) {
        res.status(500).json({ status: '500', error: data.error })
      } else {
        res.json({ status: '200', config: data })
      }
    })
    .catch(error => {
      res.status(500).json({ status: '500', error: error.message })
    })
})

router.post('/proj-config', (req, res) => {
  const config = req.body
  if (!config) {
    return res.status(400).json({ status: '400', error: 'Missing config data' })
  }

  tshockService.saveProjConfig(config)
    .then(data => {
      if (data.error) {
        res.status(500).json({ status: '500', error: data.error })
      } else {
        res.json({ status: '200', message: 'Projectile config saved successfully' })
      }
    })
    .catch(error => {
      res.status(500).json({ status: '500', error: error.message })
    })
})

router.get('/item-config', (req, res) => {
  tshockService.getItemConfig()
    .then(data => {
      if (data.error) {
        res.status(500).json({ status: '500', error: data.error })
      } else {
        res.json({ status: '200', config: data })
      }
    })
    .catch(error => {
      res.status(500).json({ status: '500', error: error.message })
    })
})

router.post('/item-config', (req, res) => {
  const config = req.body
  if (!config) {
    return res.status(400).json({ status: '400', error: 'Missing config data' })
  }

  tshockService.saveItemConfig(config)
    .then(data => {
      if (data.error) {
        res.status(500).json({ status: '500', error: data.error })
      } else {
        res.json({ status: '200', message: 'Item config saved successfully' })
      }
    })
    .catch(error => {
      res.status(500).json({ status: '500', error: error.message })
    })
})

// 反恶性 bug 修复配置（总开关 + 4 项子功能开关）
// 直读/直写插件端 /data/bugfix 与 /data/bugfix/set（缺省参数保持插件原值）
router.get('/bugfix', (req, res) => {
  tshockService.getBugfixConfig()
    .then(data => {
      if (data.error) {
        res.status(500).json({ status: '500', error: data.error })
      } else {
        res.json({ status: '200', config: data })
      }
    })
    .catch(error => {
      res.status(500).json({ status: '500', error: error.message })
    })
})

router.post('/bugfix/set', (req, res) => {
  const { enabled, loginFix, chestFix, minionLimit, lightning } = req.body || {}
  if (enabled === undefined && loginFix === undefined && chestFix === undefined &&
      minionLimit === undefined && lightning === undefined) {
    return res.status(400).json({ status: '400', error: '缺少开关参数（enabled/loginFix/chestFix/minionLimit/lightning）' })
  }
  tshockService.saveBugfixConfig({ enabled, loginFix, chestFix, minionLimit, lightning })
    .then(data => {
      if (data.error) {
        res.status(500).json({ status: '500', error: data.error })
      } else {
        res.json({ status: '200', message: 'BugFixes 配置已保存', config: data })
      }
    })
    .catch(error => {
      res.status(500).json({ status: '500', error: error.message })
    })
})

router.post('/check-anomaly', (req, res) => {
  const { id, stack } = req.body
  if (id === undefined || stack === undefined) {
    return res.json({ status: '400', error: 'Missing parameters' })
  }

  tshockService.checkAnomalyItem(id, stack)
    .then(data => {
      res.json({
        status: '200',
        isAnomaly: data.isAnomaly || false,
        itemName: data.itemName || null
      })
    })
    .catch(error => {
      res.json({ status: '200', isAnomaly: false, itemName: null })
    })
})

export default router
