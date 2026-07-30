namespace AzertyCommander;

internal sealed record OperationProgress(int Current, int Total, string Message, long BytesDone = 0, long BytesTotal = 0);
