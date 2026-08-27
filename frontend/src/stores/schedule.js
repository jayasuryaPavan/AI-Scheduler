import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { schedulerApi } from '../api/scheduler'

export const useScheduleStore = defineStore('schedule', () => {
  // ── State ──────────────────────────────────────────────────
  const schedule        = ref(null)
  const isLoading       = ref(false)
  const isAgentRunning  = ref(false)
  const error           = ref(null)
  const toast           = ref(null)
  const selectedPlant   = ref(null)   // null = Global View
  const shiftActuals    = ref([])
  let   pollIntervalId  = null

  // ── Computed ───────────────────────────────────────────────
  const workOrders = computed(() =>
    (schedule.value?.workOrders ?? []).filter(wo => wo.status !== 'Completed')
  )
  const workCenters      = computed(() => schedule.value?.workCenters    ?? [])
  const purchaseOrders   = computed(() => schedule.value?.purchaseOrders ?? [])
  const transferOrders   = computed(() => schedule.value?.transferOrders ?? [])
  const lastSyncAt       = computed(() =>
    schedule.value ? new Date(schedule.value.generatedAt).toLocaleTimeString() : '--'
  )

  // Available plants (derived from work centers)
  const availablePlants = computed(() => {
    const plants = new Set((schedule.value?.workCenters ?? []).map(wc => wc.plantName))
    return [...plants].sort()
  })

  // ── Actions ────────────────────────────────────────────────

  async function fetchSchedule() {
    try {
      schedule.value = await schedulerApi.getSchedule(selectedPlant.value)
      error.value    = null
    } catch (e) {
      error.value = e.message
    }
  }

  async function fetchShiftActuals() {
    try {
      shiftActuals.value = await schedulerApi.getShiftActuals()
    } catch (e) {
      console.error('Failed to load shift actuals:', e)
    }
  }

  async function saveShiftActuals(actualsList) {
    try {
      await schedulerApi.saveShiftActuals(actualsList)
      await fetchShiftActuals()
    } catch (e) {
      console.error('Failed to save shift actuals:', e)
    }
  }

  function setPlant(plant) {
    selectedPlant.value = plant || null
    fetchSchedule()
  }

  /** Fetch data initially, but do not poll. Refresh is manual. */
  function startPolling() {
    fetchSchedule()
    fetchShiftActuals()
  }

  function stopPolling() {
    // No-op since polling is disabled
  }

  async function simulatePODelay(poId) {
    isAgentRunning.value = true
    try {
      const result = await schedulerApi.simulatePODelay(poId)
      showToast({
        type:    'warning',
        title:   'Supply Chain Disruption — Agent Response',
        message: result.agentReasoning || 'Agent re-optimized the production schedule.',
        tools:   result.toolCalls ?? []
      })
      await fetchSchedule()
      return result
    } catch (e) {
      showToast({ type: 'error', title: 'Simulation Error', message: e.message, tools: [] })
    } finally {
      isAgentRunning.value = false
    }
  }

  async function simulateMachineBreakdown(wcId) {
    isAgentRunning.value = true
    try {
      const result = await schedulerApi.simulateMachineBreakdown(wcId)
      showToast({
        type:    'error',
        title:   'Machine Breakdown — Agent Response',
        message: result.agentReasoning || 'Agent rerouted affected work orders.',
        tools:   result.toolCalls ?? []
      })
      await fetchSchedule()
      return result
    } catch (e) {
      showToast({ type: 'error', title: 'Simulation Error', message: e.message, tools: [] })
    } finally {
      isAgentRunning.value = false
    }
  }

  async function resetSimulation() {
    isLoading.value = true
    try {
      await schedulerApi.resetSimulation()
      showToast({
        type:    'success',
        title:   'Simulation Reset',
        message: 'All statuses restored to initial seed state.',
        tools:   []
      })
      await fetchSchedule()
      await fetchShiftActuals()
    } catch (e) {
      showToast({ type: 'error', title: 'Reset Failed', message: e.message, tools: [] })
    } finally {
      isLoading.value = false
    }
  }

  async function setWorkOrderStatus(woId, status) {
    try {
      await schedulerApi.updateWorkOrderStatus(woId, status)
      showToast({
        type: 'success',
        title: 'Status Updated',
        message: `Work order status changed to ${status}`,
        tools: []
      })
      await fetchSchedule()
    } catch (e) {
      showToast({ type: 'error', title: 'Update Failed', message: e.message, tools: [] })
    }
  }

  async function setOperationStatus(opId, status) {
    try {
      await schedulerApi.updateOperationStatus(opId, status)
      showToast({
        type: 'success',
        title: 'Operation Updated',
        message: `Operation status changed to ${status}`,
        tools: []
      })
      await fetchSchedule()
    } catch (e) {
      showToast({ type: 'error', title: 'Update Failed', message: e.message, tools: [] })
    }
  }

  async function setPurchaseOrderStatus(poId, status, date) {
    try {
      await schedulerApi.updatePurchaseOrderStatus(poId, status, date)
      showToast({
        type: 'success',
        title: 'Purchase Order Updated',
        message: `Purchase Order status changed to ${status}`,
        tools: []
      })
      await fetchSchedule()
    } catch (e) {
      showToast({ type: 'error', title: 'Update Failed', message: e.message, tools: [] })
    }
  }

  function showToast(data) {
    toast.value = { ...data, id: Date.now() }
    setTimeout(() => { toast.value = null }, 12_000)
  }

  function dismissToast() {
    toast.value = null
  }

  /** Send a natural language message to the agent (sandbox mode) and return its response */
  async function runAgent(message, sessionId) {
    isAgentRunning.value = true
    try {
      const result = await schedulerApi.runAgent(message, sessionId)
      // Don't refresh dashboard — sandbox mode means nothing was committed yet
      return result
    } catch (e) {
      showToast({ type: 'error', title: 'Agent Error', message: e.message, tools: [] })
      return { success: false, error: e.message, agentReasoning: `Error: ${e.message}` }
    } finally {
      isAgentRunning.value = false
    }
  }

  /** Approve a pending proposal — commits changes and refreshes dashboard */
  async function approveProposal(proposalId) {
    isAgentRunning.value = true
    try {
      const result = await schedulerApi.approveProposal(proposalId)
      showToast({
        type: 'success',
        title: '✅ Changes Approved & Applied',
        message: result.agentReasoning || 'Schedule changes have been committed.',
        tools: result.toolCalls ?? []
      })
      await fetchSchedule()
      return result
    } catch (e) {
      showToast({ type: 'error', title: 'Approval Failed', message: e.message, tools: [] })
      return { success: false, error: e.message }
    } finally {
      isAgentRunning.value = false
    }
  }

  /** Reject a pending proposal — discards it */
  async function rejectProposal(proposalId) {
    try {
      await schedulerApi.rejectProposal(proposalId)
      showToast({
        type: 'info',
        title: 'Proposal Rejected',
        message: 'The proposed schedule changes have been discarded.',
        tools: []
      })
    } catch (e) {
      showToast({ type: 'error', title: 'Rejection Failed', message: e.message, tools: [] })
    }
  }

  return {
    // state
    schedule, isLoading, isAgentRunning, error, toast, selectedPlant, shiftActuals,
    // computed
    workOrders, workCenters, purchaseOrders, transferOrders, lastSyncAt, availablePlants,
    // actions
    fetchSchedule, fetchShiftActuals, saveShiftActuals, startPolling, stopPolling, setPlant,
    simulatePODelay, simulateMachineBreakdown, resetSimulation, dismissToast,
    setWorkOrderStatus, setOperationStatus, setPurchaseOrderStatus, runAgent,
    approveProposal, rejectProposal
  }
})

