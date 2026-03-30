namespace Shared.Infrastructure;

public static class ObservabilityHelper
{
    public static string BuildTraceId() => Guid.NewGuid().ToString("N");
}
