<script setup>
import { ref, computed } from 'vue'
import { useScheduleStore } from '../stores/schedule'

const store = useScheduleStore()

const editingId = ref(null)
const editForm = ref({ status: '', expectedDeliveryDate: '' })

// Combine POs and TOs into a single list with a "type" field
const allOrders = computed(() => {
  const pos = store.purchaseOrders.map(po => ({
    ...po,
    type: 'PO',
    route: null
  }))
  const tos = store.transferOrders.map(to => ({
    ...to,
    type: 'TO',
    route: `${to.sourcePlant} → ${to.destinationPlant}`,
    supplierLeadTimeDays: 0
  }))
  return [...pos, ...tos]
})

function cfg(status) {
  const map = {
    'Received':   { ring: 'border-slate-200', bg: 'bg-white',    badge: 'bg-emerald-100 text-emerald-700 border-emerald-200' },
    'Pending':    { ring: 'border-amber-300',  bg: 'bg-amber-50', badge: 'bg-amber-100 text-amber-700 border-amber-200' },
    'Delayed':    { ring: 'border-red-300',    bg: 'bg-red-50',   badge: 'bg-red-100 text-red-700 border-red-200' },
    'In-Transit': { ring: 'border-blue-300',   bg: 'bg-blue-50',  badge: 'bg-blue-100 text-blue-700 border-blue-200' }
  }
  return map[status] ?? map.Pending
}

function fmtDate(d) {
  return new Date(d + 'T00:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

function daysUntil(d) {
  const ms   = new Date(d + 'T00:00:00') - new Date()
  const days = Math.ceil(ms / 86_400_000)
  return days
}

function startEdit(order) {
  if (order.type !== 'PO') return // Only POs are editable for now
  editingId.value = order.id
  editForm.value = {
    status: order.status,
    expectedDeliveryDate: order.expectedDeliveryDate
  }
}

function cancelEdit() {
  editingId.value = null
}

async function saveEdit(order) {
  if (order.type !== 'PO') return
  await store.setPurchaseOrderStatus(order.id, editForm.value.status, editForm.value.expectedDeliveryDate)
  editingId.value = null
}
</script>

<template>
  <!-- Pane header -->
  <div class="pane-header">
    <h2 class="font-semibold text-sm text-slate-900">Orders</h2>
    <span class="ml-auto font-mono text-xs text-slate-500">{{ allOrders.length }} orders</span>
  </div>

  <!-- Order list -->
  <div class="pane-body">
    <TransitionGroup name="list" tag="div" class="space-y-4 relative">
      <article
        v-for="order in allOrders"
        :key="`${order.type}-${order.id}`"
        class="rounded-xl border p-4 transition-all duration-300 shadow-sm"
        :class="[cfg(order.status).ring, cfg(order.status).bg]"
      >
        <!-- Order header row -->
        <div class="flex items-start justify-between mb-2">
          <div>
            <div class="flex items-center gap-2 mb-1">
              <span class="text-xs font-mono font-bold text-slate-400 tracking-wider uppercase">
                {{ order.type }}-{{ String(order.id).padStart(3, '0') }}
              </span>
              <span
                class="text-[10px] font-bold uppercase tracking-wider px-1.5 py-0.5 rounded border"
                :class="order.type === 'PO'
                  ? 'bg-slate-100 text-slate-600 border-slate-200'
                  : 'bg-purple-50 text-purple-600 border-purple-200'"
              >
                {{ order.type === 'PO' ? 'Purchase' : 'Transfer' }}
              </span>
            </div>
            <h3 class="text-base font-bold font-mono text-slate-900">{{ order.partNumber }}</h3>
          </div>
          <div v-if="editingId === order.id" class="flex gap-2">
            <select v-model="editForm.status" class="text-xs border-slate-200 rounded px-2 py-1">
              <option>Pending</option>
              <option>Received</option>
              <option>Delayed</option>
              <option>In-Transit</option>
              <option>Out of Stock</option>
            </select>
          </div>
          <div v-else class="flex items-center gap-2 cursor-pointer" @click="startEdit(order)" title="Click to edit">
            <span class="status-badge" :class="cfg(order.status).badge">
              {{ order.status }}
            </span>
            <svg v-if="order.type === 'PO'" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3 h-3 text-slate-400 hover:text-blue-500">
              <path d="M2.695 14.763l-1.262 3.154a.5.5 0 00.65.65l3.155-1.262a4 4 0 001.343-.885L17.5 5.5a2.121 2.121 0 00-3-3L3.58 13.42a4 4 0 00-.885 1.343z" />
            </svg>
          </div>
        </div>

        <!-- Description -->
        <p class="text-sm font-medium text-slate-600 leading-snug mb-3">{{ order.partDescription }}</p>

        <!-- Transfer route (TO only) -->
        <p v-if="order.route" class="text-xs text-purple-600 font-semibold mb-3">
          {{ order.route }}
        </p>

        <!-- Quantity + Delivery -->
        <div class="flex items-end justify-between">
          <div>
            <span class="text-xs font-bold text-slate-400 uppercase tracking-wide">Quantity</span>
            <p class="text-base font-mono font-bold text-slate-800">
              {{ order.quantity.toLocaleString() }}
              <span class="text-xs font-semibold text-slate-500 font-sans">units</span>
            </p>
            <p v-if="order.supplierLeadTimeDays > 0" class="text-[10px] font-bold text-slate-400 mt-1 uppercase tracking-wider">
              Lead time: {{ order.supplierLeadTimeDays }} days
            </p>
          </div>
          <div class="text-right">
            <span class="text-xs font-bold text-slate-400 uppercase tracking-wide">Expected</span>
            
            <div v-if="editingId === order.id" class="mt-1 flex flex-col items-end gap-2">
              <input type="date" v-model="editForm.expectedDeliveryDate" class="text-sm border-slate-200 rounded px-2 py-1 w-36" />
              <div class="flex gap-1">
                <button @click="cancelEdit" class="px-2 py-1 text-xs text-slate-500 hover:bg-slate-100 rounded">Cancel</button>
                <button @click="saveEdit(order)" class="px-2 py-1 text-xs bg-blue-600 text-white hover:bg-blue-700 rounded">Save</button>
              </div>
            </div>
            <div v-else>
              <p class="text-sm font-semibold text-slate-700">{{ fmtDate(order.expectedDeliveryDate) }}</p>
              <p class="text-xs mt-1 font-bold tracking-wide uppercase"
                 :class="{
                   'text-emerald-600': order.status === 'Received',
                   'text-amber-600':   order.status === 'Pending',
                   'text-blue-600':    order.status === 'In-Transit',
                   'text-red-600':     order.status === 'Delayed',
                   'text-slate-500':   order.status === 'Out of Stock'
                 }">
                <template v-if="order.status === 'Received'">In Inventory</template>
                <template v-else-if="order.status === 'Delayed'">DELAYED — ETA Unknown</template>
                <template v-else-if="order.status === 'In-Transit'">In Transit</template>
                <template v-else-if="order.status === 'Out of Stock'">Out of Stock</template>
                <template v-else>{{ daysUntil(order.expectedDeliveryDate) }} days out</template>
              </p>
            </div>
          </div>
        </div>

        <!-- Delayed warning banner -->
        <Transition name="expand">
          <div v-if="order.status === 'Delayed'"
               class="mt-3 p-2.5 rounded-lg bg-red-50 border border-red-200 flex items-center shadow-sm">
            <p class="text-xs font-semibold text-red-700 tracking-wide">Agent blocked dependent Work Orders</p>
          </div>
        </Transition>
      </article>
    </TransitionGroup>

    <div v-if="!allOrders.length"
         class="flex flex-col items-center justify-center py-16 text-slate-400">
      <p class="text-sm font-medium">No orders</p>
    </div>
  </div>
</template>
