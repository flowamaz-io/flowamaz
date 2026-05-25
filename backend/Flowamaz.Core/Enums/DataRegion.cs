namespace Flowamaz.Core.Enums;

/// <summary>
/// Data residency region for an organisation (FUNCTIONAL.md §12.7). Immutable after org
/// creation. Default is <see cref="ApSoutheast1"/> (Malaysia — our home market).
/// </summary>
public enum DataRegion
{
    ApSoutheast1,
    EuWest1,
    UsEast1,
}
