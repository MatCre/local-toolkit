using Toolkit.AI.CallLog;

namespace Toolkit.AI.Tests;

/// <summary>
/// What every <see cref="IAiCallLog"/> must do. The budget cap depends on these numbers being right,
/// so any new storage adapter has to pass them. The in-memory fake the other tests use runs them
/// too, which proves the fake behaves like the real thing.
/// </summary>
public abstract class AiCallLogContractTests
{
    protected abstract IAiCallLog CreateLog();

    private static readonly DateTimeOffset MonthStart = new(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(13)); // midnight in NZ

    private static AiCallRecord Call(DateTimeOffset at, decimal? cost) =>
        new(at, "openrouter", "m", 1, 1, TimeSpan.FromSeconds(1), cost, cost is not null, null);

    [Fact]
    public async Task Sums_cost_from_a_given_time_onwards()
    {
        var log = CreateLog();

        await log.RecordAsync(Call(MonthStart.AddMinutes(-1), 4.00m)); // last month: not counted
        await log.RecordAsync(Call(MonthStart, 0.25m));                // exactly at the start: counted
        await log.RecordAsync(Call(MonthStart.AddDays(10), 0.50m));
        await log.RecordAsync(Call(MonthStart.AddDays(11), null));     // failed call, no cost

        Assert.Equal(0.75m, await log.GetCostSinceAsync(MonthStart));
    }

    [Fact]
    public async Task Compares_times_correctly_across_time_zones()
    {
        var log = CreateLog();

        // 11:30 UTC on 31 Oct is 00:30 on 1 Nov in New Zealand: inside the NZ month.
        await log.RecordAsync(Call(new DateTimeOffset(2026, 10, 31, 11, 30, 0, TimeSpan.Zero), 1.00m));

        Assert.Equal(1.00m, await log.GetCostSinceAsync(MonthStart));
    }

    [Fact]
    public async Task Reports_zero_when_nothing_was_spent()
    {
        Assert.Equal(0m, await CreateLog().GetCostSinceAsync(MonthStart));
    }
}

public sealed class InMemoryCallLogContractTests : AiCallLogContractTests
{
    protected override IAiCallLog CreateLog() => new InMemoryCallLog();
}
