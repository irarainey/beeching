namespace Beeching.Commands.Interfaces
{
    public interface IAzureAxe
    {
        Task<HttpResponseMessage> AxeResource(AxeSettings settings);
    }
}
