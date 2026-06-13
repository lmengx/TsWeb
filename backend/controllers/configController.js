import { getTshockConfig, updateTshockConfig, isTshockConfigured } from '../config.js'

export const getConfigStatus = async (req, res) => {
  try {
    const configured = await isTshockConfigured()
    const tshockConfig = await getTshockConfig()
    res.json({
      configured,
      tshock: {
        hasDatabasePath: !!tshockConfig.databasePath,
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
    const { databasePath, host, port, apiKey } = req.body
    const config = await updateTshockConfig({ databasePath, host, port, apiKey })
    res.json({ success: true, config })
  } catch (error) {
    res.status(500).json({ error: error.message })
  }
}