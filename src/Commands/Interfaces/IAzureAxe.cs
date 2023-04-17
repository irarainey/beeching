namespace Beeching.Commands.Interfaces
{
    public interface IAzureAxe
    {
        Task<HttpResponseMessage> AxeResources(AxeSettings settings);
    }
}
