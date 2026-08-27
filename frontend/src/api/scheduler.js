import axios from 'axios'

const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
  timeout: 90_000 // 90s — agent calls can take ~20-30s
})

// Response interceptor for unified error messages
api.interceptors.response.use(
  (res) => res,
  (err) => {
    const msg = err.response?.data?.error
             ?? err.response?.data?.title
             ?? err.message
             ?? 'Unknown error'
    return Promise.reject(new Error(msg))
  }
)

export const schedulerApi = {
  /** Full dashboard snapshot, optionally filtered by plant */
  getSchedule: (plant) =>
    api.get('/schedule', { params: plant ? { plant } : {} }).then(r => r.data),

  /** Simulate a PO being marked as Delayed — triggers agent */
  simulatePODelay: (id) =>
    api.put(`/purchaseorders/${id}/delay`).then(r => r.data),

  /** Mark a PO as Received — agent unblocks resolved WOs */
  markPOReceived: (id) =>
    api.put(`/purchaseorders/${id}/receive`).then(r => r.data),

  /** Simulate a machine breakdown — triggers agent */
  simulateMachineBreakdown: (id) =>
    api.put(`/workcenters/${id}/breakdown`).then(r => r.data),

  /** Restore a Work Center to Active — agent re-promotes WOs */
  restoreWorkCenter: (id) =>
    api.put(`/workcenters/${id}/restore`).then(r => r.data),

  /** Manually trigger the agent with an optional prompt (sandbox mode) */
  runAgent: (trigger, sessionId) =>
    api.post('/agent/run', { trigger, sessionId }).then(r => r.data),

  /** Approve a pending agent proposal — commits changes to live DB */
  approveProposal: (proposalId) =>
    api.post(`/agent/approve/${proposalId}`).then(r => r.data),

  /** Reject a pending agent proposal — discards it */
  rejectProposal: (proposalId) =>
    api.post(`/agent/reject/${proposalId}`).then(r => r.data),

  /** Manually update a Work Order status (e.g. from MES) */
  updateWorkOrderStatus: (id, status) =>
    api.put(`/schedule/workorders/${id}/status`, `"${status}"`, { headers: { 'Content-Type': 'application/json' } }).then(r => r.data),

  /** Update an operation status (with sequential cascade) */
  updateOperationStatus: (id, status) =>
    api.put(`/schedule/operations/${id}/status`, `"${status}"`, { headers: { 'Content-Type': 'application/json' } }).then(r => r.data),

  /** Update a Purchase Order status and expected delivery date */
  updatePurchaseOrderStatus: (id, status, expectedDeliveryDate = null) =>
    api.put(`/schedule/purchaseorders/${id}/status`, { status, expectedDeliveryDate }).then(r => r.data),

  /** Reset everything to seed state (for demo re-runs) */
  resetSimulation: () =>
    api.post('/schedule/reset').then(r => r.data),

  /** Get Shift Actuals logged by supervisors */
  getShiftActuals: () =>
    api.get('/schedule/shift-actuals').then(r => r.data),

  /** Save Shift Actuals */
  saveShiftActuals: (actuals) =>
    api.post('/schedule/shift-actuals', actuals).then(r => r.data),

  /** Load full chat history for a persistent session */
  getChatHistory: (sessionId) =>
    api.get(`/agent/history/${sessionId}`).then(r => r.data),

  /** Clear all chat history for a session */
  clearChatHistory: (sessionId) =>
    api.delete(`/agent/history/${sessionId}`).then(r => r.data)
}
