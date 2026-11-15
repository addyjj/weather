namespace Weather.UseCases.CurrentConditions.GetCurrentConditions;

public interface ICurrentConditionsProvider
{
    Task<CurrentConditionsResult> GetCurrentConditionsAsync(CancellationToken cancellationToken = default);
}
