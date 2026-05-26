namespace Flowamaz.Core.Models;

public sealed record AutoMapResult(
    IReadOnlyList<FieldMapping> Mappings,
    IReadOnlyList<string> UnmappedFields
);

public sealed record FieldMapping(
    string FieldName,
    string SuggestedExpression,
    double Confidence
);
