using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;

namespace Dental_Clinic.Services
{
    public class CloudinaryService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public CloudinaryService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<string> UploadImageAsync(IBrowserFile file)
        {
            var cloudName = _configuration["Cloudinary:CloudName"];
            var uploadPreset = _configuration["Cloudinary:UploadPreset"];

            if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(uploadPreset))
            {
                throw new Exception("Cloudinary configuration is missing. Please add CloudName and UploadPreset to appsettings.json.");
            }

            System.Diagnostics.Debug.WriteLine($"[Cloudinary] Uploading to {cloudName} with preset {uploadPreset}");

            // Use a fresh HttpClient to avoid conflicts
            using var client = new HttpClient();
            var content = new MultipartFormDataContent();

            // 1. Add upload_preset FIRST
            content.Add(new StringContent(uploadPreset), "upload_preset");

            // 2. Add file
            using var stream = file.OpenReadStream(10 * 1024 * 1024); // 10MB limit
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            // Use ByteArrayContent for the file
            var fileContent = new ByteArrayContent(ms.ToArray());
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, "file", file.Name);

            // Pass upload_preset in URL as well to ensure it's picked up
            var response = await client.PostAsync($"https://api.cloudinary.com/v1_1/{cloudName}/image/upload?upload_preset={uploadPreset}", content);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (json.TryGetProperty("secure_url", out var url))
                {
                    return url.GetString() ?? "";
                }
            }

            var error = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"[Cloudinary] Error: {error}");
            throw new Exception($"Image upload failed: {error}");
        }
    }
}
