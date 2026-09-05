using System.Collections.Concurrent;
using VotschVc3.Core.Communication;

namespace VotschVc3.App.ViewModels;

internal static class TemperatureSafetyRegistry
{
    private static readonly ConcurrentDictionary<Guid, TemperatureSafetyPolicy> Policies = new();

    public static TemperatureSafetyPolicy Get(Guid chamberId, double minimumC, double maximumC)
    {
        TemperatureSafetyPolicy policy = Policies.GetOrAdd(chamberId, _ => new(minimumC, maximumC));
        return policy;
    }
}
