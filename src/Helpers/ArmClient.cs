using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Beeching.Models;

namespace Beeching.Helpers
{
    internal class ArmClient
    {
        private readonly HttpClient _client;
        private string _accessToken = string.Empty;

        public ArmClient(IHttpClientFactory httpClientFactory)
        {
            _client = httpClientFactory.CreateClient("ArmApi");
        }

        public async Task InitializeAsync(bool debug)
        {
            _accessToken = await AuthHelper.GetAccessToken(debug);
        }

        public async Task<HttpResponseMessage> GetAsync(string uri)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return await _client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> DeleteAsync(string uri)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return await _client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> PutAsync(string uri, HttpContent content)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return await _client.SendAsync(request);
        }

        public async Task<T?> GetAsAsync<T>(string uri)
        {
            var response = await GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                return default;
            }

            string json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(json);
        }

        public async Task<List<T>> GetListAsync<T>(string uri)
        {
            var result = await GetAsAsync<ArmListResponse<T>>(uri);
            return result?.Value ?? [];
        }

        public async Task PutJsonAsync<T>(string uri, T body)
        {
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            await PutAsync(uri, content);
        }
    }
}
