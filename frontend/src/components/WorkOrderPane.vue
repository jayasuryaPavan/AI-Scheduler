<script setup>
import { ref, computed } from 'vue'
import { useScheduleStore } from '../stores/schedule'

const store = useScheduleStore()

// Track which Work Orders have their routing expanded
const expanded = ref(new Set())

function toggle(id) {
  expanded.value.has(id) ? expanded.value.delete(id) : expanded.value.add(id)
}

// ── Status colour maps ─────────────────────────────────────
const woColors = {
  'Scheduled':   { ring: 'border-slate-200',  bg: 'bg-white',    dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-700 border-slate-200' },
  'In-Progress': { ring: 'border-blue-300',   bg: 'bg-blue-50',  dot: 'bg-blue-500',  badge: 'bg-blue-100 text-blue-700 border-blue-200' },
  'Blocked':     { ring: 'border-red-300',    bg: 'bg-red-50',   dot: 'bg-red-500',   badge: 'bg-red-100 text-red-700 border-red-200' },
  'Completed':   { ring: 'border-slate-200',  bg: 'bg-slate-50', dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-500 border-slate-200' }
}

const opColors = {
  'Scheduled':   'bg-white border border-slate-200 text-slate-600',
  'In-Progress': 'bg-blue-50 border border-blue-200 text-blue-700',
  'Completed':   'bg-slate-50 border border-slate-200 text-slate-400 line-through',
  'Blocked':     'bg-red-50 border border-red-200 text-red-700'
}

function woColor(status) { return woColors[status] ?? woColors['Scheduled'] }
function opColor(status) { return opColors[status] ?? opColors['Scheduled'] }

function fmtDate(d) {
  return new Date(d + 'T00:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
}

function currentOp(wo) {
  if (!wo || !wo.operations || wo.operations.length === 0) return null;
  return wo.operations.find(o => o.status === 'In-Progress') 
      || wo.operations.find(o => o.status === 'Scheduled' || o.status === 'Blocked')
      || wo.operations[wo.operations.length - 1];
}

// ── Sorting & Filtering ────────────────────────────────────
const searchQuery = ref('')
const statusFilter = ref('All')
const sortBy = ref('Priority') // 'Priority', 'DueDate', 'Status'

const filteredAndSortedWorkOrders = computed(() => {
  let list = store.workOrders

  // 1. Filter by status
  if (statusFilter.value !== 'All') {
    list = list.filter(wo => wo.status === statusFilter.value)
  }

  // 2. Filter by search query (SKU or Work Order Number)
  const q = searchQuery.value.trim().toLowerCase()
  if (q) {
    list = list.filter(wo => 
      wo.workOrderNumber.toLowerCase().includes(q) || 
      wo.finishedGoodSku.toLowerCase().includes(q)
    )
  }

  // 3. Sort
  list = [...list].sort((a, b) => {
    if (sortBy.value === 'Priority') {
      return a.priority - b.priority
    } else if (sortBy.value === 'DueDate') {
      return new Date(a.dueDate) - new Date(b.dueDate)
    } else if (sortBy.value === 'Status') {
      const order = { 'In-Progress': 1, 'Blocked': 2, 'Scheduled': 3, 'Completed': 4 }
      return (order[a.status] || 99) - (order[b.status] || 99)
    }
    return 0
  })

  return list
})
</script>

<template>
  <div class="pane-header">
    <h2 class="font-semibold text-sm text-slate-900">Work Orders</h2>
    <span class="ml-auto font-mono text-xs text-slate-500">{{ store.workOrders.length }} active</span>
  </div>

  <!-- Toolbar: Search, Filter, Sort -->
  <div class="px-4 py-3 border-b border-slate-100 flex flex-wrap items-center gap-3 bg-slate-50/50">
    <div class="flex-1 min-w-[200px] relative">
      <svg class="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor">
        <path stroke-linecap="round" stroke-linejoin="round" d="M21 21l-5.197-5.197m0 0A7.5 7.5 0 105.196 5.196a7.5 7.5 0 0010.607 10.607z" />
      </svg>
      <input 
        v-model="searchQuery" 
        type="text" 
        placeholder="Search SKU or order #..." 
        class="w-full pl-9 pr-3 py-1.5 text-xs bg-white border border-slate-200 rounded-lg outline-none focus:border-blue-400 focus:ring-2 focus:ring-blue-100 transition-all text-slate-700 placeholder:text-slate-400"
      />
    </div>
    
    <div class="flex items-center gap-2">
      <select v-model="statusFilter" class="text-xs border border-slate-200 rounded-lg px-2.5 py-1.5 bg-white text-slate-700 outline-none cursor-pointer hover:border-slate-300 transition-colors">
        <option value="All">All Statuses</option>
        <option value="In-Progress">In-Progress</option>
        <option value="Scheduled">Scheduled</option>
        <option value="Blocked">Blocked</option>
        <option value="Completed">Completed</option>
      </select>
      
      <select v-model="sortBy" class="text-xs border border-slate-200 rounded-lg px-2.5 py-1.5 bg-white text-slate-700 outline-none cursor-pointer hover:border-slate-300 transition-colors">
        <option value="Priority">Sort: Priority</option>
        <option value="DueDate">Sort: Due Date</option>
        <option value="Status">Sort: Status</option>
      </select>
    </div>
  </div>

  <!-- Work Order list -->
  <div class="pane-body">
    <TransitionGroup name="list" tag="div" class="space-y-3 relative">
      <article
        v-for="(wo, woIndex) in filteredAndSortedWorkOrders"
        :key="wo.id"
        class="rounded-xl border transition-all duration-300 shadow-sm overflow-hidden"
        :class="[woColor(wo.status).ring, woColor(wo.status).bg]"
      >
        <!-- Row header — click to expand routing -->
        <div
          class="flex items-center gap-3 px-4 py-3 cursor-pointer select-none hover:bg-black/5 transition-colors"
          :id="`wo-row-${wo.id}`"
          @click="toggle(wo.id)"
        >
          <!-- Priority badge (shows rank in sorted list, not raw DB priority) -->
          <div class="w-7 h-7 rounded-full bg-white border border-slate-200 shadow-sm flex items-center justify-center flex-shrink-0">
            <span class="text-xs font-bold font-mono text-slate-700">{{ woIndex + 1 }}</span>
          </div>

          <!-- Identity -->
          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-1.5 flex-wrap">
              <span class="text-xs font-mono text-slate-500 flex-shrink-0">{{ wo.workOrderNumber }}</span>
              <span class="text-sm font-semibold text-slate-900 truncate">{{ wo.finishedGoodSku }}</span>
            </div>
            <div class="flex items-center gap-2 mt-0.5 text-xs text-slate-500">
              <span>Qty {{ wo.quantity }}</span>
              <span class="text-slate-300">·</span>
              <span>Due {{ fmtDate(wo.dueDate) }}</span>
              <span class="text-slate-300">·</span>
              <span>{{ wo.operations.length }} ops</span>
              <template v-if="wo.operations.length > 0 && currentOp(wo)">
                <span class="text-slate-300">·</span>
                <span class="truncate max-w-[200px]" :class="{'text-blue-600 font-medium': currentOp(wo).status === 'In-Progress'}">
                  {{ currentOp(wo).operationSequence }} {{ currentOp(wo).operationDescription }}
                </span>
              </template>
            </div>
          </div>

          <!-- Status -->
          <div class="flex items-center gap-2 flex-shrink-0">
            <div
              :class="[woColor(wo.status).dot, 'status-dot', wo.status === 'In-Progress' ? 'animate-pulse' : '']"
            />
            <select
              class="status-badge cursor-pointer outline-none hover:shadow-md transition-all appearance-none text-center"
              :class="woColor(wo.status).badge"
              :value="wo.status"
              @change="store.setWorkOrderStatus(wo.id, $event.target.value)"
              @click.stop
              title="Click to edit status"
            >
              <option value="Scheduled">Scheduled</option>
              <option value="In-Progress">In-Progress</option>
              <option value="Blocked">Blocked</option>
              <option value="Completed">Completed</option>
            </select>
          </div>

          <!-- Chevron -->
          <svg
            class="w-5 h-5 text-slate-400 transition-transform duration-200 flex-shrink-0 ml-2"
            :class="{ 'rotate-180': expanded.has(wo.id) }"
            fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2"
          >
            <path stroke-linecap="round" stroke-linejoin="round" d="M19 9l-7 7-7-7" />
          </svg>
        </div>

        <!-- Expanded detail: agent notes + BOM + routing -->
        <Transition name="expand">
          <div v-if="expanded.has(wo.id)" class="border-t border-slate-100 bg-white/50 px-4 pb-4 pt-3 space-y-4">

            <!-- Agent notes (only when present) -->
            <div v-if="wo.agentNotes"
                 class="p-3 rounded-lg bg-amber-50 border border-amber-200/60 shadow-sm space-y-1.5">
              <p v-for="(note, idx) in wo.agentNotes.split('\n').filter(Boolean)" :key="idx" class="text-xs text-amber-900 leading-relaxed">
                {{ note }}
              </p>
            </div>

            <!-- Required Materials (BOM) -->
            <div>
              <p class="text-xs text-slate-400 uppercase tracking-wider font-bold mb-2">BOM Materials</p>
              <div class="flex flex-wrap gap-1.5">
                <span
                  v-for="m in wo.requiredMaterials"
                  :key="m.partNumber"
                  class="text-xs font-mono px-2 py-1 rounded-md bg-slate-100 border border-slate-200 text-slate-700"
                >
                  {{ m.partNumber }} <span class="text-slate-400 ml-1">×</span> <span class="font-bold">{{ m.quantity }}</span>
                </span>
              </div>
            </div>

            <!-- Routing operations with inline status editing -->
            <div>
              <p class="text-xs text-slate-400 uppercase tracking-wider font-bold mb-2">Routing</p>
              <div class="space-y-1.5">
                <div
                  v-for="op in wo.operations"
                  :key="op.id"
                  class="flex items-start gap-2.5 px-3 py-2 rounded-lg text-xs transition-colors shadow-sm"
                  :class="opColor(op.status)"
                >
                  <span class="font-mono font-bold w-5 flex-shrink-0 opacity-50 mt-0.5">{{ op.operationSequence }}</span>
                  <div class="flex-1 min-w-0">
                    <p class="truncate font-semibold">{{ op.operationDescription }}</p>
                    <p class="opacity-70 mt-0.5 flex items-center gap-1.5 flex-wrap">
                      <span>{{ op.workCenterName }}</span>
                      <span>·</span>
                      <span class="font-semibold text-slate-500">{{ op.totalJobHours }}h</span>
                      <span v-if="op.setupWaived" class="ml-1 px-1.5 py-0.5 rounded text-[10px] bg-amber-100 border border-amber-200 text-amber-700 font-bold tracking-wide uppercase">Setup Waived</span>
                    </p>
                  </div>
                  <!-- Operation status dropdown -->
                  <select
                    class="flex-shrink-0 font-semibold text-xs capitalize mt-0.5 cursor-pointer outline-none bg-transparent appearance-none border border-slate-200 rounded px-1.5 py-0.5 hover:border-slate-400 transition-colors"
                    :value="op.status"
                    @change="store.setOperationStatus(op.id, $event.target.value)"
                    title="Change operation status"
                  >
                    <option value="Scheduled">Scheduled</option>
                    <option value="In-Progress">In-Progress</option>
                    <option value="Completed">Completed</option>
                    <option value="Blocked">Blocked</option>
                  </select>
                </div>
              </div>
            </div>
          </div>
        </Transition>
      </article>
    </TransitionGroup>

    <div v-if="!filteredAndSortedWorkOrders.length"
         class="flex flex-col items-center justify-center py-16 text-slate-400">
      <p class="text-sm font-medium">No work orders found</p>
    </div>
  </div>
</template>
