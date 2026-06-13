import { ref, watch } from 'vue'

const isDark = ref(localStorage.getItem('tsweb-theme') !== 'light')

export function useTheme() {
  const toggleTheme = () => {
    isDark.value = !isDark.value
  }

  const setTheme = (dark) => {
    isDark.value = dark
  }

  watch(isDark, (newVal) => {
    localStorage.setItem('tsweb-theme', newVal ? 'dark' : 'light')
    document.documentElement.classList.toggle('dark', newVal)
    document.documentElement.classList.toggle('light', !newVal)
  }, { immediate: true })

  return {
    isDark,
    toggleTheme,
    setTheme
  }
}

export { isDark }
