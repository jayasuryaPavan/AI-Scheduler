using Microsoft.EntityFrameworkCore;
using Google.Cloud.AIPlatform.V1;
using Google.Protobuf.WellKnownTypes;
using AiScheduler.Api.Data;
using AiScheduler.Api.DTOs;
using AiScheduler.Api.Services;
using System.Text.Json;

namespace AiScheduler.Api.Agent;

/// <summary>
/// Autonomous Master Production Scheduler Agent.
///
/// Implements the Google ADK agent architecture pattern using the official
/// Google.Cloud.AIPlatform.V1 SDK (Vertex AI). Binds three tools to Gemini
/// and runs an agentic function-calling loop until the model returns a final
/// text response (no more tool calls).
///
/// Tools:
///   1. assess_material_availability   â†’ cross-references BOM vs inventory + POs
///   2. evaluate_work_center_capacity  â†’ checks machine status + capacity load
///   3. execute_schedule_adjustment    â†’ blocks/promotes WOs and commits to DB
/// </summary>
public sealed class MasterProductionSchedulerAgent
{
    private readonly ISchedulingService                      _schedulingService;
    private readonly ILogger<MasterProductionSchedulerAgent> _logger;
    private readonly PredictionServiceClient                 _predictionClient;
    private readonly AppDbContext                            _db;
    private readonly IApprovalStore                          _approvalStore;
    private readonly IChatMemoryService                      _chatMemory;
    private readonly IAgentMemoryService                     _agentMemory;
    private readonly FloorManagerAgent                       _floorManager;
    private readonly ProcurementAgent                        _procurementAgent;
    private readonly string                                  _modelName;

    // Max iterations prevents infinite tool-calling loops
    private const int MaxIterations = 12;

    private const string SystemPrompt = """
        You are an autonomous Master Production Scheduler (MPS) AI for a precision manufacturing facility.
        Your mission: optimize the production schedule to maximize throughput while respecting material
        availability and work center capacity constraints.

        Operating Principles:
        - Work Orders (Jobs) are sequenced by Priority (1 = highest). Lower priority fills capacity gaps.
        - A Work Order is BLOCKED when: (a) required materials are on a DELAYED Purchase Order with zero
          on-hand stock, OR (b) a required Work Center is DOWN.
        - Operations within a Work Order must be executed in ascending OperationSequence order (10 â†’ 20 â†’ 30).
        - When disruptions occur, immediately assess impact, block infeasible Work Orders, and promote the
          next viable candidates to fill the schedule.

        Domain Knowledge (Pen Manufacturing Process):
        1. Fill ink in the refills.
        2. Parallel process: Brand stamping on outer plastics from another plant. If stock is insufficient, raise a transfer order from Plant 2 for stamped cases.
        3. Casing & Assembly: Ink and brand-stamped cases come together to assemble the entire pen.
        4. Packaging: Pens are sent in a bunch, sorted, and packed into packages of 30, 50, or 100, which are then packed into larger boxes of 10 packs.

        You have TWO modes of operation:

        MODE 1: Full schedule optimization (ONLY when the user explicitly asks to optimize or review the schedule, without mentioning any new changes or disruptions):
        1. Consult the Procurement Agent to check inventory and PO status.
        2. Consult the Floor Manager to check machine capacity.
        3. Use `execute_schedule_adjustment` to commit the optimized schedule.

        MODE 2: Conversational disruption reports (when a user mentions ANY change, disruption, or need to update the schedule):
        First, ANALYZE the user's statement for completeness. If the user's statement is ambiguous, vague, or missing critical details, DO NOT CALL ANY TOOLS. Instead, reply directly to the user with a clarifying question to get the exact details.
        
        ONLY when you have all necessary details, consult the appropriate sub-agent (Procurement Agent or Floor Manager) to investigate and apply the change. For example, if the user says "the ink is delayed by 2 hours", ask the Procurement Agent to delay the ink PO. If they say "assembly line is down", ask the Floor Manager to update the assembly line status. You may also use `update_work_order_priority` directly.

        After consulting your sub-agents, run `execute_schedule_adjustment` to cascade the impact through the schedule.

        After all tool calls complete, provide a concise, conversational summary (2-4 sentences) that
        explains: (a) what disruption was detected, (b) which data was updated, (c) which Work Orders
        were blocked and why, (d) which Work Orders were promoted to fill the queue.
        
        CRITICAL COMMUNICATION RULE:
        Keep your final response extremely simple and natural. DO NOT mention the names of the tools you called, DO NOT mention SQL, and DO NOT mention internal code concepts like "savepoints" or "transactions". Speak to the user like a human supply chain assistant (e.g. say "I ran a simulation" instead of "I called the create_savepoint tool").
        
        Self-Reflection & Iteration (Tree of Thoughts):
        When asked to solve a complex scheduling problem, DO NOT settle for your first idea. You must explore multiple branches:
        1. Call `create_savepoint` with a unique name (e.g., "StrategyA").
        2. Apply your first strategy using the update tools or raw SQL.
        3. Call `evaluate_schedule_metrics` to grade the schedule.
        4. Call `rollback_to_savepoint` to undo the changes.
        5. Create a new savepoint ("StrategyB"), apply your second strategy, and evaluate it.
        6. Finally, rollback and apply the strategy that yielded the best metrics (fewest late orders, lowest idle time).
        In your final simple response, explain the business strategies you tested, their scores, and why you picked the winner, without ever mentioning the technical tools used to do so.
        
        Long-Term Memory:
        If the user tells you a preference, a rule, or explains why they rejected a previous proposal (e.g., "never delay customer X", "I prefer splitting batches"), you must use the `store_user_preference` tool to save this to your long-term memory so you don't forget it.
        """;

    public MasterProductionSchedulerAgent(
        ISchedulingService                      schedulingService,
        ILogger<MasterProductionSchedulerAgent> logger,
        IConfiguration                          configuration,
        AppDbContext                            db,
        IApprovalStore                          approvalStore,
        IChatMemoryService                      chatMemory,
        IAgentMemoryService                     agentMemory,
        FloorManagerAgent                       floorManager,
        ProcurementAgent                        procurementAgent)
    {
        _schedulingService = schedulingService;
        _logger            = logger;
        _db                = db;
        _approvalStore     = approvalStore;
        _chatMemory        = chatMemory;
        _agentMemory       = agentMemory;
        _floorManager      = floorManager;
        _procurementAgent  = procurementAgent;

        var projectId  = configuration["Gemini:ProjectId"]
                         ?? throw new InvalidOperationException("Gemini:ProjectId is not configured.");
        var location   = configuration["Gemini:Location"] ?? "us-central1";
        var modelId    = configuration["Gemini:ModelId"]  ?? "gemini-3.5-flash";

        _modelName = $"projects/{projectId}/locations/{location}/publishers/google/models/{modelId}";

        _predictionClient = new PredictionServiceClientBuilder
        {
            Endpoint = $"{location}-aiplatform.googleapis.com"
        }.Build();

        _logger.LogInformation("Agent initialized â€” model: {Model}", _modelName);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Public entry point
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public async Task<AgentRunResult> RunAsync(string? triggerMessage = null, string? sessionId = null)
    {
        var runResult = new AgentRunResult
        {
            StartedAt = DateTimeOffset.UtcNow,
            ToolCalls = new List<string>()
        };

        _logger.LogInformation("Agent run started (SANDBOX). Trigger: {Trigger}", triggerMessage ?? "(manual)");

        // Collected impacts from schedule adjustments executed inside the sandbox
        var simulatedImpact = new List<string>();

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // â”€â”€ Open a transaction so ALL tool changes can be rolled back â”€â”€
            await using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                var tools              = BuildToolDeclarations();
                var conversationHistory = new List<Content>();

                var userPrompt = triggerMessage
                    ?? "A schedule review has been requested. Assess all constraints and optimize the production schedule.";

                conversationHistory.Add(new Content
                {
                    Role  = "user",
                    Parts = { new Part { Text = userPrompt } }
                });

                // â”€â”€ Retrieve relevant past conversations for memory â”€â”€â”€â”€â”€â”€â”€â”€
                string memoryContext = "";
                if (!string.IsNullOrWhiteSpace(triggerMessage))
                {
                    try
                    {
                        var similar = await _chatMemory.RetrieveSimilarAsync(triggerMessage, topK: 10);
                        if (similar.Count > 0)
                        {
                            memoryContext = "\n\n## Relevant Past Conversations\n" +
                                "The following are relevant past interactions for context. Use them to maintain continuity:\n" +
                                string.Join("\n", similar.Select((h, i) =>
                                    $"{i + 1}. [{h.Role}] ({h.CreatedAt:MMM dd HH:mm}): {(h.Content.Length > 200 ? h.Content[..200] + "..." : h.Content)}"));
                            _logger.LogInformation("Injected {Count} past conversation(s) into agent context", similar.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to retrieve chat memory â€” proceeding without context");
                    }
                }

                // â”€â”€ Retrieve relevant agent preferences (long term memory) â”€
                string agentMemoryContext = "";
                if (!string.IsNullOrWhiteSpace(triggerMessage))
                {
                    try
                    {
                        var prefs = await _agentMemory.RetrieveRelevantPreferencesAsync(triggerMessage, topK: 5);
                        if (prefs.Count > 0)
                        {
                            agentMemoryContext = "\n\n## Learned User Preferences\n" +
                                "The following are learned preferences from the user. You MUST adhere to these rules when proposing a schedule:\n" +
                                string.Join("\n", prefs.Select((p, i) => $"- {p.MemoryText}"));
                            _logger.LogInformation("Injected {Count} learned preference(s) into agent context", prefs.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to retrieve agent preferences");
                    }
                }

                // â”€â”€ Agentic reasoning loop â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                for (int iteration = 0; iteration < MaxIterations; iteration++)
                {
                    _logger.LogDebug("Agent iteration {Iteration}/{Max}", iteration + 1, MaxIterations);

                    var request = new GenerateContentRequest
                    {
                        Model             = _modelName,
                        SystemInstruction = new Content
                        {
                            Parts = { new Part { Text = SystemPrompt + memoryContext + agentMemoryContext } }
                        },
                        Tools             = { tools },
                        GenerationConfig  = new GenerationConfig
                        {
                            Temperature     = 0.1f,
                            MaxOutputTokens = 2048
                        }
                    };

                    foreach (var content in conversationHistory)
                        request.Contents.Add(content);

                    GenerateContentResponse response =
                        await _predictionClient.GenerateContentAsync(request);

                    var candidate = response.Candidates.FirstOrDefault();
                    if (candidate is null)
                    {
                        runResult.Error = "Gemini returned no candidates.";
                        break;
                    }

                    conversationHistory.Add(candidate.Content);

                    var functionCallParts = candidate.Content.Parts
                        .Where(p => p.FunctionCall != null)
                        .ToList();

                    if (functionCallParts.Count == 0)
                    {
                        runResult.AgentReasoning = string.Concat(
                            candidate.Content.Parts
                                .Where(p => !string.IsNullOrWhiteSpace(p.Text))
                                .Select(p => p.Text));

                        _logger.LogInformation("Agent reasoning complete in {Iterations} iterations", iteration + 1);
                        break;
                    }

                    var functionResponseParts = new List<Part>();

                    foreach (var fcPart in functionCallParts)
                    {
                        var fc       = fcPart.FunctionCall;
                        var toolName = fc.Name;

                        _logger.LogInformation("Agent â†’ tool call: {Tool}", toolName);
                        runResult.ToolCalls.Add(toolName);

                        object toolResult = await DispatchToolAsync(toolName, fc);

                        // Capture cascading impacts from schedule adjustments
                        if (toolResult is ScheduleAdjustmentResult sar)
                            simulatedImpact.AddRange(sar.Actions);
                        if (toolResult is WorkOrderPriorityUpdateResult wopr && wopr.Success)
                            simulatedImpact.Add(wopr.Message);
                        if (toolResult is PurchaseOrderUpdateResult pour && pour.Success)
                            simulatedImpact.Add(pour.Message);
                        if (toolResult is WorkCenterUpdateResult wcur && wcur.Success)
                            simulatedImpact.Add(wcur.Message);

                        Struct resultStruct = SerializeToStruct(toolResult);

                        functionResponseParts.Add(new Part
                        {
                            FunctionResponse = new FunctionResponse
                            {
                                Name     = toolName,
                                Response = resultStruct
                            }
                        });
                    }

                    var toolResponseContent = new Content { Role = "user" };
                    foreach (var part in functionResponseParts)
                        toolResponseContent.Parts.Add(part);

                    conversationHistory.Add(toolResponseContent);
                }

                runResult.Success     = true;
                runResult.CompletedAt = DateTimeOffset.UtcNow;

                if (string.IsNullOrWhiteSpace(runResult.AgentReasoning))
                    runResult.AgentReasoning = $"Schedule optimization complete. Tools executed: {string.Join(", ", runResult.ToolCalls)}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent run failed");
                runResult.Error       = ex.Message;
                runResult.Success     = false;
                runResult.CompletedAt = DateTimeOffset.UtcNow;
            }

            // â”€â”€ ROLLBACK â€” nothing is committed to the live DB â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            await transaction.RollbackAsync();
            _logger.LogInformation("Sandbox transaction rolled back. {ImpactCount} simulated impacts captured.", simulatedImpact.Count);
        });

        // â”€â”€ If any mutation tools were called, store as a proposal â”€â”€â”€
        bool hasMutations = runResult.ToolCalls.Any(t =>
            t is "execute_schedule_adjustment" or "update_purchase_order_status"
               or "update_work_center_status" or "update_work_order_priority"
               or "manage_inventory_levels" or "execute_raw_sql");

        if (hasMutations && runResult.Success)
        {
            var proposalId = _approvalStore.Save(new PendingProposal
            {
                UserPrompt      = triggerMessage ?? "",
                SimulatedImpact = simulatedImpact,
                ToolCalls       = runResult.ToolCalls,
                AgentReasoning  = runResult.AgentReasoning
            });

            runResult.RequiresApproval = true;
            runResult.ProposalId       = proposalId;
            runResult.SimulatedImpact  = simulatedImpact;

            _logger.LogInformation("Proposal {Id} saved for human approval.", proposalId);
        }

        // â”€â”€ Store conversation in chat memory â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (!string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(triggerMessage))
        {
            try
            {
                await _chatMemory.StoreConversationTurnAsync(sessionId, "user", triggerMessage);
                if (!string.IsNullOrWhiteSpace(runResult.AgentReasoning))
                    await _chatMemory.StoreConversationTurnAsync(
                        sessionId, 
                        "assistant", 
                        runResult.AgentReasoning, 
                        runResult.ToolCalls,
                        runResult.RequiresApproval,
                        runResult.ProposalId,
                        runResult.SimulatedImpact,
                        runResult.RequiresApproval ? "pending" : ""
                    );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to store conversation in chat memory");
            }
        }

        return runResult;
    }

    /// <summary>
    /// Deterministically replays the agent's tool calls against the live database
    /// after human approval. No LLM call needed â€” we just re-execute the same tools.
    /// </summary>
    public async Task<AgentRunResult> ReplayAsync(string proposalId)
    {
        var proposal = _approvalStore.Get(proposalId)
            ?? throw new InvalidOperationException($"Proposal '{proposalId}' not found or already applied.");

        _logger.LogInformation("Replaying approved proposal {Id}: {Prompt}", proposalId, proposal.UserPrompt);

        // Re-run the agent with the original prompt â€” this time WITHOUT a transaction wrapper,
        // so changes commit to the live database.
        var replayResult = await RunDirectAsync(proposal.UserPrompt);

        _approvalStore.Remove(proposalId);

        return replayResult;
    }

    /// <summary>
    /// Runs the agent loop and commits changes directly (no sandbox). 
    /// Used for approved replays and disruption simulations.
    /// </summary>
    public async Task<AgentRunResult> RunDirectAsync(string? triggerMessage = null)
    {
        var runResult = new AgentRunResult
        {
            StartedAt = DateTimeOffset.UtcNow,
            ToolCalls = new List<string>()
        };

        _logger.LogInformation("Agent run started (DIRECT/APPROVED). Trigger: {Trigger}", triggerMessage ?? "(manual)");

        try
        {
            var tools              = BuildToolDeclarations();
            var conversationHistory = new List<Content>();

            var userPrompt = triggerMessage
                ?? "A schedule review has been requested. Assess all constraints and optimize the production schedule.";

            conversationHistory.Add(new Content
            {
                Role  = "user",
                Parts = { new Part { Text = userPrompt } }
            });

            // â”€â”€ Retrieve relevant agent preferences (long term memory) â”€
            string agentMemoryContext = "";
            if (!string.IsNullOrWhiteSpace(triggerMessage))
            {
                try
                {
                    var prefs = await _agentMemory.RetrieveRelevantPreferencesAsync(triggerMessage, topK: 5);
                    if (prefs.Count > 0)
                    {
                        agentMemoryContext = "\n\n## Learned User Preferences\n" +
                            "The following are learned preferences from the user. You MUST adhere to these rules when proposing a schedule:\n" +
                            string.Join("\n", prefs.Select((p, i) => $"- {p.MemoryText}"));
                    }
                }
                catch { }
            }

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                var request = new GenerateContentRequest
                {
                    Model             = _modelName,
                    SystemInstruction = new Content
                    {
                        Parts = { new Part { Text = SystemPrompt + agentMemoryContext } }
                    },
                    Tools             = { tools },
                    GenerationConfig  = new GenerationConfig
                    {
                        Temperature     = 0.1f,
                        MaxOutputTokens = 2048
                    }
                };

                foreach (var content in conversationHistory)
                    request.Contents.Add(content);

                var response = await _predictionClient.GenerateContentAsync(request);
                var candidate = response.Candidates.FirstOrDefault();
                if (candidate is null) { runResult.Error = "Gemini returned no candidates."; break; }

                conversationHistory.Add(candidate.Content);

                var functionCallParts = candidate.Content.Parts
                    .Where(p => p.FunctionCall != null).ToList();

                if (functionCallParts.Count == 0)
                {
                    runResult.AgentReasoning = string.Concat(
                        candidate.Content.Parts
                            .Where(p => !string.IsNullOrWhiteSpace(p.Text))
                            .Select(p => p.Text));
                    break;
                }

                var functionResponseParts = new List<Part>();
                foreach (var fcPart in functionCallParts)
                {
                    var fc = fcPart.FunctionCall;
                    _logger.LogInformation("Agent â†’ tool call: {Tool}", fc.Name);
                    runResult.ToolCalls.Add(fc.Name);

                    object toolResult = await DispatchToolAsync(fc.Name, fc);
                    functionResponseParts.Add(new Part
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Name     = fc.Name,
                            Response = SerializeToStruct(toolResult)
                        }
                    });
                }

                var toolResponseContent = new Content { Role = "user" };
                foreach (var part in functionResponseParts)
                    toolResponseContent.Parts.Add(part);
                conversationHistory.Add(toolResponseContent);
            }

            runResult.Success     = true;
            runResult.CompletedAt = DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(runResult.AgentReasoning))
                runResult.AgentReasoning = $"Schedule optimization complete. Tools executed: {string.Join(", ", runResult.ToolCalls)}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent direct run failed");
            runResult.Error       = ex.Message;
            runResult.Success     = false;
            runResult.CompletedAt = DateTimeOffset.UtcNow;
        }

        return runResult;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Tool definitions (ADK-style function declarations)
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static Tool BuildToolDeclarations()
    {
        var tool = new Tool();

        var consultParams = new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Object };
        consultParams.Properties.Add("query", new OpenApiSchema
        {
            Type        = Google.Cloud.AIPlatform.V1.Type.String,
            Description = "The question or instruction to pass to the sub-agent."
        });
        consultParams.Required.Add("query");

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name        = "consult_floor_manager",
            Description = "Consults the Floor Manager Agent. Use this to check work center capacity or update machine status.",
            Parameters  = consultParams
        });
        
        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name        = "consult_procurement_manager",
            Description = "Consults the Procurement Manager Agent. Use this to check inventory, purchase orders, shipments, or weather delays.",
            Parameters  = consultParams
        });

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name        = "execute_schedule_adjustment",
            Description =
                "The core autonomous scheduling mutation. Evaluates all Scheduled and Blocked Work Orders " +
                "and: (1) BLOCKS Work Orders whose required materials are unavailable due to Delayed POs, " +
                "or whose required Work Centers are Down; (2) PROMOTES previously-Blocked Work Orders whose " +
                "constraints have been resolved; (3) Re-sequences remaining Scheduled Work Orders by due date " +
                "to fill capacity gaps. Commits all changes to the database atomically."
        });

        // â”€â”€ Chat-driven mutation tools â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        var woUpdateParams = new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Object };
        woUpdateParams.Properties.Add("description", new OpenApiSchema
        {
            Type        = Google.Cloud.AIPlatform.V1.Type.String,
            Description = "A keyword or phrase describing the Work Order product name or order number (e.g. 'delta basic', 'alpha')."
        });
        woUpdateParams.Properties.Add("priority", new OpenApiSchema
        {
            Type        = Google.Cloud.AIPlatform.V1.Type.Integer,
            Description = "The new priority to set (1 is highest, lower priority fills capacity gaps)."
        });
        woUpdateParams.Required.Add("description");
        woUpdateParams.Required.Add("priority");

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name        = "update_work_order_priority",
            Description =
                "Searches for a Work Order by a keyword in its product name or order number, then updates its priority. " +
                "Use this when a user asks to prioritize, expedite, or deprioritize a specific order.",
            Parameters  = woUpdateParams
        });
        var rawSqlParams = new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Object };
        rawSqlParams.Properties.Add("sql_query", new OpenApiSchema
        {
            Type        = Google.Cloud.AIPlatform.V1.Type.String,
            Description = "The raw PostgreSQL query to execute (e.g. UPDATE, INSERT, DELETE, SELECT)."
        });
        rawSqlParams.Required.Add("sql_query");

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name        = "execute_raw_sql",
            Description =
                "Executes raw SQL against the PostgreSQL database. Use this tool for direct database manipulation, " +
                "such as changing due dates, deleting records, adding new entries, or running custom selects. " +
                "Available EF Core tables (use double quotes for case sensitivity): " +
                "\"WorkOrders\" (Id, WorkOrderNumber, DueDate, Status), " +
                "\"WorkOrderOperations\" (Id, WorkOrderId, OperationSequence), " +
                "\"PurchaseOrders\" (Id, PartNumber, ExpectedDeliveryDate), " +
                "\"WorkCenters\", \"Inventory\", \"Shifts\". " +
                "Do NOT ask the user for table names or schema details. Assume these tables exist. " +
                "Any modifications will be captured in the sandbox and require human approval.",
            Parameters  = rawSqlParams
        });

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name        = "evaluate_schedule_metrics",
            Description = "Evaluates the current state of the schedule and returns a score based on total late orders, " +
                          "machine idle time, and constraint violations. Use this to critique your own changes."
        });

        var savepointParams = new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Object };
        savepointParams.Properties.Add("savepoint_name", new OpenApiSchema
        {
            Type        = Google.Cloud.AIPlatform.V1.Type.String,
            Description = "A unique name for the savepoint."
        });
        savepointParams.Required.Add("savepoint_name");

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name        = "create_savepoint",
            Description = "Creates a named savepoint in the current database transaction. " +
                          "Use this before trying a scheduling strategy so you can rollback if the metrics are poor.",
            Parameters  = savepointParams
        });

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name        = "rollback_to_savepoint",
            Description = "Rolls back the database state to a previously created savepoint. " +
                          "Use this to undo a scheduling strategy that resulted in poor metrics.",
            Parameters  = savepointParams
        });

        var storePrefParams = new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Object };
        storePrefParams.Properties.Add("preference_text", new OpenApiSchema
        {
            Type        = Google.Cloud.AIPlatform.V1.Type.String,
            Description = "The specific rule or preference to store (e.g., 'Never delay VIP orders', 'Always split batches before delaying')."
        });
        storePrefParams.Required.Add("preference_text");

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name        = "store_user_preference",
            Description = "Stores a learned user preference or rule in long-term memory so the agent will adhere to it in future runs.",
            Parameters  = storePrefParams
        });

        return tool;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Tool dispatcher
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task<object> DispatchToolAsync(string toolName, FunctionCall fc) => toolName switch
    {
        "consult_floor_manager"         => new { Result = await _floorManager.ConsultAsync(fc.Args.Fields.TryGetValue("query", out var q1) ? q1.StringValue : "") },
        "consult_procurement_manager"   => new { Result = await _procurementAgent.ConsultAsync(fc.Args.Fields.TryGetValue("query", out var q2) ? q2.StringValue : "") },
        "execute_schedule_adjustment"   => await _schedulingService.ExecuteScheduleAdjustmentAsync(),
        "update_work_order_priority"    => await _schedulingService.UpdateWorkOrderPriorityByDescriptionAsync(
            fc.Args.Fields.TryGetValue("description", out var woDesc) ? woDesc.StringValue : "",
            fc.Args.Fields.TryGetValue("priority", out var pri)       ? (int)pri.NumberValue : 50),
        "execute_raw_sql"               => new { Result = await _schedulingService.ExecuteRawSqlAsync(
            fc.Args.Fields.TryGetValue("sql_query", out var sql)      ? sql.StringValue : "") },
        "evaluate_schedule_metrics"     => await _schedulingService.EvaluateScheduleMetricsAsync(),
        "create_savepoint"              => new { Result = await _schedulingService.CreateSavepointAsync(
            fc.Args.Fields.TryGetValue("savepoint_name", out var sp1) ? sp1.StringValue : "sp_temp") },
        "rollback_to_savepoint"         => new { Result = await _schedulingService.RollbackToSavepointAsync(
            fc.Args.Fields.TryGetValue("savepoint_name", out var sp2) ? sp2.StringValue : "sp_temp") },
        "store_user_preference"         => await StoreUserPreferenceWrapperAsync(
            fc.Args.Fields.TryGetValue("preference_text", out var prefText) ? prefText.StringValue : ""),
        _                               => throw new InvalidOperationException($"Unknown tool: '{toolName}'")
    };

    private async Task<object> StoreUserPreferenceWrapperAsync(string prefText)
    {
        if (string.IsNullOrWhiteSpace(prefText)) return new { Success = false, Message = "Preference text was empty." };
        await _agentMemory.StorePreferenceAsync(prefText);
        return new { Success = true, Message = "Preference stored successfully." };
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Helpers
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static Struct SerializeToStruct(object obj)
    {
        // Serialize the C# result to JSON then parse into a Protobuf Struct,
        // which is what Gemini expects as a FunctionResponse.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Converters                  = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        string json = JsonSerializer.Serialize(obj, options);
        return Struct.Parser.ParseJson(json);
    }
}

