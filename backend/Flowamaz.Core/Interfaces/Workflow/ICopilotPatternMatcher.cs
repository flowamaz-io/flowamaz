using Flowamaz.Core.Models;

namespace Flowamaz.Core.Interfaces.Workflow;

public interface ICopilotPatternMatcher
{
    PatternMatchResult? TryMatch(string command, string? yamlContent = null);
}
