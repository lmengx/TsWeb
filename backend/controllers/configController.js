import { getTshockConfig, updateTshockConfig, isTshockConfigured } from '../config.js'
import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'
import tshockService from '../services/tshockService.js'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const configDir = path.join(__dirname, '../config/反作弊')

export const getConfigStatus = async (req, res) => {
  try {
    const configured = await isTshockConfigured()
    const tshockConfig = await getTshockConfig()
    res.json({
      configured,
      tshock: {
        hasHost: !!tshockConfig.host,
        hasPort: !!tshockConfig.port,
        hasApiKey: !!tshockConfig.apiKey
      }
    })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

export const getTshockSettings = async (req, res) => {
  try {
    const config = await getTshockConfig()
    res.json(config)
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

export const updateTshockSettings = async (req, res) => {
  try {
    const { host, port, apiKey } = req.body
    const config = await updateTshockConfig({ host, port, apiKey })
    res.json({ success: true, config })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}

export const getConfigFile = async (req, res) => {
  try {
    const { name } = req.query
    if (!name) {
      return res.status(400).json({ status: '400', error: 'Missing file name' })
    }

    const filePath = path.join(configDir, name)
    
    if (!fs.existsSync(filePath)) {
      return res.status(404).json({ status: '404', error: 'File not found' })
    }

    const content = fs.readFileSync(filePath, 'utf8')
    res.json({ status: '200', content })
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const saveConfigFile = async (req, res) => {
  try {
    const { name, content } = req.body
    if (!name || !content) {
      return res.status(400).json({ status: '400', error: 'Missing parameters' })
    }

    const filePath = path.join(configDir, name)
    
    fs.writeFileSync(filePath, content, 'utf8')
    res.json({ status: '200', message: 'File saved successfully' })
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const getTsWebConfig = async (req, res) => {
  try {
    const result = await tshockService.getTsWebConfig()
    res.json(result)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}

export const setTsWebConfig = async (req, res) => {
  try {
    const result = await tshockService.setTsWebConfig(req.body)
    res.json(result)
  } catch (error) {
    res.status(500).json({ status: '500', error: error.message })
  }
}
