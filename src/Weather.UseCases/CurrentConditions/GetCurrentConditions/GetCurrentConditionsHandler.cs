namespace Weather.UseCases.CurrentConditions.GetCurrentConditions;

public sealed class GetCurrentConditionsHandler(ICurrentConditionsProvider conditionsProvider)
{
    public Task<CurrentConditionsResult> HandleAsync(GetCurrentConditionsQuery query, CancellationToken cancellationToken = default) =>
        conditionsProvider.GetCurrentConditionsAsync(cancellationToken);
}
