using UrlShortener.Agents.Runtime;
using Xunit;

namespace UrlShortener.Tests;

public sealed class AgentPromptCatalogTests
{
    private readonly IAgentPromptCatalog _catalog = new EmbeddedAgentPromptCatalog();

    [Theory]
    [InlineData("requirement-analyst.md")]
    [InlineData("product-purpose.md")]
    [InlineData("persona-researcher.md")]
    [InlineData("intent-analyst.md")]
    [InlineData("task-planner.md")]
    [InlineData("greenfield-architect.md")]
    [InlineData("brownfield-architect.md")]
    [InlineData("test-strategist.md")]
    [InlineData("ux-api-designer.md")]
    [InlineData("security-risk-reviewer.md")]
    [InlineData("implementation-proposer.md")]
    [InlineData("test-reviewer.md")]
    [InlineData("documentation-writer.md")]
    [InlineData("release-reviewer.md")]
    [InlineData("devops-engineer.md")]
    [InlineData("clarification-agent.md")]
    public void GetPrompt_LoadsEmbeddedRoleInstructions(string fileName)
    {
        var prompt = _catalog.GetPrompt(fileName);

        Assert.False(string.IsNullOrWhiteSpace(prompt));
        Assert.Contains("## Mission", prompt);
        Assert.Contains("## Rules", prompt);
    }

    [Fact]
    public void GetPrompt_UnknownRole_ThrowsExplicitError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => _catalog.GetPrompt("unknown-role.md"));

        Assert.Contains("unknown-role.md", exception.Message);
    }
}
