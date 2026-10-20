<script setup lang="ts">
import { DARK_THEME, theme, toggleTheme } from '../theme'
</script>

<template>
  <button class="btn btn-circle btn-ghost" :title="theme === DARK_THEME ? '切换到亮色' : '切换到暗色'" @click="toggleTheme">
    <span class="text-xl">{{ theme === DARK_THEME ? '☀' : '☾' }}</span>
  </button>
</template>
