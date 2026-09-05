<script setup>
import { RouterView, useRoute } from 'vue-router'
import RouteProgress from './components/RouteProgress.vue'

const route = useRoute()
// 顶层 key 用"一级路由"（matched[0]），避免 console 子页面切换时整个框架重播动画
const topKey = () => route.matched[0]?.path || route.path
</script>

<template>
  <RouteProgress />
  <router-view v-slot="{ Component }">
    <transition name="fade-slide" mode="out-in">
      <component :is="Component" :key="topKey()" />
    </transition>
  </router-view>
</template>

<style>
* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

body {
  font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
  background: #f5f5f5;
}
</style>
