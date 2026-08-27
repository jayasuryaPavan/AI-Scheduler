<script setup>
import { useScheduleStore } from '../stores/schedule'

const store = useScheduleStore()

function cfg(status) {
  return status === 'Active'
    ? { ring: 'border-slate-200', bg: 'bg-white', dot: 'bg-emerald-500', badge: 'bg-emerald-100 text-emerald-700 border-emerald-200', bar: 'bg-emerald-500' }
    : { ring: 'border-red-300',     bg: 'bg-red-50',  dot: 'bg-red-500',     badge: 'bg-red-100 text-red-700 border-red-200',         bar: 'bg-red-500'     }
}
</script>

<template>
  <div class="pane-header">
    <h2 class="font-semibold text-sm text-slate-900">Work Centers</h2>
    <span class="ml-auto font-mono text-xs text-slate-500">
      {{ store.workCenters.filter(w => w.status === 'Active').length }}/{{ store.workCenters.length }} active
    </span>
  </div>

  <!-- Work Center cards -->
  <div class="pane-body">
    <TransitionGroup name="list" tag="div" class="space-y-4 relative">
      <article
        v-for="wc in store.workCenters"
        :key="wc.id"
        class="rounded-xl border p-5 transition-all duration-300 shadow-sm"
        :class="[cfg(wc.status).ring, cfg(wc.status).bg]"
      >
        <!-- Card header -->
        <div class="flex items-start justify-between mb-4">
          <div class="flex-1 min-w-0 pr-2">
            <h3 class="font-bold text-slate-900 text-base leading-tight">{{ wc.name }}</h3>
            <p class="text-xs text-slate-500 mt-1 font-medium">
              {{ wc.plantName }} · {{ wc.dailyCapacityHours }}h daily capacity
            </p>
          </div>
          <div class="flex items-center gap-2 flex-shrink-0">
            <div
              :class="[
                'status-dot transition-all duration-500',
                cfg(wc.status).dot,
                wc.status === 'Active' ? 'animate-pulse' : 'animate-pulse'
              ]"
            />
            <span class="status-badge" :class="cfg(wc.status).badge">{{ wc.status }}</span>
          </div>
        </div>

        <!-- Breakdown alert -->
        <Transition name="expand">
          <div v-if="wc.status === 'Down'"
               class="mb-4 p-3 rounded-lg bg-red-50 border border-red-200 flex items-start gap-3 shadow-sm">
            <div>
              <p class="text-xs font-bold text-red-800 uppercase tracking-wide">Machine offline</p>
              <p class="text-xs text-red-600 mt-1">AI agent is rerouting affected jobs</p>
            </div>
          </div>
        </Transition>

        <!-- Capacity visualisation bar (per Shift if available) -->
        <div v-if="wc.shifts && wc.shifts.length > 0" class="space-y-3 mt-2">
          <div v-for="shift in wc.shifts" :key="shift.id" class="flex flex-col gap-2">
            <div class="flex items-center justify-between">
              <span class="text-[11px] font-bold text-slate-500 uppercase tracking-wider">{{ shift.shiftName }} ({{ shift.startTime.substring(0,5) }}-{{ shift.endTime.substring(0,5) }})</span>
              <span class="text-xs font-mono text-slate-500 font-semibold">{{ shift.capacityHours }}h max</span>
            </div>
            <div class="flex gap-1">
              <div
                v-for="seg in 10"
                :key="seg"
                class="h-1.5 flex-1 rounded-full transition-all duration-700"
                :class="wc.status === 'Down' ? 'bg-red-200' : (seg <= 6 ? cfg(wc.status).bar : 'bg-slate-200')"
              />
            </div>
          </div>
        </div>
        <div v-else>
          <div class="flex items-center justify-between mb-2">
            <span class="text-xs font-bold text-slate-500 uppercase tracking-wider">Daily Capacity</span>
            <span class="text-xs font-mono font-semibold text-slate-500">
              {{ wc.status === 'Down' ? 'OFFLINE' : `${wc.dailyCapacityHours}h max` }}
            </span>
          </div>
          <div class="flex gap-1">
            <div
              v-for="seg in 10"
              :key="seg"
              class="h-1.5 flex-1 rounded-full transition-all duration-700"
              :class="wc.status === 'Down'
                ? 'bg-red-200'
                : (seg <= 6 ? cfg(wc.status).bar : 'bg-slate-200')"
            />
          </div>
        </div>

        <!-- Machine ID chip -->
        <div class="mt-4 flex items-center gap-3">
          <span class="text-xs font-mono font-bold text-slate-400">WC-{{ String(wc.id).padStart(3, '0') }}</span>
          <span class="flex-1 h-px bg-slate-200" />
          <span class="text-xs font-medium text-slate-400 uppercase tracking-wide">Station</span>
        </div>
      </article>
    </TransitionGroup>

    <div v-if="!store.workCenters.length"
         class="flex flex-col items-center justify-center py-16 text-slate-400">
      <p class="text-sm font-medium">No work centers</p>
    </div>
  </div>
</template>
