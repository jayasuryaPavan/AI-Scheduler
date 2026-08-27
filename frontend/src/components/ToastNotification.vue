<script setup>
import { computed } from 'vue'
import { useScheduleStore } from '../stores/schedule'

const store = useScheduleStore()

const themes = {
  success: {
    wrapper: 'border-emerald-200 bg-emerald-50 shadow-lg',
    header:  'text-emerald-800',
    bar:     'bg-emerald-500'
  },
  warning: {
    wrapper: 'border-amber-200 bg-amber-50 shadow-lg',
    header:  'text-amber-800',
    bar:     'bg-amber-500'
  },
  error: {
    wrapper: 'border-red-200 bg-red-50 shadow-lg',
    header:  'text-red-800',
    bar:     'bg-red-500'
  },
  info: {
    wrapper: 'border-blue-200 bg-blue-50 shadow-lg',
    header:  'text-blue-800',
    bar:     'bg-blue-500'
  }
}

const theme = computed(() =>
  store.toast ? (themes[store.toast.type] ?? themes.info) : themes.info
)
</script>

<template>
  <Transition name="toast">
    <div
      v-if="store.toast"
      class="fixed bottom-6 right-6 z-50 w-full max-w-md rounded-xl border overflow-hidden"
      :class="theme.wrapper"
      role="alert"
      aria-live="polite"
    >
      <div class="p-4">
        <div class="flex items-start gap-3">
          <!-- Content -->
          <div class="flex-1 min-w-0">
            <h4 class="font-bold text-sm leading-snug" :class="theme.header">
              {{ store.toast.title }}
            </h4>
            <p class="text-xs text-slate-700 font-medium mt-1.5 leading-relaxed">
              {{ store.toast.message }}
            </p>

            <!-- Tool calls used by the agent -->
            <div v-if="store.toast.tools?.length" class="mt-3 flex flex-wrap items-center gap-2">
              <span class="text-xs font-bold text-slate-500 uppercase tracking-wide">Agent tools:</span>
              <span
                v-for="tool in store.toast.tools"
                :key="tool"
                class="text-xs font-mono px-2 py-0.5 rounded-md bg-white border border-slate-200 text-slate-600 shadow-sm"
              >
                {{ tool }}
              </span>
            </div>
          </div>

          <!-- Dismiss button -->
          <button
            class="flex-shrink-0 w-7 h-7 rounded-full bg-white border border-slate-200 hover:bg-slate-50
                   flex items-center justify-center transition-colors shadow-sm focus:outline-none"
            @click="store.dismissToast()"
            aria-label="Dismiss notification"
          >
            <svg class="w-3.5 h-3.5 text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2">
              <path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
      </div>

      <!-- Auto-dismiss progress bar -->
      <div class="h-1 w-full" :class="theme.bar"
           style="animation: shrink-width 12s linear forwards" />
    </div>
  </Transition>
</template>

<style scoped>
@keyframes shrink-width {
  from { width: 100%; }
  to   { width: 0%; }
}
</style>
