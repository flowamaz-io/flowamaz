namespace Flowamaz.Core.Enums;

/// <summary>Kind of Process-Intelligence insight (FUNCTIONAL.md §11.2). Stored as a string column.</summary>
public enum InsightType
{
    SlaRisk,
    Bottleneck,
    AnomalyDetected,
    CompletionForecast,
    PatternChange,
}
