using System.Text.Json;
using Google.Cloud.AIPlatform.V1;
using Google.Protobuf.WellKnownTypes;
using AiScheduler.Api.Services;

namespace AiScheduler.Api.Agent;

public class FloorManagerAgent
{
    private readonly ISchedulingService _schedulingService;
    private readonly ILogger<FloorManagerAgent> _logger;
    private readonly PredictionServiceClient _predictionClient;
    private readonly string _modelName;
    private const int MaxIterations = 5;

    private const string SystemPrompt = 
        """
        You are the Floor Manager Agent for a manufacturing plant. 
        Your ONLY job is to manage and report on Work Centers, machine health, and capacity.
        You have access to tools that can evaluate capacity and update machine status.
        
        When the Master Scheduler (CEO) asks you a question or gives you an instruction:
        1. Use your tools to evaluate the request.
        2. Reply with a clear, concise summary of your findings (e.g., "Press 1 is down, but Press 2 has capacity").
        3. Do NOT make broad schedule adjustments; that is the CEO's job.
        """;

    public FloorManagerAgent(
        ISchedulingService schedulingService,
        ILogger<FloorManagerAgent> logger,
        IConfiguration configuration)
    {
        _schedulingService = schedulingService;
        _logger = logger;
        
        var projectId = configuration["Gemini:ProjectId"] 
            ?? throw new InvalidOperationException("Gemini:ProjectId missing.");
        var location = configuration["Gemini:Location"] ?? "us-central1";
        
        _predictionClient = new PredictionServiceClientBuilder
        {
            Endpoint = $"{location}-aiplatform.googleapis.com:443"
        }.Build();
        
        _modelName = $"projects/{projectId}/locations/{location}/publishers/google/models/gemini-2.5-flash";
    }

    public async Task<string> ConsultAsync(string query)
    {
        _logger.LogInformation("Floor Manager received query: {Query}", query);
        
        var tools = BuildToolDeclarations();
        var conversationHistory = new List<Content>
        {
            new Content { Role = "user", Parts = { new Part { Text = query } } }
        };
        
        for (int i = 0; i < MaxIterations; i++)
        {
            var request = new GenerateContentRequest
            {
                Model = _modelName,
                SystemInstruction = new Content { Parts = { new Part { Text = SystemPrompt } } },
                Tools = { tools }
            };
            request.Contents.AddRange(conversationHistory);
            
            var response = await _predictionClient.GenerateContentAsync(request);
            var responseContent = response.Candidates[0].Content;
            conversationHistory.Add(responseContent);
            
            var functionCalls = responseContent.Parts
                .Where(p => p.FunctionCall != null)
                .Select(p => p.FunctionCall)
                .ToList();
                
            if (!functionCalls.Any())
            {
                var finalAnswer = string.Join("\n", responseContent.Parts.Select(p => p.Text));
                _logger.LogInformation("Floor Manager finished: {Answer}", finalAnswer);
                return finalAnswer;
            }
            
            var functionResponses = new Content { Role = "model" };
            foreach (var fc in functionCalls)
            {
                try 
                {
                    var result = await DispatchToolAsync(fc.Name, fc);
                    functionResponses.Parts.Add(new Part 
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Name = fc.Name,
                            Response = SerializeToStruct(result)
                        }
                    });
                }
                catch (Exception ex)
                {
                    functionResponses.Parts.Add(new Part
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Name = fc.Name,
                            Response = SerializeToStruct(new { error = ex.Message })
                        }
                    });
                }
            }
            conversationHistory.Add(functionResponses);
        }
        
        return "Floor Manager reached maximum iterations without completing the request.";
    }

    private Tool BuildToolDeclarations()
    {
        var tool = new Tool();
        
        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name = "evaluate_work_center_capacity",
            Description = "Evaluates the capacity of all work centers to identify bottlenecks or idle time."
        });

        var updateWcParams = new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Object };
        updateWcParams.Properties.Add("name", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.String });
        updateWcParams.Properties.Add("new_status", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.String });
        updateWcParams.Required.Add("name"); updateWcParams.Required.Add("new_status");

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name = "update_work_center_status",
            Description = "Updates the status of a work center by name (e.g. 'Down', 'Active').",
            Parameters = updateWcParams
        });
        
        return tool;
    }

    private async Task<object> DispatchToolAsync(string toolName, FunctionCall fc) => toolName switch
    {
        "evaluate_work_center_capacity" => await _schedulingService.EvaluateWorkCenterCapacityAsync(),
        "update_work_center_status" => await _schedulingService.UpdateWorkCenterByNameAsync(
            fc.Args.Fields.TryGetValue("name", out var wcName) ? wcName.StringValue : "",
            fc.Args.Fields.TryGetValue("new_status", out var wcSt) ? wcSt.StringValue : "Down"),
        _ => throw new InvalidOperationException($"Unknown tool: '{toolName}'")
    };

    private static Struct SerializeToStruct(object obj)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        string json = JsonSerializer.Serialize(obj, options);
        return Struct.Parser.ParseJson(json);
    }
}
