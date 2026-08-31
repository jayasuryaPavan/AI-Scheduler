# 🏭 AI Manufacturing Scheduler
### All Things Agentic Hackathon — Taskmaster Track

An autonomous micro-ERP for an assembly operation, powered by an **AI Master Production Scheduler agent** built on **Google Gemini** (via Vertex AI). The agent dynamically re-optimizes the production queue in response to supply chain disruptions (delayed Purchase Orders) and machine breakdowns — without human intervention.

---

## Architecture

```
┌─────────────────┐    HTTP/JSON     ┌──────────────────────────┐
│  Vue 3 Frontend │ ◄──────────────► │  .NET 9 Web API          │
│  Tailwind CSS   │                  │                          │
│  Pinia Store    │                  │  SchedulingService (CRUD)│
│  3-Pane Layout  │                  │  MPS Agent ──────────────┼──► Gemini 3.5 Flash
└─────────────────┘                  │    tool: assess_material │    (Vertex AI)
                                     │    tool: evaluate_cap.   │
                                     │    tool: execute_adjust  │
                                     └──────────┬───────────────┘
                                                │ EF Core
                                     ┌──────────▼───────────────┐
                                     │  PostgreSQL 16            │
                                     │  WorkOrders + Operations  │
                                     │  PurchaseOrders + POs     │
                                     │  Inventory + WorkCenters  │
                                     └──────────────────────────┘
```

---

## Prerequisites

| Tool | Version | Install |
|------|---------|---------|
| Docker Desktop | 4.x+ | [docker.com](https://docker.com) |
| Google Cloud CLI | latest | [cloud.google.com/sdk](https://cloud.google.com/sdk) |
| GCP Project | — | With **Vertex AI API** enabled |
| .NET SDK | 9.0 | (only needed for local dev without Docker) |
| Node.js | 20+ | (only needed for local dev without Docker) |

---

## Quick Start (Docker Compose)

### 1. Clone and configure environment

```bash
git clone <your-repo-url>
cd ai-scheduler

# Copy and edit environment variables
cp .env.example .env
```

Open `.env` and set:
```dotenv
GEMINI_PROJECT_ID=your-actual-gcp-project-id
GEMINI_LOCATION=us-central1
GEMINI_MODEL_ID=gemini-3.5-flash
DB_PASSWORD=scheduler_dev_pass          # change in production
GCP_ADC_PATH=~/.config/gcloud/application_default_credentials.json
```

### 2. Authenticate with Google Cloud (Vertex AI)

```bash
# Login with your GCP account
gcloud auth login

# Set Application Default Credentials (used by the agent)
gcloud auth application-default login

# Enable required APIs
gcloud services enable aiplatform.googleapis.com --project=YOUR_PROJECT_ID
```

### 3. Start the application

```bash
docker compose up --build
```

> **First run**: Docker will build both images (~3-5 minutes). PostgreSQL will initialize from `init.sql` automatically.

### 4. Open the dashboard

- **Plant Floor Dashboard**: http://localhost:8080
- **Swagger API Explorer**: http://localhost:8080/swagger
- **Health Check**: http://localhost:8080/health

---

## Local Development (Without Docker)

### Backend

```bash
cd backend

# Set user secrets for development
dotnet user-secrets set "Gemini:ProjectId" "your-gcp-project-id"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=ai_scheduler;Username=scheduler;Password=scheduler_dev_pass"

# Start a local PostgreSQL (via Docker)
docker run -d --name scheduler-db \
  -e POSTGRES_DB=ai_scheduler \
  -e POSTGRES_USER=scheduler \
  -e POSTGRES_PASSWORD=scheduler_dev_pass \
  -p 5432:5432 \
  -v "$(pwd)/init/init.sql:/docker-entrypoint-initdb.d/init.sql" \
  postgres:16-alpine

# Run the API
dotnet run
# API available at http://localhost:8080
```

### Frontend

```bash
cd frontend
npm install
npm run dev
# Dev server at http://localhost:5173 (proxies /api to port 8080)
```

---

## Testing Instructions

The AI Manufacturing Scheduler is designed to be tested entirely through the **AI Copilot** natural language chat interface. 

To test the core capabilities, open the application in your browser and use the AI Copilot on the right side of the screen to trigger these two scenarios:

### Scenario 1: Shipper Delay (Email Integration)
Simulate an incoming email from a supplier regarding a delayed component.
1. Open the AI Copilot chat.
2. Type: *"I just received an email from PlasticsGrp. Due to customs issues, our shipment of Molded Plastic Casings (PLAST-CSG) is delayed by 5 days. Please update the schedule."*
3. Watch as the agent parses the text, checks material availability, and **blocks/delays** all Work Orders dependent on the `PLAST-CSG` purchase order.
4. Click **Approve** to commit the changes to the live schedule.

### Scenario 2: Machine Breakdown
Simulate a real-time hardware failure on the plant floor.
1. In the AI Copilot, type: *"The Casing Assembly Station just broke down unexpectedly."*
2. Watch as the agent evaluates capacity and instantly **blocks** all operations dependent on that machine.
3. Click **Approve**.
4. To resolve it, type: *"Maintenance just finished. The Casing Assembly Station is back online and running perfectly."* The agent will restore the schedule.

*(Note: You can reset the database by restarting the postgres container, which will re-run `init.sql`).*

---

## API Reference

For the full, detailed API reference with request/response examples, see **[docs/api-endpoints.md](docs/api-endpoints.md)**.

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/schedule` | Full dashboard snapshot (optional `?plant=` filter) |
| `PUT` | `/api/schedule/workorders/{id}/status` | Manually update Work Order status |
| `PUT` | `/api/schedule/operations/{id}/status` | Update Operation status (with cascade) |
| `PUT` | `/api/schedule/purchaseorders/{id}/status` | Update PO status + delivery date |
| `PUT` | `/api/purchaseorders/{id}/delay` | Mark PO delayed → triggers AI agent |
| `PUT` | `/api/purchaseorders/{id}/receive` | Mark PO received → agent unblocks WOs |
| `PUT` | `/api/workcenters/{id}/breakdown` | Mark WC down → triggers AI agent |
| `PUT` | `/api/workcenters/{id}/restore` | Restore WC → agent re-promotes WOs |
| `POST` | `/api/agent/run` | Chat / manual agent trigger |
| `POST` | `/api/schedule/reset` | Reset demo to seed state |
| `GET` | `/health` | Health check (DB connectivity) |

---

## Google Cloud Run Deployment

### 1. Enable required GCP APIs

```bash
export PROJECT_ID=your-gcp-project-id
export REGION=us-central1

gcloud config set project $PROJECT_ID

gcloud services enable \
  run.googleapis.com \
  cloudbuild.googleapis.com \
  aiplatform.googleapis.com \
  sqladmin.googleapis.com \
  --project=$PROJECT_ID
```

### 2. Create Cloud SQL (PostgreSQL) instance

```bash
# Create instance (takes ~5 minutes)
gcloud sql instances create ai-scheduler-db \
  --database-version=POSTGRES_16 \
  --tier=db-f1-micro \
  --region=$REGION \
  --project=$PROJECT_ID

# Create database and user
gcloud sql databases create ai_scheduler --instance=ai-scheduler-db
gcloud sql users create scheduler --instance=ai-scheduler-db --password=CHANGE_ME_STRONG_PASSWORD

# Import seed data
gcloud sql import sql ai-scheduler-db gs://YOUR_BUCKET/init.sql \
  --database=ai_scheduler
# (or use Cloud SQL Studio to run init.sql manually)
```

### 3. Build and push container to Artifact Registry

```bash
# Create Artifact Registry repo
gcloud artifacts repositories create ai-scheduler \
  --repository-format=docker \
  --location=$REGION

# Build and push (uses Cloud Build — no local Docker required)
gcloud builds submit \
  --tag $REGION-docker.pkg.dev/$PROJECT_ID/ai-scheduler/app:latest \
  .
```

### 4. Deploy to Cloud Run

```bash
# Get Cloud SQL connection name
INSTANCE_CONNECTION=$(gcloud sql instances describe ai-scheduler-db \
  --format="value(connectionName)")

gcloud run deploy ai-scheduler \
  --image=$REGION-docker.pkg.dev/$PROJECT_ID/ai-scheduler/app:latest \
  --region=$REGION \
  --platform=managed \
  --allow-unauthenticated \
  --add-cloudsql-instances=$INSTANCE_CONNECTION \
  --set-env-vars="
    Gemini__ProjectId=$PROJECT_ID,
    Gemini__Location=$REGION,
    Gemini__ModelId=gemini-3.5-flash,
    ConnectionStrings__DefaultConnection=Host=/cloudsql/$INSTANCE_CONNECTION;Database=ai_scheduler;Username=scheduler;Password=CHANGE_ME_STRONG_PASSWORD
  " \
  --min-instances=1 \
  --memory=512Mi \
  --cpu=1 \
  --timeout=120
```

> **Note**: Cloud Run's service account needs the **Vertex AI User** IAM role:
> ```bash
> gcloud projects add-iam-policy-binding $PROJECT_ID \
>   --member="serviceAccount:$(gcloud run services describe ai-scheduler \
>     --region=$REGION --format='value(spec.template.spec.serviceAccountName)')" \
>   --role="roles/aiplatform.user"
> ```

### 5. Get the service URL

```bash
gcloud run services describe ai-scheduler --region=$REGION \
  --format="value(status.url)"
```

---

## Project Structure

```
ai-scheduler/
│
├── agent/                        # 🤖 AI Agent (Gemini function-calling)
│   └── MasterProductionSchedulerAgent.cs
│
├── backend/                      # ⚙️  .NET 9 Web API
│   ├── AiScheduler.Api.csproj    # Project file (compiles agent/ via link)
│   ├── Program.cs                # Startup, DI, middleware
│   ├── appsettings.json
│   ├── Controllers/
│   │   ├── ScheduleController.cs       # Dashboard + manual status updates
│   │   ├── PurchaseOrdersController.cs # PO delay/receive → triggers agent
│   │   ├── WorkCentersController.cs    # Breakdown/restore → triggers agent
│   │   └── AgentController.cs          # Chat endpoint (POST /api/agent/run)
│   ├── Data/
│   │   └── AppDbContext.cs
│   ├── DTOs/
│   │   └── Dtos.cs
│   ├── Models/
│   │   ├── PurchaseOrder.cs
│   │   ├── Inventory.cs
│   │   ├── WorkCenter.cs
│   │   ├── WorkOrder.cs
│   │   └── WorkOrderOperation.cs
│   └── Services/
│       ├── ISchedulingService.cs
│       └── SchedulingService.cs
│
├── frontend/                     # 🖥️  Vue 3 + Vite + Tailwind CSS
│   ├── package.json
│   ├── vite.config.js
│   ├── index.html
│   └── src/
│       ├── App.vue               # Root layout + 3-pane grid
│       ├── main.js
│       ├── api/scheduler.js      # Axios client
│       ├── stores/schedule.js    # Pinia (polling + simulations)
│       ├── assets/main.css       # Global styles + animations
│       └── components/
│           ├── WorkOrderPane.vue       # Left pane
│           ├── WorkCenterPane.vue      # Middle pane
│           ├── PurchaseOrderPane.vue   # Right pane (inline PO editing)
│           ├── ChatCopilot.vue         # AI chatbot panel
│           ├── SimulationControls.vue  # Action buttons bar
│           └── ToastNotification.vue   # Agent result toast
│
├── database/                     # 🗄️  PostgreSQL
│   └── init.sql                  # Schema + seed data
│
├── docs/                         # 📖 Documentation
│   └── api-endpoints.md          # Full API reference with examples
│
├── Dockerfile                    # Multi-stage: Vue → .NET → runtime
├── docker-compose.yml            # Local dev stack (app + postgres)
├── .dockerignore
├── .env / .env.example
├── README.md
└── RUNBOOK.md
```

---

## Agent Design

The `MasterProductionSchedulerAgent` implements the **Google ADK architecture pattern** using the official `Google.Cloud.AIPlatform.V1` .NET SDK (Vertex AI).

```
User Trigger
    │
    ▼
┌────────────────────────────────────────────┐
│  Gemini 3.5 Flash (System Prompt + Tools)   │
│                                            │
│  Loop (max 12 iterations):                 │
│    ① Model returns FunctionCall(s)         │
│    ② C# dispatches to service method       │
│    ③ Result returned as FunctionResponse   │
│    ④ Repeat until model returns text only  │
└────────────────────────────────────────────┘
    │
    ▼
AgentRunResult { agentReasoning, toolCalls }
    │
    ▼
Frontend toast + schedule refresh
```

**Tools bound to the agent:**

| Tool Name | Returns |
|-----------|---------|
| `assess_material_availability` | `MaterialAvailabilityReport` — per-WO BOM checks |
| `evaluate_work_center_capacity` | `WorkCenterCapacityReport` — utilization + down status |
| `execute_schedule_adjustment` | `ScheduleAdjustmentResult` — mutations committed to DB |

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Vue 3 (Composition API), Pinia, Tailwind CSS, Vite |
| Backend | .NET 9 Web API, Entity Framework Core, Npgsql |
| AI Agent | Google Gemini 3.5 Flash via `Google.Cloud.AIPlatform.V1` (Vertex AI) |
| Database | PostgreSQL 16 (Cloud SQL for production) |
| Deployment | Docker, Google Cloud Run |

---

## License

MIT — Built for the All Things Agentic Hackathon (Taskmaster Track)
