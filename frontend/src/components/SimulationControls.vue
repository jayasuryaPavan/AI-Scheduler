<script setup>
import { useScheduleStore } from '../stores/schedule'

const store = useScheduleStore()
</script>

<template>
  <div class="glass-card px-4 py-3 flex items-center gap-4 flex-wrap">

    <!-- Label -->
    <div class="flex items-center flex-shrink-0">
      <span class="text-xs font-bold text-slate-600 uppercase tracking-widest">Disruption Simulator</span>
    </div>

    <div class="h-6 w-px bg-slate-200 flex-shrink-0" />

    <!-- Simulate PO Delay -->
    <button
      id="btn-simulate-po-delay"
      class="btn bg-white text-amber-700 border border-amber-300 shadow-sm
             hover:bg-amber-50 hover:border-amber-400
             focus:ring-amber-200"
      :disabled="store.isAgentRunning"
      @click="store.simulatePODelay(3)"
      title="Marks PO-003 (PLST-223) as Delayed and triggers the AI agent"
    >
      <span v-if="store.isAgentRunning"
            class="w-4 h-4 border-2 border-amber-200 border-t-amber-600 rounded-full animate-spin" />
      Simulate PO Delay
      <span class="hidden sm:inline text-xs font-medium opacity-70">(PLST-223)</span>
    </button>

    <!-- Simulate Machine Breakdown -->
    <button
      id="btn-simulate-breakdown"
      class="btn bg-white text-red-700 border border-red-300 shadow-sm
             hover:bg-red-50 hover:border-red-400
             focus:ring-red-200"
      :disabled="store.isAgentRunning"
      @click="store.simulateMachineBreakdown(1)"
      title="Marks Casing Assembly Station as Down and triggers the AI agent"
    >
      <span v-if="store.isAgentRunning"
            class="w-4 h-4 border-2 border-red-200 border-t-red-600 rounded-full animate-spin" />
      Machine Breakdown
      <span class="hidden sm:inline text-xs font-medium opacity-70">(Casing Assembly)</span>
    </button>

    <div class="h-6 w-px bg-slate-200 flex-shrink-0" />

    <!-- Reset -->
    <button
      id="btn-reset-simulation"
      class="btn bg-white text-slate-700 border border-slate-300 shadow-sm
             hover:bg-slate-50 hover:border-slate-400
             focus:ring-slate-200"
      :disabled="store.isAgentRunning || store.isLoading"
      @click="store.resetSimulation()"
      title="Restore all PO and Work Center statuses to initial seed state"
    >
      Reset
    </button>

    <!-- Agent running animation (right-aligned) -->
    <Transition name="expand">
      <div v-if="store.isAgentRunning"
           class="ml-auto flex items-center gap-2.5 text-amber-700">
        <!-- Bouncing dots -->
        <div class="flex gap-1.5">
          <div v-for="i in 3" :key="i"
               class="w-2 h-2 rounded-full bg-amber-600 animate-bounce"
               :style="`animation-delay: ${(i - 1) * 120}ms`" />
        </div>
        <span class="text-xs font-bold tracking-wide">Gemini optimizing schedule…</span>
      </div>
    </Transition>
  </div>
</template>
