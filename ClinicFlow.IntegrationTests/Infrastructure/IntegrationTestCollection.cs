namespace ClinicFlow.IntegrationTests.Infrastructure
{
    [CollectionDefinition(nameof(IntegrationTestCollection))]
    public class IntegrationTestCollection : ICollectionFixture<ClinicFlowApiFactory>
    {
    }
}
