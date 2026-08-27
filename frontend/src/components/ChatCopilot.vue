<script setup>
import { ref, nextTick, watch, onMounted, onUnmounted } from 'vue'
import { useScheduleStore } from '../stores/schedule'
import { schedulerApi } from '../api/scheduler'

const store = useScheduleStore()

const isOpen = ref(false)
const userInput = ref('')
const messages = ref([])
const isThinking = ref(false)
const chatBody = ref(null)
const isLoadingHistory = ref(false)

const hiddenTools = [
  'execute_raw_sql',
  'create_savepoint',
  'rollback_to_savepoint',
  'evaluate_schedule_metrics',
  'store_user_preference'
]

function getVisibleTools(tools) {
  if (!tools) return []
  return tools.filter(t => !hiddenTools.includes(t))
}

function hasEvaluatedScenarios(tools) {
  if (!tools) return false
  return tools.includes('evaluate_schedule_metrics') || tools.includes('create_savepoint')
}

// ── Persistent User Session ─────────────────────────────────
function getOrCreateSessionId() {
  let id = localStorage.getItem('ai-copilot-session-id')
  if (!id) {
    id = crypto.randomUUID()
    localStorage.setItem('ai-copilot-session-id', id)
  }
  return id
}

const sessionId = ref(getOrCreateSessionId())

// ── Load chat history on mount ──────────────────────────────
let eventSource = null

onMounted(async () => {
  await loadChatHistory()
  connectToProactiveStream()
})

onUnmounted(() => {
  if (eventSource) {
    eventSource.close()
  }
})

function connectToProactiveStream() {
  eventSource = new EventSource('http://localhost:5000/api/agent/stream')
  
  eventSource.onmessage = (event) => {
    try {
      const data = JSON.parse(event.data)
      console.log('Received proactive notification:', data)
      
      // Inject the proactive proposal into the chat
      messages.value.push({
        role: 'assistant',
        text: data.agentReasoning,
        tools: data.toolCalls ?? [],
        requiresApproval: data.requiresApproval,
        proposalId: data.proposalId,
        simulatedImpact: data.simulatedImpact ?? [],
        approvalStatus: data.requiresApproval ? 'pending' : null,
        isProactive: true
      })
      
      // Flash the UI if it's closed
      if (!isOpen.value) {
        // You could add a badge or bounce animation here
        isOpen.value = true
      }
      
      scrollToBottom()
    } catch (e) {
      console.error('Failed to parse SSE message:', e)
    }
  }

  eventSource.onerror = (error) => {
    console.warn('SSE stream error, it will auto-reconnect', error)
  }
}

async function loadChatHistory() {
  isLoadingHistory.value = true
  try {
    const history = await schedulerApi.getChatHistory(sessionId.value)
    const welcomeMessage = {
      role: 'assistant',
      text: 'Hi! I\'m your AI Scheduling Copilot. Share any updates — like "black ink arrived early" or "assembly station is down" — and I\'ll adjust the schedule automatically.',
      tools: []
    }

    if (history && history.length > 0) {
      messages.value = [
        welcomeMessage,
        ...history.map(h => ({
          role: h.role,
          text: h.content,
          tools: h.tools ?? []
        }))
      ]
    } else {
      // Show default greeting if no history
      messages.value = [welcomeMessage]
    }
  } catch (e) {
    console.warn('Failed to load chat history:', e)
    messages.value = [{
      role: 'assistant',
      text: 'Hi! I\'m your AI Scheduling Copilot. Share any updates — like "black ink arrived early" or "assembly station is down" — and I\'ll adjust the schedule automatically.',
      tools: []
    }]
  } finally {
    isLoadingHistory.value = false
  }
}

async function clearHistory() {
  if (!confirm('Clear all chat history? This cannot be undone.')) return
  try {
    await schedulerApi.clearChatHistory(sessionId.value)
    messages.value = [{
      role: 'assistant',
      text: 'Chat history cleared. How can I help you?',
      tools: []
    }]
  } catch (e) {
    console.error('Failed to clear history:', e)
  }
}

function toggleChat() {
  isOpen.value = !isOpen.value
}

function scrollToBottom() {
  nextTick(() => {
    if (chatBody.value) {
      chatBody.value.scrollTop = chatBody.value.scrollHeight
    }
  })
}

watch(messages, scrollToBottom, { deep: true })
watch(isOpen, (val) => { if (val) scrollToBottom() })

async function sendMessage() {
  const text = userInput.value.trim()
  if (!text || isThinking.value) return

  messages.value.push({ role: 'user', text })
  userInput.value = ''
  isThinking.value = true
  scrollToBottom()

  try {
    const result = await store.runAgent(text, sessionId.value)
    
    if (result.success === false) {
      messages.value.push({
        role: 'assistant',
        text: `Error: ${result.error || 'The agent encountered an unknown error.'}`,
        tools: []
      })
    } else if (result.requiresApproval) {
      // Sandbox mode: show the approval card
      messages.value.push({
        role: 'assistant',
        text: result.agentReasoning || 'I\'ve analyzed the impact of this change.',
        tools: result.toolCalls ?? [],
        requiresApproval: true,
        proposalId: result.proposalId,
        simulatedImpact: result.simulatedImpact ?? [],
        approvalStatus: 'pending' // 'pending' | 'approved' | 'rejected'
      })
    } else {
      messages.value.push({
        role: 'assistant',
        text: result.agentReasoning || 'Schedule updated successfully.',
        tools: result.toolCalls ?? []
      })
    }
  } catch (e) {
    messages.value.push({
      role: 'assistant',
      text: `Sorry, something went wrong: ${e.message}`,
      tools: []
    })
  } finally {
    isThinking.value = false
  }
}

async function handleApprove(msg) {
  if (!msg.proposalId || msg.approvalStatus !== 'pending') return
  isThinking.value = true
  try {
    await store.approveProposal(msg.proposalId)
    msg.approvalStatus = 'approved'
    messages.value.push({
      role: 'assistant',
      text: '✅ Changes approved and applied to the live schedule!',
      tools: []
    })
  } catch (e) {
    messages.value.push({
      role: 'assistant',
      text: `Failed to approve: ${e.message}`,
      tools: []
    })
  } finally {
    isThinking.value = false
  }
}

function handleReject(msg) {
  if (!msg.proposalId || msg.approvalStatus !== 'pending') return
  store.rejectProposal(msg.proposalId)
  msg.approvalStatus = 'rejected'
  messages.value.push({
    role: 'assistant',
    text: '❌ Proposal rejected. If you have a moment, please tell me why so I can learn your preferences for next time!',
    tools: []
  })
}

function handleKeydown(e) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    sendMessage()
  }
}
</script>

<template>
  <!-- Floating Action Button -->
  <Transition name="fab">
    <button
      v-if="!isOpen"
      id="btn-chat-copilot-fab"
      class="chat-fab"
      @click="toggleChat"
      title="Open AI Copilot Chat"
    >
      <!-- Chat icon -->
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-6 h-6">
        <path d="M4.913 2.658c2.075-.27 4.19-.408 6.337-.408 2.147 0 4.262.139 6.337.408 1.922.25 3.291 1.861 3.405 3.727a4.403 4.403 0 00-1.032-.211 50.89 50.89 0 00-8.42 0c-2.358.196-4.04 2.19-4.04 4.434v4.286a4.47 4.47 0 002.433 3.984L7.28 21.53A.75.75 0 016 21v-4.03a48.527 48.527 0 01-1.087-.128C2.905 16.58 1.5 14.833 1.5 12.862V6.638c0-1.97 1.405-3.718 3.413-3.979z" />
        <path d="M15.75 7.5c-1.376 0-2.739.057-4.086.169C10.124 7.797 9 9.103 9 10.609v4.285c0 1.507 1.128 2.814 2.67 2.94 1.243.102 2.5.157 3.768.165l2.782 2.781a.75.75 0 001.28-.53v-2.39l.33-.026c1.542-.125 2.67-1.433 2.67-2.94v-4.286c0-1.505-1.125-2.811-2.664-2.94A49.392 49.392 0 0015.75 7.5z" />
      </svg>

      <!-- Unread badge pulse -->
      <span class="absolute -top-0.5 -right-0.5 w-3 h-3 bg-emerald-400 rounded-full animate-pulse" />
    </button>
  </Transition>

  <!-- Chat Window -->
  <Transition name="chat-window">
    <div v-if="isOpen" class="chat-panel">

      <!-- Header -->
      <div class="chat-header">
        <div class="flex items-center gap-2.5">
          <div class="w-8 h-8 rounded-lg bg-gradient-to-br from-blue-500 to-violet-600 flex items-center justify-center shadow-sm">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 text-white">
              <path fill-rule="evenodd" d="M9.664 1.319a.75.75 0 01.672 0 41.059 41.059 0 018.198 5.424.75.75 0 01-.254 1.285 31.372 31.372 0 00-7.86 3.83.75.75 0 01-.84 0 31.508 31.508 0 00-7.86-3.83.75.75 0 01-.254-1.285 41.059 41.059 0 018.198-5.424zM6.303 9.952A42.992 42.992 0 0110 12.496a42.992 42.992 0 013.697-2.544 28.376 28.376 0 00-3.697-2.176 28.376 28.376 0 00-3.697 2.176z" clip-rule="evenodd" />
              <path d="M10 15.027a44.09 44.09 0 01-4.71-2.9.75.75 0 00-.672.034 22.397 22.397 0 00-3.628 2.585.75.75 0 00.1 1.202 33.92 33.92 0 008.91 3.671.75.75 0 00.42 0 33.92 33.92 0 008.91-3.671.75.75 0 00.1-1.202 22.397 22.397 0 00-3.628-2.585.75.75 0 00-.672-.034A44.09 44.09 0 0110 15.027z" />
            </svg>
          </div>
          <div>
            <h3 class="text-sm font-bold text-slate-900 leading-tight">AI Copilot</h3>
            <p class="text-xs text-slate-500">
              <span :class="isThinking ? 'text-amber-600' : 'text-emerald-600'">
                {{ isThinking ? 'Thinking…' : 'Online' }}
              </span>
            </p>
          </div>
        </div>
        <div class="flex items-center gap-1">
          <!-- Clear History Button -->
          <button
            class="w-7 h-7 rounded-lg hover:bg-red-50 flex items-center justify-center text-slate-400 hover:text-red-500 transition-colors"
            @click="clearHistory"
            title="Clear chat history"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3.5 h-3.5">
              <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 006 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 10.23 1.482l.149-.022.841 10.518A2.75 2.75 0 007.596 19h4.807a2.75 2.75 0 002.742-2.53l.841-10.519.149.023a.75.75 0 00.23-1.482A41.03 41.03 0 0014 4.193V3.75A2.75 2.75 0 0011.25 1h-2.5zM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4zM8.58 7.72a.75.75 0 00-1.5.06l.3 7.5a.75.75 0 101.5-.06l-.3-7.5zm4.34.06a.75.75 0 10-1.5-.06l-.3 7.5a.75.75 0 101.5.06l.3-7.5z" clip-rule="evenodd" />
            </svg>
          </button>
          <!-- Close Button -->
          <button
            class="w-7 h-7 rounded-lg hover:bg-slate-100 flex items-center justify-center text-slate-400 hover:text-slate-600 transition-colors"
            @click="toggleChat"
            title="Close chat"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
              <path d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z" />
            </svg>
          </button>
        </div>
      </div>

      <!-- Messages Body -->
      <div ref="chatBody" class="chat-body">
        <!-- Loading history indicator -->
        <div v-if="isLoadingHistory" class="flex items-center justify-center py-8">
          <div class="flex items-center gap-2 text-slate-400">
            <div class="flex gap-1">
              <span v-for="i in 3" :key="i"
                    class="w-1.5 h-1.5 rounded-full bg-slate-400 animate-bounce"
                    :style="`animation-delay: ${(i - 1) * 150}ms`" />
            </div>
            <span class="text-xs font-medium">Loading history…</span>
          </div>
        </div>

        <TransitionGroup v-else name="msg">
          <div
            v-for="(msg, i) in messages"
            :key="i"
            :class="['chat-message', msg.role === 'user' ? 'chat-user' : 'chat-assistant']"
          >
            <!-- Avatar for assistant -->
            <div v-if="msg.role === 'assistant'" class="chat-avatar">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3.5 h-3.5">
                <path fill-rule="evenodd" d="M9.664 1.319a.75.75 0 01.672 0 41.059 41.059 0 018.198 5.424.75.75 0 01-.254 1.285 31.372 31.372 0 00-7.86 3.83.75.75 0 01-.84 0 31.508 31.508 0 00-7.86-3.83.75.75 0 01-.254-1.285 41.059 41.059 0 018.198-5.424z" clip-rule="evenodd" />
              </svg>
            </div>

            <div :class="['chat-bubble', msg.role === 'user' ? 'chat-bubble-user' : 'chat-bubble-assistant']">
              <p class="text-sm leading-relaxed whitespace-pre-wrap">{{ msg.text }}</p>
              
              <!-- Approval Card (Sandbox Mode) -->
              <div v-if="msg.requiresApproval" class="mt-3 p-3 bg-white/50 rounded-md border border-slate-200">
                <h4 class="text-xs font-bold text-slate-700 mb-2">Simulated Impact:</h4>
                <ul class="text-[11px] font-mono text-slate-600 mb-3 space-y-1">
                  <li v-for="(impact, idx) in msg.simulatedImpact" :key="idx" class="flex gap-2">
                    <span class="text-slate-400">›</span>
                    <span>{{ impact }}</span>
                  </li>
                  <li v-if="!msg.simulatedImpact?.length" class="text-slate-400 italic">No measurable impact on schedule.</li>
                </ul>

                <div v-if="msg.approvalStatus === 'pending'" class="flex gap-2 mt-2">
                  <button @click="handleApprove(msg)" class="flex-1 bg-emerald-500 hover:bg-emerald-600 text-white text-xs font-semibold py-1.5 px-3 rounded shadow-sm transition-colors">
                    Approve
                  </button>
                  <button @click="handleReject(msg)" class="flex-1 bg-white hover:bg-slate-50 text-slate-700 text-xs font-semibold py-1.5 px-3 rounded border border-slate-200 shadow-sm transition-colors">
                    Reject
                  </button>
                </div>
                <div v-else-if="msg.approvalStatus === 'approved'" class="text-xs font-semibold text-emerald-600 text-center py-1">
                  ✅ Approved
                </div>
                <div v-else class="text-xs font-semibold text-slate-500 text-center py-1">
                  ❌ Rejected
                </div>
              </div>

              <!-- Simulation Trace -->
              <div v-if="hasEvaluatedScenarios(msg.tools)" class="mt-2 text-[11px] text-slate-500 italic flex items-center gap-1.5 border-t border-slate-200/50 pt-2">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3.5 h-3.5 text-indigo-400">
                  <path fill-rule="evenodd" d="M10 2a8 8 0 1 0 0 16 8 8 0 0 0 0-16ZM8.732 6.232a2.5 2.5 0 0 1 3.536 0 .75.75 0 1 0 1.06-1.06A4 4 0 0 0 7.67 5.67a.75.75 0 0 0 1.06 1.062Zm-.5 3.998a.75.75 0 0 0 0 1.5H9.5a.75.75 0 0 0 0-1.5H8.232ZM10.5 12a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z" clip-rule="evenodd" />
                </svg>
                Evaluated multiple scheduling strategies internally
              </div>

              <!-- Tool badges -->
              <div v-if="getVisibleTools(msg.tools).length" class="flex flex-wrap gap-1 mt-2 pt-2 border-t border-slate-200/50">
                <span
                  v-for="tool in getVisibleTools(msg.tools)"
                  :key="tool"
                  class="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-semibold tracking-wide bg-slate-100 text-slate-500 border border-slate-200"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-2.5 h-2.5">
                    <path fill-rule="evenodd" d="M15 4.5A3.5 3.5 0 0 1 11.435 8c-.99-.019-2.093.132-2.7.913l-4.13 5.31a2.015 2.015 0 1 1-2.827-2.828l5.309-4.13c.781-.607.932-1.71.914-2.7L8 4.5a3.5 3.5 0 0 1 4.477-3.362c.325.094.39.497.15.736L10.6 3.902a.48.48 0 0 0-.033.653c.271.314.565.608.879.879a.48.48 0 0 0 .653-.033l2.027-2.027c.239-.24.642-.175.736.15.09.31.138.637.138.976Z" clip-rule="evenodd" />
                  </svg>
                  {{ tool.replace(/_/g, ' ') }}
                </span>
              </div>
            </div>
          </div>
        </TransitionGroup>

        <!-- Typing indicator -->
        <Transition name="msg">
          <div v-if="isThinking" class="chat-message chat-assistant">
            <div class="chat-avatar">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3.5 h-3.5">
                <path fill-rule="evenodd" d="M9.664 1.319a.75.75 0 01.672 0 41.059 41.059 0 018.198 5.424.75.75 0 01-.254 1.285 31.372 31.372 0 00-7.86 3.83.75.75 0 01-.84 0 31.508 31.508 0 00-7.86-3.83.75.75 0 01-.254-1.285 41.059 41.059 0 018.198-5.424z" clip-rule="evenodd" />
              </svg>
            </div>
            <div class="chat-bubble chat-bubble-assistant">
              <div class="flex items-center gap-2">
                <div class="flex gap-1">
                  <span v-for="i in 3" :key="i"
                        class="w-1.5 h-1.5 rounded-full bg-slate-400 animate-bounce"
                        :style="`animation-delay: ${(i - 1) * 150}ms`" />
                </div>
                <span class="text-xs text-slate-400 font-medium">Analyzing & optimizing…</span>
              </div>
            </div>
          </div>
        </Transition>
      </div>

      <!-- Input Bar -->
      <div class="chat-input-bar">
        <div class="chat-input-wrapper">
          <input
            id="chat-copilot-input"
            v-model="userInput"
            type="text"
            placeholder="What's the update?"
            class="chat-input"
            :disabled="isThinking"
            @keydown="handleKeydown"
            autocomplete="off"
          />
          <button
            id="btn-chat-send"
            class="chat-send-btn"
            :disabled="!userInput.trim() || isThinking"
            @click="sendMessage"
            title="Send message"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
              <path d="M3.105 2.289a.75.75 0 00-.826.95l1.414 4.925A1.5 1.5 0 005.135 9.25h6.115a.75.75 0 010 1.5H5.135a1.5 1.5 0 00-1.442 1.086l-1.414 4.926a.75.75 0 00.826.95 28.896 28.896 0 0015.293-7.154.75.75 0 000-1.115A28.897 28.897 0 003.105 2.289z" />
            </svg>
          </button>
        </div>
        <p class="text-[10px] text-slate-400 mt-1.5 px-1 leading-tight">
          Powered by Gemini · Changes are applied to the live schedule
        </p>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
/* ── FAB ──────────────────────────────────── */
.chat-fab {
  @apply fixed bottom-6 right-6 z-50
         w-14 h-14 rounded-2xl
         bg-gradient-to-br from-blue-600 to-violet-600
         text-white shadow-lg shadow-blue-600/25
         flex items-center justify-center
         hover:scale-110 hover:shadow-xl hover:shadow-blue-600/30
         active:scale-95
         transition-all duration-200 ease-out;
}

/* ── Chat Panel ───────────────────────────── */
.chat-panel {
  @apply fixed bottom-6 right-6 z-50
         w-[400px] bg-white
         border border-slate-200
         rounded-2xl shadow-2xl shadow-slate-900/10
         flex flex-col overflow-hidden;
  height: min(580px, calc(100vh - 48px));
}

.chat-header {
  @apply px-4 py-3 flex items-center justify-between
         border-b border-slate-100
         bg-white flex-shrink-0;
}

.chat-body {
  @apply flex-1 overflow-y-auto p-4 space-y-3;
  background:
    linear-gradient(to bottom, rgba(248,250,252,0.5), transparent 40px),
    linear-gradient(to top, rgba(248,250,252,0.5), transparent 40px);
  background-color: #fafbfc;
}

/* ── Messages ─────────────────────────────── */
.chat-message {
  @apply flex gap-2 items-end;
}
.chat-user {
  @apply flex-row-reverse;
}
.chat-assistant {
  @apply flex-row;
}

.chat-avatar {
  @apply w-6 h-6 rounded-full flex-shrink-0
         bg-gradient-to-br from-blue-500 to-violet-600
         flex items-center justify-center
         text-white;
}

.chat-bubble {
  @apply max-w-[85%] px-3.5 py-2.5 rounded-2xl;
}

.chat-bubble-user {
  @apply bg-blue-600 text-white rounded-br-md;
}

.chat-bubble-assistant {
  @apply bg-white text-slate-700 border border-slate-200 rounded-bl-md
         shadow-sm;
  border-color: #e8ecf0;
}

/* ── Input ────────────────────────────────── */
.chat-input-bar {
  @apply px-3 py-3 border-t border-slate-100 bg-white flex-shrink-0;
}

.chat-input-wrapper {
  @apply flex items-center gap-2 bg-slate-50 border border-slate-200
         rounded-xl px-3 py-1.5
         focus-within:border-blue-400 focus-within:ring-2 focus-within:ring-blue-100
         transition-all;
}

.chat-input {
  @apply flex-1 bg-transparent text-sm text-slate-800
         placeholder:text-slate-400
         outline-none border-none;
}

.chat-send-btn {
  @apply w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0
         text-white
         bg-gradient-to-r from-blue-500 to-violet-600
         hover:from-blue-600 hover:to-violet-700
         disabled:from-slate-300 disabled:to-slate-300 disabled:cursor-not-allowed
         transition-all duration-200
         active:scale-90;
}

/* ── Transitions ──────────────────────────── */
.fab-enter-active { animation: fab-in 0.3s cubic-bezier(0.34, 1.56, 0.64, 1); }
.fab-leave-active { animation: fab-out 0.15s ease-in; }

@keyframes fab-in {
  from { transform: scale(0) rotate(-90deg); opacity: 0; }
  to   { transform: scale(1) rotate(0);      opacity: 1; }
}
@keyframes fab-out {
  from { transform: scale(1); opacity: 1; }
  to   { transform: scale(0); opacity: 0; }
}

.chat-window-enter-active { animation: chat-open 0.3s cubic-bezier(0.34, 1.56, 0.64, 1); }
.chat-window-leave-active { animation: chat-close 0.2s ease-in; }

@keyframes chat-open {
  from { transform: translateY(16px) scale(0.95); opacity: 0; }
  to   { transform: translateY(0) scale(1);       opacity: 1; }
}
@keyframes chat-close {
  from { transform: translateY(0) scale(1);       opacity: 1; }
  to   { transform: translateY(16px) scale(0.95); opacity: 0; }
}

.msg-enter-active { animation: slide-in-up 0.25s ease-out; }
.msg-leave-active { transition: all 0.15s ease-in; }
.msg-leave-to     { opacity: 0; transform: translateY(8px); }
</style>
