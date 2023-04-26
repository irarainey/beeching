namespace Beeching.Commands.Interfaces
{
    internal interface IAxe
    {
        Task<int> AxeResources(AxeSettings settings);
    }
}
