namespace Beeching.Commands
{
    public interface IAzureAxe
    {
        Task AxeResourceGroup(bool includeDebugOutput, Guid subscriptionId, string name);
    }
}