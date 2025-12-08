using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Dental_Clinic.Services
{
    public class PayMongoService
    {
        private readonly HttpClient _httpClient;
        private readonly string _secretKey;

        public PayMongoService(IConfiguration configuration)
        {
            _secretKey = configuration["PayMongo:SecretKey"] ?? throw new InvalidOperationException("PayMongo Secret Key is missing in configuration.");

            // Ensure TLS 1.2 is used (required by many payment gateways)
#pragma warning disable SYSLIB0014
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
#pragma warning restore SYSLIB0014

            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://api.paymongo.com/v1/");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(_secretKey + ":")));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<(string Id, string Url)> CreatePaymentLinkAsync(decimal amount, string description)
        {
            try
            {
                var requestData = new
                {
                    data = new
                    {
                        attributes = new
                        {
                            amount = (int)(amount * 100), // Amount in centavos
                            description = description,
                            remarks = "Dental Clinic Payment"
                        }
                    }
                };

                var json = JsonSerializer.Serialize(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("links", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseString);
                    var data = doc.RootElement.GetProperty("data");
                    var id = data.GetProperty("id").GetString();
                    var checkoutUrl = data.GetProperty("attributes").GetProperty("checkout_url").GetString();
                    return (id ?? string.Empty, checkoutUrl ?? string.Empty);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"PayMongo Error: {error}");
                    // Return the error message in the URL field to display it
                    return (string.Empty, $"ERROR: {error}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PayMongo Exception: {ex.Message}");
                return (string.Empty, $"EXCEPTION: {ex.Message}");
            }
        }

        public async Task<string> GetLinkStatusAsync(string linkId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"links/{linkId}");
                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseString);
                    // Status: unpaid, paid
                    var status = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("status").GetString();
                    return status ?? "unpaid";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PayMongo Check Status Error: {ex.Message}");
            }
            return "unpaid";
        }
    }
}
