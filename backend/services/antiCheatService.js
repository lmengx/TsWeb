import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

class AntiCheatService {
  constructor() {
    this.itemConfigPath = path.join(__dirname, '../config/反作弊/物品数量限制.json');
    this.projConfigPath = path.join(__dirname, '../config/反作弊/弹幕违禁.json');
    this.itemConfig = null;
    this.projConfig = null;
  }

  loadItemConfig() {
    try {
      const data = fs.readFileSync(this.itemConfigPath, 'utf8');
      this.itemConfig = JSON.parse(data);
      return this.itemConfig;
    } catch (error) {
      console.error('Failed to load item anti-cheat config:', error.message);
      return null;
    }
  }

  loadProjConfig() {
    try {
      const data = fs.readFileSync(this.projConfigPath, 'utf8');
      this.projConfig = JSON.parse(data);
      return this.projConfig;
    } catch (error) {
      console.error('Failed to load projectile anti-cheat config:', error.message);
      return null;
    }
  }

  getItemConfig() {
    if (!this.itemConfig) {
      return this.loadItemConfig();
    }
    return this.itemConfig;
  }

  getProjConfig() {
    if (!this.projConfig) {
      return this.loadProjConfig();
    }
    return this.projConfig;
  }

  saveProjConfig(config) {
    try {
      const dir = path.dirname(this.projConfigPath);
      if (!fs.existsSync(dir)) {
        fs.mkdirSync(dir, { recursive: true });
      }
      fs.writeFileSync(this.projConfigPath, JSON.stringify(config, null, 2), 'utf8');
      this.projConfig = config;
      return true;
    } catch (error) {
      console.error('Failed to save projectile anti-cheat config:', error.message);
      return false;
    }
  }

  getItemName(id) {
    const config = this.getItemConfig();
    if (!config || !config.items) return null;

    const item = config.items.find(i => i.id === id);
    return item ? item.name : null;
  }

  isAnomalyItem(id, stack) {
    const config = this.getItemConfig();
    if (!config || !config.items) return false;

    const item = config.items.find(i => i.id === id);
    if (item) {
      return stack > item.maxStack;
    }

    return false;
  }
}

export default new AntiCheatService();
