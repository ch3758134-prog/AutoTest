namespace AutoTest.Application.Production;

// RECORD owns all persistence and aggregation. No second historical count/store here.
public sealed record ProductionSummary(int RunCount, int DistinctDutCount,
    int PassedCount, int FailedCount, int UncertainCount, bool FromDurableRecords);
public sealed record ProductionSummaryFilter(DateTimeOffset? FromUtc, DateTimeOffset? ToUtc,
    string? RecipeId, bool ProductionOnly);
public interface IProductionSummaryReader
{
    ValueTask<ProductionSummary> QuerySummaryAsync(ProductionSummaryFilter filter, CancellationToken ct);
}
public sealed class ProductionStatisticsProjection
{
    private readonly IProductionSummaryReader _reader;
    public ProductionStatisticsProjection(IProductionSummaryReader reader) => _reader = reader;
    public ProductionSummaryFilter Filter(DateTimeOffset? from, DateTimeOffset? to,
        string? recipe = null, bool productionOnly = true)
    {
        if (from > to) throw new ArgumentException("Invalid statistics date range.");
        return new(from, to, recipe, productionOnly);
    }
    public async ValueTask<ProductionSummary> Refresh(ProductionSummaryFilter filter, CancellationToken ct)
    {
        var summary = await _reader.QuerySummaryAsync(filter, ct).ConfigureAwait(false);
        if (!summary.FromDurableRecords || summary.RunCount < summary.DistinctDutCount ||
            summary.RunCount < 0 || summary.DistinctDutCount < 0)
            throw new InvalidOperationException("UNTRUSTED_PRODUCTION_STATISTICS");
        return summary;
    }
}
