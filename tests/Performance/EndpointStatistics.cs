namespace UAI.ZeroVP.Tests.Performance;

/// <summary>Souhrn dob odezvy jednoho endpointu – medián, 95. a 99. percentil a maximum.</summary>
internal sealed record EndpointStatistics(string Endpoint, int Count, double P50Ms, double P95Ms, double P99Ms, double MaxMs)
{
    public static IReadOnlyList<EndpointStatistics> From(IEnumerable<RequestSample> samples) =>
        samples
            .GroupBy(sample => sample.Endpoint)
            .Select(group =>
            {
                var sorted = group.Select(sample => sample.Duration.TotalMilliseconds).Order().ToArray();
                return new EndpointStatistics(
                    group.Key,
                    sorted.Length,
                    Percentile(sorted, 0.50),
                    Percentile(sorted, 0.95),
                    Percentile(sorted, 0.99),
                    sorted[^1]);
            })
            .OrderBy(statistics => statistics.Endpoint)
            .ToList();

    private static double Percentile(double[] sorted, double percentile) =>
        sorted[(int)Math.Ceiling(percentile * sorted.Length) - 1];

    public override string ToString() =>
        $"{Endpoint,-34} n={Count,5}   p50={P50Ms,7:F2} ms   p95={P95Ms,7:F2} ms   p99={P99Ms,7:F2} ms   max={MaxMs,7:F2} ms";
}