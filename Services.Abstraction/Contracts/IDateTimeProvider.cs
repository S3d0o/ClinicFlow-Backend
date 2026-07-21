namespace Services.Abstraction.Contracts
{
    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }

    }
}
