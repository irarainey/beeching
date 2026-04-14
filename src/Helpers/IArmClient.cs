using Beeching.Models;

namespace Beeching.Helpers
{
    internal interface IArmClient
    {
        Task InitializeAsync(bool debug);
        Task<HttpResponseMessage> GetAsync(string uri);
        Task<HttpResponseMessage> DeleteAsync(string uri);
        Task<HttpResponseMessage> PutAsync(string uri, HttpContent content);
        Task<T?> GetAsAsync<T>(string uri);
        Task<List<T>> GetListAsync<T>(string uri);
        Task PutJsonAsync<T>(string uri, T body);
    }
}
