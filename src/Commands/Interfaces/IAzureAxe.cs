namespace Beeching.Commands.Interfaces
{
    internal interface IAzureAxe
    {
        Task<int> AxeResources(AxeSettings settings);
    }
}
