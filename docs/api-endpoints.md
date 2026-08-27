# 📡 AI Manufacturing Scheduler — API Reference

> **Base URL**: `http://localhost:8080` (local) or your Cloud Run service URL  
> **Content-Type**: `application/json`  
> **Swagger UI**: `http://localhost:8080/swagger`

---

## Table of Contents

1. [Schedule (Dashboard & Mutations)](#1-schedule-dashboard--mutations)
2. [Purchase Orders (Agent-Triggered)](#2-purchase-orders-agent-triggered)
3. [Work Centers (Agent-Triggered)](#3-work-centers-agent-triggered)
4. [Agent (Chat / Manual Trigger)](#4-agent-chat--manual-trigger)
5. [Health](#5-health)

---

## 1. Schedule (Dashboard & Mutations)

### `GET /api/schedule`

Returns the full dashboard snapshot: all Work Orders (with Operations), Work Centers, Purchase Orders, and Transfer Orders.

| Parameter | In    | Type   | Required | Description |
|-----------|-------|--------|----------|-------------|
| `plant`   | query | string | No       | Filter by plant name (e.g., `"Plant A"`). Only returns WOs with operations at that plant, and TOs involving that plant. |

**Response** `200 OK` — `ScheduleDashboardDto`

```json
{
  "workOrders": [
    {
      "id": 1,
      "workOrderNumber": "WO-2026-001",
      "finishedGoodSku": "SKU-PUMP-A",
      "quantity": 150,
      "priority": 1,
      "status": "Scheduled",
      "dueDate": "2026-09-05",
      "requiredMaterials": [
        { "partNumber": "MTR-5HP", "quantity": 150 }
      ],
      "operations": [
        {
          "id": 1,
          "operationSequence": 10,
          "operationDescription": "CNC Milling",
          "workCenterId": 1,
          "workCenterName": "CNC Milling Station",
          "plantName": "Plant A",
          "status": "Scheduled",
          "setupTimeHours": 1.5,
          "cycleTimePerUnitHours": 0.08,
          "setupWaived": false,
          "totalJobHours": 13.5
        }
      ]
    }
  ],
  "workCenters": [...],
  "purchaseOrders": [...],
  "transferOrders": [...],
  "generatedAt": "2026-08-19T12:00:00Z"
}
```

---

### `POST /api/schedule/reset`

Resets the entire simulation back to the initial seed state. Useful for demo re-runs.

**Request Body**: None

**Response** `200 OK`
```json
{ "message": "Simulation reset to initial state." }
```

---

### `PUT /api/schedule/workorders/{id}/status`

Manually updates a Work Order's status.

| Parameter | In   | Type   | Required | Description |
|-----------|------|--------|----------|-------------|
| `id`      | path | int    | Yes      | Work Order ID |

**Request Body**: Raw JSON string — one of `"Scheduled"`, `"In-Progress"`, `"Blocked"`, `"Completed"`

```json
"In-Progress"
```

**Response** `200 OK`
```json
{ "message": "Work Order status updated." }
```

**Response** `404 Not Found` — if the Work Order ID doesn't exist.

---

### `PUT /api/schedule/operations/{id}/status`

Updates an Operation's status with **sequential cascade logic**:
- When an operation is completed, the next sequential operation is automatically promoted from `Blocked` → `Scheduled`.
- When all operations on a Work Order are completed, the Work Order itself is marked `Completed`.
- **Constraint**: Cannot start an operation if another operation is already `In-Progress` at the same Work Center.

| Parameter | In   | Type   | Required | Description |
|-----------|------|--------|----------|-------------|
| `id`      | path | int    | Yes      | Operation ID |

**Request Body**: Raw JSON string — one of `"Blocked"`, `"Scheduled"`, `"In-Progress"`, `"Completed"`

```json
"In-Progress"
```

**Response** `200 OK`
```json
{ "message": "Operation status updated." }
```

**Response** `400 Bad Request` — if the work center is occupied:
```json
{ "error": "Cannot start operation. 'CNC Milling' (WO WO-2026-001) is currently in-progress at this work center." }
```

---

### `PUT /api/schedule/purchaseorders/{id}/status`

Manually updates a Purchase Order's status and/or expected delivery date.

| Parameter | In   | Type   | Required | Description |
|-----------|------|--------|----------|-------------|
| `id`      | path | int    | Yes      | Purchase Order ID |

**Request Body**: `PurchaseOrderUpdateRequest`

```json
{
  "status": "In-Transit",
  "expectedDeliveryDate": "2026-09-10"
}
```

| Field                  | Type    | Required | Values |
|------------------------|---------|----------|--------|
| `status`               | string  | Yes      | `"Pending"`, `"Received"`, `"Delayed"`, `"In-Transit"`, `"Out of Stock"` |
| `expectedDeliveryDate` | string? | No       | ISO date format `"YYYY-MM-DD"`. If omitted, date is not changed. |

**Response** `200 OK`
```json
{ "message": "Purchase Order updated." }
```

---

## 2. Purchase Orders (Agent-Triggered)

These endpoints mutate a Purchase Order's status **and** automatically trigger the AI Agent to re-optimize the schedule.

### `PUT /api/purchaseorders/{id}/delay`

Marks a PO as **Delayed** and triggers the agent to assess material impact and re-optimize.

| Parameter | In   | Type | Required | Description |
|-----------|------|------|----------|-------------|
| `id`      | path | int  | Yes      | Purchase Order ID |

**Request Body**: None

**Response** `200 OK` — `AgentRunResult`
```json
{
  "agentReasoning": "PO-003 for PCB-CTRL has been delayed...",
  "toolCalls": [
    { "toolName": "assess_material_availability", "input": "{}", "output": "..." },
    { "toolName": "execute_schedule_adjustment", "input": "{}", "output": "..." }
  ]
}
```

---

### `PUT /api/purchaseorders/{id}/receive`

Marks a PO as **Received** and triggers the agent to unblock any Work Orders that were waiting on this material.

| Parameter | In   | Type | Required | Description |
|-----------|------|------|----------|-------------|
| `id`      | path | int  | Yes      | Purchase Order ID |

**Request Body**: None

**Response** `200 OK` — `AgentRunResult` (same shape as above)

---

## 3. Work Centers (Agent-Triggered)

These endpoints mutate a Work Center's status **and** automatically trigger the AI Agent.

### `PUT /api/workcenters/{id}/breakdown`

Marks a Work Center as **Down** and triggers the agent to block impacted Work Orders and re-route production.

| Parameter | In   | Type | Required | Description |
|-----------|------|------|----------|-------------|
| `id`      | path | int  | Yes      | Work Center ID |

**Request Body**: None

**Response** `200 OK` — `AgentRunResult`

---

### `PUT /api/workcenters/{id}/restore`

Restores a Work Center to **Active** and triggers the agent to unblock and re-promote any Work Orders that were held.

| Parameter | In   | Type | Required | Description |
|-----------|------|------|----------|-------------|
| `id`      | path | int  | Yes      | Work Center ID |

**Request Body**: None

**Response** `200 OK` — `AgentRunResult`

---

## 4. Agent (Chat / Manual Trigger)

### `POST /api/agent/run`

Manually triggers the AI agent with a natural-language prompt. This is the endpoint used by the chatbot copilot in the frontend.

**Request Body**: `AgentRunRequest`

```json
{
  "trigger": "The blue ink shipment arrived early. Update PO-005 and check if we can start WO-2026-005."
}
```

| Field     | Type    | Required | Description |
|-----------|---------|----------|-------------|
| `trigger` | string? | No       | Natural-language instruction for the agent. If omitted, the agent runs a general health assessment. |

**Response** `200 OK` — `AgentRunResult`
```json
{
  "agentReasoning": "Great news! I've updated PO-005 to Received and...",
  "toolCalls": [
    {
      "toolName": "update_purchase_order_by_description",
      "input": "{ \"partDescription\": \"Blue ink\", \"newStatus\": \"Received\" }",
      "output": "{ \"success\": true }"
    },
    {
      "toolName": "execute_schedule_adjustment",
      "input": "{}",
      "output": "{ \"promotedCount\": 1, \"blockedCount\": 0 }"
    }
  ]
}
```

---

## 5. Health

### `GET /health`

Standard health check endpoint. Verifies database connectivity.

**Response** `200 OK`
```json
{ "status": "Healthy" }
```

---

## Agent Tools (Internal)

These are not HTTP endpoints — they are **function-calling tools** that the Gemini model invokes during its reasoning loop. Documented here for architectural reference.

| Tool Name | Description | Returns |
|-----------|-------------|---------|
| `assess_material_availability` | Checks BOM requirements for all active Work Orders against current inventory and PO status. | `MaterialAvailabilityReport` — per-WO material satisfaction checks |
| `evaluate_work_center_capacity` | Reports on all Work Center statuses, active operations, and utilization. | `WorkCenterCapacityReport` — per-center load and health |
| `execute_schedule_adjustment` | Commits schedule changes: blocks/unblocks Work Orders, promotes alternatives, re-prioritizes the queue. | `ScheduleAdjustmentResult` — summary of all mutations |
| `manage_inventory_levels` | Checks inventory thresholds and recommends reorder actions. | `InventoryManagementResult` — stock levels and alerts |
| `update_purchase_order_by_description` | Finds a PO by part description keyword and updates its status/date. | `PurchaseOrderUpdateResult` — confirmation of PO changes |
| `update_work_center_by_name` | Finds a Work Center by name and updates its status. | `WorkCenterUpdateResult` — confirmation of WC changes |
