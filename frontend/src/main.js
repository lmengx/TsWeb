import { createApp } from 'vue'
import './styles/theme.css'
import './style.css'
import App from './App.vue'
import router from './router'

const app = createApp(App)

// 全局错误兜底：任何组件渲染/生命周期异常都在控制台留下完整堆栈，
// 避免"黑屏但无从查起"（配合 fade-slide 过渡降级，黑屏类问题均可定位）
app.config.errorHandler = (err, instance, info) => {
  console.error('[TSWeb Vue Error]', info, err)
  if (instance) {
    console.error('[TSWeb Vue Error] 组件:', instance.$options?.name || instance.$.type?.__name || 'anonymous')
  }
}

app.use(router)

app.mount('#app')
