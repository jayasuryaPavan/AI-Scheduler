using System.Text.Json;
using Google.Cloud.AIPlatform.V1;
using Google.Protobuf.WellKnownTypes;
using AiScheduler.Api.Services;

namespace AiScheduler.Api.Agent;

public class ProcurementAgent
{
    private readonly ISchedulingService _schedulingService;
    private readonly ILogger<ProcurementAgent> _logger;
    private readonly PredictionServiceClient _predictionClient;
    private readonly string _modelName;
    private const int MaxIterations = 5;

    private const string SystemPrompt = 
        """
        You are the Procurement Manager Agent for a manufacturing plant. 
        Your ONLY job is to manage and report on Inventory, Purchase Orders, and Logistics.
        You have access to tools that can check inventory, update POs, and check real-world external tracking and weather APIs.
        
        When the Master Scheduler (CEO) asks you a question or gives you an instruction:
        1. Use your tools to evaluate the request. For example, check weather or tracking APIs if there is a logistics question.
        2. Reply with a clear, concise summary of your findings (e.g., "PO 123 is delayed due to a storm in Miami").
        3. Do NOT make broad schedule adjustments; that is the CEO's job.
        
        When inventory drops low or when instructed to order parts:
        1. Use the `email_supplier` tool to draft and send an email to the supplier requesting the parts.
        """;

    public ProcurementAgent(
        ISchedulingService schedulingService,
        ILogger<ProcurementAgent> logger,
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
        _logger.LogInformation("Procurement Manager received query: {Query}", query);
        
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
                _logger.LogInformation("Procurement Manager finished: {Answer}", finalAnswer);
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
        
        return "Procurement Manager reached maximum iterations without completing the request.";
    }

    private Tool BuildToolDeclarations()
    {
        var tool = new Tool();
        
        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name = "manage_inventory_levels",
            Description = "Evaluates inventory levels against upcoming work orders and identifies shortages."
        });

        var updatePoParams = new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Object };
        updatePoParams.Properties.Add("description", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.String });
        updatePoParams.Properties.Add("new_status", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.String });
        updatePoParams.Properties.Add("delay_days", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Number });
        updatePoParams.Required.Add("description"); updatePoParams.Required.Add("new_status"); updatePoParams.Required.Add("delay_days");

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name = "update_purchase_order_status",
            Description = "Updates PO status by description (e.g. 'Delayed', 'Received') and shifts delivery dates.",
            Parameters = updatePoParams
        });

        var shipmentParams = new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Object };
        shipmentParams.Properties.Add("tracking_number_or_description", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.String });
        shipmentParams.Required.Add("tracking_number_or_description");

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name = "check_shipment_tracking",
            Description = "Calls an external shipping API (e.g. FedEx/UPS) to check the real-world status of a shipment.",
            Parameters = shipmentParams
        });
        
        var weatherParams = new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Object };
        weatherParams.Properties.Add("location", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.String });
        weatherParams.Required.Add("location");

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name = "check_weather_forecast",
            Description = "Calls an external Weather API to check for storms or conditions that might disrupt supply chains.",
            Parameters = weatherParams
        });
        
        var emailParams = new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Object };
        emailParams.Properties.Add("supplier_name", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.String });
        emailParams.Properties.Add("part_number", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.String });
        emailParams.Properties.Add("quantity", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.Integer });
        emailParams.Properties.Add("urgency", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.String, Description = "e.g., Normal, High, Critical" });
        emailParams.Properties.Add("message_body", new OpenApiSchema { Type = Google.Cloud.AIPlatform.V1.Type.String, Description = "The drafted email body" });
        emailParams.Required.Add("supplier_name"); emailParams.Required.Add("part_number"); emailParams.Required.Add("quantity"); emailParams.Required.Add("urgency"); emailParams.Required.Add("message_body");

        tool.FunctionDeclarations.Add(new FunctionDeclaration
        {
            Name = "email_supplier",
            Description = "Drafts and sends an email to a supplier to request parts.",
            Parameters = emailParams
        });
        
        return tool;
    }

    private async Task<object> DispatchToolAsync(string toolName, FunctionCall fc) => toolName switch
    {
        "manage_inventory_levels" => await _schedulingService.ManageInventoryLevelsAsync(),
        "update_purchase_order_status" => await _schedulingService.UpdatePurchaseOrderByDescriptionAsync(
            fc.Args.Fields.TryGetValue("description", out var poDesc) ? poDesc.StringValue : "",
            fc.Args.Fields.TryGetValue("new_status", out var poSt) ? poSt.StringValue : "Delayed",
            fc.Args.Fields.TryGetValue("delay_days", out var poDel) ? (int)poDel.NumberValue : 0),
        "check_shipment_tracking" => new { Status = await _schedulingService.CheckShipmentTrackingAsync(
            fc.Args.Fields.TryGetValue("tracking_number_or_description", out var track) ? track.StringValue : "") },
        "check_weather_forecast" => new { Forecast = await _schedulingService.CheckWeatherForecastAsync(
            fc.Args.Fields.TryGetValue("location", out var loc) ? loc.StringValue : "") },
        "email_supplier" => new { Status = await _schedulingService.SendSupplierEmailAsync(
            fc.Args.Fields.TryGetValue("supplier_name", out var sname) ? sname.StringValue : "",
            fc.Args.Fields.TryGetValue("part_number", out var pnum) ? pnum.StringValue : "",
            fc.Args.Fields.TryGetValue("quantity", out var qty) ? (int)qty.NumberValue : 0,
            fc.Args.Fields.TryGetValue("urgency", out var urg) ? urg.StringValue : "Normal",
            fc.Args.Fields.TryGetValue("message_body", out var body) ? body.StringValue : "") },
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
