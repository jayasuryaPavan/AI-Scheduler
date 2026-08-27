# AI Scheduler Runbook

This guide covers how to start the AI Manufacturing Scheduler locally. Because recent updates added Shifts, Min/Max Inventory, and Setup Times, the database schema has changed. Follow the instructions below to ensure a clean start.

---

## Method 1: Using Docker Compose (Recommended)

This is the easiest way to run the full stack (PostgreSQL, .NET 9 API, and Vue 3 Frontend) simultaneously.

### 1. Configure the Environment
Ensure your `.env` file in the project root (`c:\Work Space\Hackathons\AI Scheduler`) is set up:
```dotenv
GEMINI_PROJECT_ID=your-actual-gcp-project-id
GEMINI_LOCATION=us-central1
GEMINI_MODEL_ID=gemini-3.5-flash
DB_PASSWORD=scheduler_dev_pass
GCP_ADC_PATH=~/.config/gcloud/application_default_credentials.json
```

### 2. Authenticate with Google Cloud
Ensure your machine is authenticated with Google Cloud so the AI Agent can access Vertex AI:
```bash
gcloud auth login
gcloud auth application-default login
```

### 3. Start the Application
Because the database schema has been updated, you must remove any old database volumes before starting to allow the new `init.sql` seed script to run.

Run the following commands from the project root (`c:\Work Space\Hackathons\AI Scheduler`):
```powershell
# Stop existing containers and delete the old database volume
docker compose down -v

# Build and start the containers in the background
docker compose up -d --build
```
*Note: The first run may take a few minutes to download the base images and build the .NET/Vue containers.*

### 4. Access the Dashboard
- **Plant Floor Dashboard:** [http://localhost:8080](http://localhost:8080)
- **API Swagger UI:** [http://localhost:8080/swagger](http://localhost:8080/swagger)

---

## Method 2: Local Development (Manual Start)

If you prefer to run the API and Frontend locally (e.g., via your IDE or terminal) and only use Docker for the database.

### 1. Start the PostgreSQL Database
```powershell
cd "c:\Work Space\Hackathons\AI Scheduler"
docker compose down -v
docker run -d --name scheduler-db -e POSTGRES_DB=ai_scheduler -e POSTGRES_USER=scheduler -e POSTGRES_PASSWORD=scheduler_dev_pass -p 5432:5432 -v "$PWD/backend/init/init.sql:/docker-entrypoint-initdb.d/init.sql" postgres:16-alpine
```

### 2. Start the .NET Backend
```powershell
cd "c:\Work Space\Hackathons\AI Scheduler\backend"
dotnet run
```
*The API will be available at http://localhost:8080*

### 3. Start the Vue Frontend
In a new terminal window:
```powershell
cd "c:\Work Space\Hackathons\AI Scheduler\frontend"
npm install
npm run dev
```
*The Dev server will start at http://localhost:5173 (which automatically proxies `/api` calls to port 8080).*

---

## Troubleshooting

> [!WARNING]
> **Data not refreshing?** If the UI does not show Shifts or the new Min/Max inventory values, it means your PostgreSQL container is using an outdated database volume. Run `docker compose down -v` and restart to fix this.

> [!NOTE]
> **Gemini Connection Errors?** If the agent fails to run, double-check your `.env` variables and ensure your `GCP_ADC_PATH` correctly points to your Google Cloud credentials JSON file.
