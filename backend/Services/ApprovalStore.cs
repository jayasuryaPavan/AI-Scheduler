using AiScheduler.Api.DTOs;
using System.Collections.Concurrent;

namespace AiScheduler.Api.Services;

/// <summary>
/// In-memory store for pending agent proposals that await human approval.
/// Registered as a Singleton so proposals survive across scoped HTTP requests.
/// </summary>
public interface IApprovalStore
{
    string Save(PendingProposal proposal);
    PendingProposal? Get(string proposalId);
    bool Remove(string proposalId);
}

public class ApprovalStore : IApprovalStore
{
    private readonly ConcurrentDictionary<string, PendingProposal> _proposals = new();

    public string Save(PendingProposal proposal)
    {
        var id = Guid.NewGuid().ToString("N")[..8]; // Short readable ID
        proposal.ProposalId = id;
        proposal.CreatedAt  = DateTimeOffset.UtcNow;
        _proposals[id]      = proposal;
        return id;
    }

    public PendingProposal? Get(string proposalId)
        => _proposals.TryGetValue(proposalId, out var p) ? p : null;

    public bool Remove(string proposalId)
        => _proposals.TryRemove(proposalId, out _);
}

/// <summary>
/// Represents a sandboxed agent run whose changes have NOT been committed.
/// Stores the original user prompt so we can deterministically replay the agent.
/// </summary>
public class PendingProposal
{
    public string              ProposalId      { get; set; } = string.Empty;
    public string              UserPrompt      { get; set; } = string.Empty;
    public List<string>        SimulatedImpact { get; set; } = new();
    public List<string>        ToolCalls       { get; set; } = new();
    public string              AgentReasoning  { get; set; } = string.Empty;
    public DateTimeOffset      CreatedAt       { get; set; }
}
