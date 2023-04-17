namespace Beeching.Commands.Interfaces
{
    public interface IAzureAxe
    {
        Task<bool> AxeResources(AxeSettings settings);
    }
}
