namespace AzertyCommander;

internal sealed record FileCompareResult(
    bool AreEqual,
    long LeftLength,
    long RightLength,
    long? FirstDifferenceOffset);
