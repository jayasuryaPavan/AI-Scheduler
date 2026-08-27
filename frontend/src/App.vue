<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { useScheduleStore } from './stores/schedule'
import WorkOrderPane       from './components/WorkOrderPane.vue'
import WorkCenterPane      from './components/WorkCenterPane.vue'
import PurchaseOrderPane   from './components/PurchaseOrderPane.vue'
import ShiftReportPane     from './components/ShiftReportPane.vue'
import ToastNotification   from './components/ToastNotification.vue'
import ChatCopilot         from './components/ChatCopilot.vue'

const store = useScheduleStore()
const currentTab = ref('work-orders')

const refreshData = async () => {
  store.isLoading = true
  await Promise.all([
    store.fetchSchedule(),
    store.fetchShiftActuals()
  ])
  store.isLoading = false
}

onMounted(() => store.startPolling())
onUnmounted(() => store.stopPolling())
</script>

<template>
  <div class="min-h-screen flex flex-col gap-3 p-4 pb-6">

    <!-- ── Header ──────────────────────────────────────────── -->
    <header class="glass-card flex flex-col flex-shrink-0">
      <div class="px-5 py-4 flex items-center justify-between border-b border-slate-100">
        <div class="flex items-center gap-3">
          <div>
            <h1 class="text-base font-bold text-slate-900 tracking-tight leading-tight">
              AI Manufacturing Scheduler
            </h1>
            <p class="text-xs text-slate-500 mt-0.5">
              Autonomous Master Production Scheduler
              <span class="text-slate-300 mx-1">·</span>
              <span class="text-blue-600 font-medium">Powered by Gemini</span>
            </p>
          </div>
        </div>

        <!-- Plant Selector -->
        <div class="flex items-center gap-3">
          <select
            class="text-xs font-semibold border border-slate-200 rounded-lg px-3 py-1.5 bg-white text-slate-700 outline-none cursor-pointer hover:border-slate-300 transition-colors"
            :value="store.selectedPlant || ''"
            @change="store.setPlant($event.target.value)"
          >
            <option value="">All Plants</option>
            <option value="Plant 1">Plant 1 (Assembly)</option>
            <option value="Plant 2">Plant 2 (Stamping)</option>
          </select>

        <!-- Right side: agent status + sync -->
        <div class="flex items-center gap-5">
          <!-- Agent status indicator -->
          <div class="flex items-center gap-2">
            <div
              :class="[
                'status-dot transition-colors duration-500',
                store.isAgentRunning
                  ? 'bg-amber-400 animate-ping'
                  : 'bg-emerald-500'
              ]"
            />
            <span
              :class="[
                'text-xs font-semibold transition-colors',
                store.isAgentRunning ? 'text-amber-600' : 'text-emerald-600'
              ]"
            >
              {{ store.isAgentRunning ? 'Agent Running…' : 'Agent Standby' }}
            </span>
          </div>

          <!-- Live sync timestamp & Refresh -->
          <div class="hidden sm:flex items-center gap-3">
            <div class="flex flex-col items-end">
              <span class="text-xs text-slate-500">Last sync</span>
              <span class="text-xs font-mono text-slate-400">{{ store.lastSyncAt }}</span>
            </div>
            <button 
              @click="refreshData"
              class="p-1.5 bg-slate-100 hover:bg-slate-200 text-slate-600 rounded-md transition-colors"
              title="Refresh Data"
            >
              <svg class="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
              </svg>
            </button>
          </div>

          <!-- Connection error badge -->
          <div v-if="store.error"
               class="px-2 py-1 rounded-lg bg-red-50 border border-red-200 text-xs text-red-600">
            {{ store.error }}
          </div>
        </div>
        </div>
      </div>

      <!-- Navigation Tabs -->
      <nav class="flex gap-6 px-5 pt-2">
        <button 
          @click="currentTab = 'work-orders'" 
          :class="['px-1 py-3 font-medium text-sm border-b-2 transition-colors duration-200', currentTab === 'work-orders' ? 'border-blue-600 text-blue-700' : 'border-transparent text-slate-500 hover:text-slate-700 hover:border-slate-300']"
        >
          Work Orders
        </button>
        <button 
          @click="currentTab = 'work-centers'" 
          :class="['px-1 py-3 font-medium text-sm border-b-2 transition-colors duration-200', currentTab === 'work-centers' ? 'border-blue-600 text-blue-700' : 'border-transparent text-slate-500 hover:text-slate-700 hover:border-slate-300']"
        >
          Work Centers
        </button>
        <button 
          @click="currentTab = 'purchase-orders'" 
          :class="['px-1 py-3 font-medium text-sm border-b-2 transition-colors duration-200', currentTab === 'purchase-orders' ? 'border-blue-600 text-blue-700' : 'border-transparent text-slate-500 hover:text-slate-700 hover:border-slate-300']"
        >
          Orders
        </button>
        <button 
          @click="currentTab = 'shift-report'" 
          :class="['px-1 py-3 font-medium text-sm border-b-2 transition-colors duration-200', currentTab === 'shift-report' ? 'border-blue-600 text-blue-700' : 'border-transparent text-slate-500 hover:text-slate-700 hover:border-slate-300']"
        >
          Shift Report
        </button>
      </nav>
    </header>

    <!-- ── Main Tabbed Content ─────────────────────────────── -->
    <main class="flex-1 flex flex-col min-h-0" style="height: calc(100vh - 210px);">
      <section v-if="currentTab === 'work-orders'" class="glass-card flex-1 flex flex-col min-h-0 overflow-hidden">
        <WorkOrderPane />
      </section>

      <section v-else-if="currentTab === 'work-centers'" class="glass-card flex-1 flex flex-col min-h-0 overflow-hidden">
        <WorkCenterPane />
      </section>

      <section v-else-if="currentTab === 'purchase-orders'" class="glass-card flex-1 flex flex-col min-h-0 overflow-hidden">
        <PurchaseOrderPane />
      </section>

      <section v-else-if="currentTab === 'shift-report'" class="glass-card flex-1 flex flex-col min-h-0 overflow-hidden">
        <ShiftReportPane />
      </section>
    </main>

    <!-- ── Toast Notifications ────────────────────────────── -->
    <ToastNotification />

    <!-- ── AI Chat Copilot ────────────────────────────────── -->
    <ChatCopilot />
  </div>
</template>
