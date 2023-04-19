namespace Beeching.Commands.Interfaces
{
    internal interface IAzureAxe
    {
        Task<bool> AxeResources(AxeSettings settings);
    }
}
