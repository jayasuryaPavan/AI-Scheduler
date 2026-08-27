<script setup>
import { computed, ref, watch } from 'vue'
import { useScheduleStore } from '../stores/schedule'

const store = useScheduleStore()

// Local state for editable fields
const localTracking = ref({})

// Initialize local tracking when new operations come in (only if not already set)
const initLocalTracking = (opId, shiftName) => {
  const key = `${opId}-${shiftName}`
  if (!localTracking.value[key]) {
    localTracking.value[key] = {
      timeFinished: '',
      associatesWorked: ''
    }
  }
}

// Auto-save logic
let saveTimeout = null
watch(localTracking, (newVal) => {
  clearTimeout(saveTimeout)
  saveTimeout = setTimeout(() => {
    const actuals = Object.entries(newVal)
      .filter(([_, v]) => v.timeFinished || v.associatesWorked !== '')
      .map(([key, v]) => {
        const [opId, shiftName] = key.split('-')
        return {
          workOrderOperationId: parseInt(opId),
          shiftName: shiftName,
          timeFinished: v.timeFinished,
          associatesWorked: v.associatesWorked === '' ? null : parseInt(v.associatesWorked)
        }
      })
    if (actuals.length > 0) {
      store.saveShiftActuals(actuals)
    }
  }, 1000)
}, { deep: true })

// Hydrate localTracking from store's actuals
watch(() => store.shiftActuals, (actuals) => {
  actuals.forEach(a => {
    const key = `${a.workOrderOperationId}-${a.shiftName}`
    if (!localTracking.value[key]) {
      localTracking.value[key] = { timeFinished: '', associatesWorked: '' }
    }
    // Only update if currently blank to not overwrite active typing
    if (!localTracking.value[key].timeFinished && a.timeFinished) {
      localTracking.value[key].timeFinished = a.timeFinished
    }
    if (!localTracking.value[key].associatesWorked && a.associatesWorked !== null) {
      localTracking.value[key].associatesWorked = a.associatesWorked
    }
  })
}, { immediate: true })

// Filter state
const shiftFilter = ref('All') // 'All', 'Shift A', 'Shift B'

// Sorting state
const sortKey = ref('')
const sortDirection = ref('asc')

const toggleSort = (key) => {
  if (sortKey.value === key) {
    sortDirection.value = sortDirection.value === 'asc' ? 'desc' : 'asc'
  } else {
    sortKey.value = key
    sortDirection.value = 'asc'
  }
}

// Compute the flattened list of operations with shift assignments
const shiftOperations = computed(() => {
  if (!store.schedule) return []

  // Flatten operations from all Work Orders
  let allOps = []
  store.schedule.workOrders.forEach(wo => {
    // Only consider Work Orders that are not Completed
    if (wo.status === 'Completed') return
    
    wo.operations.forEach(op => {
      if (op.status === 'Scheduled' || op.status === 'In-Progress') {
        allOps.push({
          ...op,
          workOrderNumber: wo.workOrderNumber,
          workOrderPriority: wo.priority,
          quantity: wo.quantity
        })
      }
    })
  })

  // Filter by selected plant if applicable
  if (store.selectedPlant) {
    allOps = allOps.filter(op => op.plantName === store.selectedPlant)
  }

  // Base sort operations by priority (lower is higher priority) and then by sequence for shift calculation
  allOps.sort((a, b) => {
    if (a.workOrderPriority !== b.workOrderPriority) {
      return a.workOrderPriority - b.workOrderPriority
    }
    return a.operationSequence - b.operationSequence
  })

  // Assign shifts based on WorkCenter capacity (Shift A: 0-8h, Shift B: 8-16h)
  // Shift splitting: if an operation spans the 8h boundary, split it into two records.
  const wcHoursAccumulator = {}
  
  let assignedOps = allOps.flatMap(op => {
    if (!wcHoursAccumulator[op.workCenterId]) {
      wcHoursAccumulator[op.workCenterId] = 0
    }
    
    const startHour = wcHoursAccumulator[op.workCenterId]
    const endHour = startHour + op.totalJobHours
    wcHoursAccumulator[op.workCenterId] = endHour
    
    const associatesEst = op.requiredAssociates || 1

    if (startHour < 8 && endHour > 8) {
      // Splits across shift A and B
      const hoursInA = 8 - startHour
      const hoursInB = endHour - 8
      
      initLocalTracking(op.id, 'Shift A')
      initLocalTracking(op.id, 'Shift B')

      return [
        { ...op, assignedShift: 'Shift A', associatesEst, splitHours: hoursInA, trackingKey: `${op.id}-Shift A` },
        { ...op, assignedShift: 'Shift B', associatesEst, splitHours: hoursInB, trackingKey: `${op.id}-Shift B` }
      ]
    } else {
      const assignedShift = startHour < 8 ? 'Shift A' : 'Shift B'
      initLocalTracking(op.id, assignedShift)
      
      return [{
        ...op,
        assignedShift,
        associatesEst,
        splitHours: op.totalJobHours,
        trackingKey: `${op.id}-${assignedShift}`
      }]
    }
  })

  // Apply shift filter
  if (shiftFilter.value !== 'All') {
    assignedOps = assignedOps.filter(op => op.assignedShift === shiftFilter.value)
  }

  // Apply column sorting if requested
  if (sortKey.value) {
    assignedOps = [...assignedOps].sort((a, b) => {
      let valA, valB

      switch (sortKey.value) {
        case 'shift':
          valA = a.assignedShift
          valB = b.assignedShift
          break
        case 'workOrder':
          valA = a.workOrderNumber
          valB = b.workOrderNumber
          break
        case 'operation':
          valA = a.operationDescription || ''
          valB = b.operationDescription || ''
          break
        case 'station':
          valA = a.workCenterName || ''
          valB = b.workCenterName || ''
          break
        case 'quantity':
          valA = a.quantity
          valB = b.quantity
          break
        case 'estTime':
          valA = a.totalJobHours
          valB = b.totalJobHours
          break
        case 'assocEst':
          valA = a.associatesEst
          valB = b.associatesEst
          break
        default:
          return 0
      }

      if (typeof valA === 'string') {
        const cmp = valA.localeCompare(valB)
        return sortDirection.value === 'asc' ? cmp : -cmp
      } else {
        return sortDirection.value === 'asc' ? (valA > valB ? 1 : -1) : (valA < valB ? 1 : -1)
      }
    })
  }

  return assignedOps
})

// Group operations by plant
const groupedOperations = computed(() => {
  const groups = {}
  shiftOperations.value.forEach(op => {
    if (!groups[op.plantName]) {
      groups[op.plantName] = []
    }
    groups[op.plantName].push(op)
  })
  
  return Object.keys(groups).sort().map(plantName => ({
    plantName,
    operations: groups[plantName]
  }))
})

// Export CSV functionality
const exportCSV = () => {
  const headers = ['Shift', 'Work Order', 'Operation', 'Station', 'Quantity', 'Est. Time (h)', 'Assoc. Est.', 'Time Finished', 'Assoc. Worked']
  const rows = shiftOperations.value.map(op => {
    const actuals = localTracking.value[op.trackingKey] || {}
    return [
      op.assignedShift,
      op.workOrderNumber,
      `"${op.operationDescription}"`,
      `"${op.workCenterName}"`,
      op.quantity,
      op.splitHours.toFixed(2),
      op.associatesEst,
      actuals.timeFinished || '',
      actuals.associatesWorked || ''
    ].join(',')
  })
  
  const csvContent = [headers.join(','), ...rows].join('\n')
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.setAttribute('download', `Shift_Report_${new Date().toISOString().split('T')[0]}.csv`)
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
}

</script>

<template>
  <div class="flex flex-col h-full bg-slate-50/50">
    <!-- Header Controls -->
    <div class="px-5 py-4 border-b border-slate-200 bg-white flex flex-wrap items-center justify-between gap-4">
      <div>
        <h2 class="text-base font-bold text-slate-900 flex items-center gap-2">
          <span>Shift Production Report</span>
        </h2>
        <p class="text-xs text-slate-500 mt-0.5">Track and record actual shift performance against estimated capacity.</p>
      </div>

      <div class="flex items-center gap-4">
        <!-- Legend badges for quick visual distinction -->
        <div class="hidden sm:flex items-center gap-2 text-xs print:hidden">
          <span class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-blue-600 text-white font-medium shadow-xs">
            <span class="w-1.5 h-1.5 rounded-full bg-white animate-pulse"></span>
            Shift A (06:00 - 14:00)
          </span>
          <span class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-amber-500 text-slate-950 font-semibold shadow-xs">
            <span class="w-1.5 h-1.5 rounded-full bg-slate-950"></span>
            Shift B (14:00 - 22:00)
          </span>
        </div>

        <!-- Shift Filter Dropdown -->
        <div class="flex items-center gap-2 print:hidden">
          <label class="text-xs font-semibold text-slate-600">Filter:</label>
          <select
            v-model="shiftFilter"
            class="text-xs font-medium border border-slate-200 rounded-lg px-2.5 py-1.5 bg-white text-slate-700 outline-none hover:border-slate-300 focus:border-blue-500 transition-colors shadow-xs"
          >
            <option value="All">All Shifts</option>
            <option value="Shift A">Shift A Only</option>
            <option value="Shift B">Shift B Only</option>
          </select>
        </div>

        <!-- Export CSV Button -->
        <button 
          @click="exportCSV" 
          class="print:hidden text-xs font-medium border border-slate-200 rounded-lg px-2.5 py-1.5 bg-white text-slate-700 hover:bg-slate-50 hover:text-slate-900 transition-colors shadow-xs flex items-center gap-1.5"
        >
          <svg class="w-3.5 h-3.5 text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"></path></svg>
          Export CSV
        </button>
      </div>
    </div>

    <!-- Table Container -->
    <div class="flex-1 overflow-auto p-5">
      <div v-if="shiftOperations.length === 0" class="flex flex-col items-center justify-center h-full text-slate-400">
        <svg class="w-12 h-12 mb-3 text-slate-300" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
        </svg>
        <p class="text-sm font-medium">No operations scheduled for this view.</p>
      </div>

      <div v-else class="space-y-6">
        <div v-for="group in groupedOperations" :key="group.plantName" class="bg-white rounded-xl border border-slate-200 overflow-hidden shadow-sm">
          <div class="px-4 py-3 bg-slate-50 border-b border-slate-200">
            <h3 class="font-bold text-slate-800 text-sm">{{ group.plantName }} Schedule</h3>
          </div>
          <table class="w-full text-left border-collapse">
            <thead>
              <tr class="bg-slate-100/80 border-b border-slate-200 text-xs text-slate-600 font-bold uppercase tracking-wider">
                <!-- Shift Column (Sortable) -->
                <th 
                @click="toggleSort('shift')"
                class="px-4 py-3 whitespace-nowrap cursor-pointer select-none hover:bg-slate-200/70 transition-colors"
                title="Click to sort by Shift"
              >
                <div class="flex items-center gap-1.5">
                  <span>Shift</span>
                  <span class="text-slate-400 font-mono text-[10px]" :class="{ 'text-blue-600 font-bold': sortKey === 'shift' }">
                    {{ sortKey === 'shift' ? (sortDirection === 'asc' ? '▲' : '▼') : '↕' }}
                  </span>
                </div>
              </th>

              <!-- Work Order Column (Sortable) -->
              <th 
                @click="toggleSort('workOrder')"
                class="px-4 py-3 whitespace-nowrap cursor-pointer select-none hover:bg-slate-200/70 transition-colors"
                title="Click to sort by Work Order"
              >
                <div class="flex items-center gap-1.5">
                  <span>Work Order</span>
                  <span class="text-slate-400 font-mono text-[10px]" :class="{ 'text-blue-600 font-bold': sortKey === 'workOrder' }">
                    {{ sortKey === 'workOrder' ? (sortDirection === 'asc' ? '▲' : '▼') : '↕' }}
                  </span>
                </div>
              </th>

              <!-- Operation Column (Sortable) -->
              <th 
                @click="toggleSort('operation')"
                class="px-4 py-3 whitespace-nowrap cursor-pointer select-none hover:bg-slate-200/70 transition-colors"
                title="Click to sort by Operation"
              >
                <div class="flex items-center gap-1.5">
                  <span>Operation</span>
                  <span class="text-slate-400 font-mono text-[10px]" :class="{ 'text-blue-600 font-bold': sortKey === 'operation' }">
                    {{ sortKey === 'operation' ? (sortDirection === 'asc' ? '▲' : '▼') : '↕' }}
                  </span>
                </div>
              </th>

              <!-- Station Column (Sortable) -->
              <th 
                @click="toggleSort('station')"
                class="px-4 py-3 whitespace-nowrap cursor-pointer select-none hover:bg-slate-200/70 transition-colors"
                title="Click to sort by Station"
              >
                <div class="flex items-center gap-1.5">
                  <span>Station</span>
                  <span class="text-slate-400 font-mono text-[10px]" :class="{ 'text-blue-600 font-bold': sortKey === 'station' }">
                    {{ sortKey === 'station' ? (sortDirection === 'asc' ? '▲' : '▼') : '↕' }}
                  </span>
                </div>
              </th>

              <!-- Quantity Column -->
              <th 
                @click="toggleSort('quantity')"
                class="px-4 py-3 text-right whitespace-nowrap cursor-pointer select-none hover:bg-slate-200/70 transition-colors"
                title="Click to sort by Quantity"
              >
                <div class="flex items-center justify-end gap-1.5">
                  <span>Qty</span>
                  <span class="text-slate-400 font-mono text-[10px]" :class="{ 'text-blue-600 font-bold': sortKey === 'quantity' }">
                    {{ sortKey === 'quantity' ? (sortDirection === 'asc' ? '▲' : '▼') : '↕' }}
                  </span>
                </div>
              </th>

              <!-- Est. Time Column (Sortable) -->
              <th 
                @click="toggleSort('estTime')"
                class="px-4 py-3 text-right whitespace-nowrap cursor-pointer select-none hover:bg-slate-200/70 transition-colors"
                title="Click to sort by Estimated Time"
              >
                <div class="flex items-center justify-end gap-1.5">
                  <span>Est. Time</span>
                  <span class="text-slate-400 font-mono text-[10px]" :class="{ 'text-blue-600 font-bold': sortKey === 'estTime' }">
                    {{ sortKey === 'estTime' ? (sortDirection === 'asc' ? '▲' : '▼') : '↕' }}
                  </span>
                </div>
              </th>

              <!-- Assoc. Est. Column (Sortable) -->
              <th 
                @click="toggleSort('assocEst')"
                class="px-4 py-3 text-center whitespace-nowrap cursor-pointer select-none hover:bg-slate-200/70 transition-colors"
                title="Click to sort by Associates Estimated"
              >
                <div class="flex items-center justify-center gap-1.5">
                  <span>Assoc. Est.</span>
                  <span class="text-slate-400 font-mono text-[10px]" :class="{ 'text-blue-600 font-bold': sortKey === 'assocEst' }">
                    {{ sortKey === 'assocEst' ? (sortDirection === 'asc' ? '▲' : '▼') : '↕' }}
                  </span>
                </div>
              </th>

              <!-- User Editable Columns -->
              <th class="px-4 py-3 whitespace-nowrap bg-slate-200/40 text-slate-700">
                <div class="flex items-center gap-1">
                  <span>Time Finished</span>
                  <span class="text-[10px] text-blue-600 font-normal">(User Input)</span>
                </div>
              </th>
              <th class="px-4 py-3 whitespace-nowrap bg-slate-200/40 text-slate-700">
                <div class="flex items-center gap-1">
                  <span>Assoc. Worked</span>
                  <span class="text-[10px] text-blue-600 font-normal">(User Input)</span>
                </div>
              </th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100 bg-white">
            <tr 
              v-for="op in group.operations" 
              :key="op.trackingKey"
              class="hover:bg-blue-50/50 transition-colors"
              :class="[
                'transition-all duration-150',
                localTracking[op.trackingKey]?.timeFinished 
                  ? 'bg-emerald-50/80 border-l-4 border-l-emerald-500 hover:bg-emerald-100/60' 
                  : (op.status === 'In-Progress' 
                      ? 'bg-amber-50/80 border-l-4 border-l-amber-500 hover:bg-amber-100/60' 
                      : 'bg-white border-l-4 border-l-transparent hover:bg-slate-50')
              ]"
            >
              <!-- Shift Badge with distinct, high-contrast opposite colors -->
              <td class="px-4 py-3 text-sm">
                <span :class="[
                  'px-2.5 py-1 rounded-md text-xs font-bold shadow-xs inline-flex items-center gap-1',
                  op.assignedShift === 'Shift A' 
                    ? 'bg-blue-600 text-white' 
                    : 'bg-amber-500 text-slate-950 font-extrabold'
                ]">
                  {{ op.assignedShift }}
                </span>
              </td>

              <!-- Work Order -->
              <td class="px-4 py-3">
                <div class="text-sm font-bold text-slate-800">{{ op.workOrderNumber }}</div>
                <div class="text-[11px] text-slate-400">Pri: {{ op.workOrderPriority }}</div>
              </td>

              <!-- Operation -->
              <td class="px-4 py-3 text-sm font-medium text-slate-700">
                {{ op.operationDescription }}
              </td>

              <!-- Station -->
              <td class="px-4 py-3 text-sm font-semibold text-slate-800">
                <span class="px-2 py-0.5 rounded bg-slate-100 border border-slate-200">
                  {{ op.workCenterName }}
                </span>
              </td>

              <!-- Quantity -->
              <td class="px-4 py-3 text-sm text-slate-700 text-right font-mono font-medium">
                {{ op.quantity.toLocaleString() }}
              </td>

              <!-- Est. Time -->
              <td class="px-4 py-3 text-sm text-slate-900 text-right font-mono font-bold">
                {{ op.splitHours.toFixed(2) }}h
              </td>

              <!-- Assoc. Est. -->
              <td class="px-4 py-3 text-sm text-slate-800 text-center font-bold">
                <span class="inline-flex items-center justify-center w-6 h-6 rounded-full bg-slate-100 text-slate-700 text-xs">
                  {{ op.associatesEst }}
                </span>
              </td>

              <!-- Time Finished (Editable) -->
              <td class="px-4 py-2 bg-slate-50/40">
                <input 
                  type="text" 
                  v-model="localTracking[op.trackingKey].timeFinished"
                  placeholder="e.g. 13:30"
                  class="w-28 text-sm px-2.5 py-1.5 rounded-lg border border-slate-200 bg-white outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-100 transition-all font-mono placeholder:text-slate-300 font-medium"
                />
              </td>

              <!-- Associates Worked (Editable) -->
              <td class="px-4 py-2 bg-slate-50/40">
                <input 
                  type="number" 
                  v-model="localTracking[op.trackingKey].associatesWorked"
                  placeholder="e.g. 2"
                  min="0"
                  class="w-24 text-sm px-2.5 py-1.5 rounded-lg border border-slate-200 bg-white outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-100 transition-all font-mono placeholder:text-slate-300 font-medium"
                />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</div>
</template>

<style>
@media print {
  body * {
    visibility: hidden;
  }
  #app, #app * {
    visibility: hidden;
  }
  table, table * {
    visibility: visible;
  }
  table {
    position: absolute;
    left: 0;
    top: 0;
    width: 100%;
  }
}
</style>
