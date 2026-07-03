import { Router } from 'express'
import tshockService from '../services/tshockService.js'

const router = Router()

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