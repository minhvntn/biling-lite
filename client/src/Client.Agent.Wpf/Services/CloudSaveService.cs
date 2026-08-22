using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Client.Agent.Wpf.Services
{
    public class CloudSaveService
    {
        private readonly HttpClient _httpClient;
        private readonly App _app;

        public CloudSaveService(App app)
        {
            _app = app;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", "SmbSecureLANKey2026");
        }

        private string GetSaveGameDirectory()
        {
            var myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(myDocuments, "My Games");
        }

        public async Task BackupSaveGameAsync(string memberId)
        {
            try
            {
                var saveDir = GetSaveGameDirectory();
                if (!Directory.Exists(saveDir))
                {
                    return; // No saves to backup
                }

                var tempZipFile = Path.GetTempFileName() + ".zip";
                
                // Create zip file
                if (File.Exists(tempZipFile)) File.Delete(tempZipFile);
                ZipFile.CreateFromDirectory(saveDir, tempZipFile, CompressionLevel.Optimal, false);

                // Upload to server
                var apiUrl = _app.BuildApiUrl($"/members/{memberId}/cloud-save");
                using var form = new MultipartFormDataContent();
                using var fileStream = new FileStream(tempZipFile, FileMode.Open, FileAccess.Read);
                using var streamContent = new StreamContent(fileStream);
                
                streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
                form.Add(streamContent, "file", $"{memberId}.zip");

                var response = await _httpClient.PostAsync(apiUrl, form);
                response.EnsureSuccessStatusCode();

                // Cleanup
                File.Delete(tempZipFile);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cloud Save Backup Failed: {ex.Message}");
            }
        }

        public async Task RestoreSaveGameAsync(string memberId)
        {
            try
            {
                var apiUrl = _app.BuildApiUrl($"/members/{memberId}/cloud-save");
                var response = await _httpClient.GetAsync(apiUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    return; // Probably no save exists
                }

                var tempZipFile = Path.GetTempFileName() + ".zip";
                using (var fs = new FileStream(tempZipFile, FileMode.Create, FileAccess.Write))
                {
                    await response.Content.CopyToAsync(fs);
                }

                var saveDir = GetSaveGameDirectory();
                if (!Directory.Exists(saveDir))
                {
                    Directory.CreateDirectory(saveDir);
                }

                ZipFile.ExtractToDirectory(tempZipFile, saveDir, overwriteFiles: true);

                File.Delete(tempZipFile);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cloud Save Restore Failed: {ex.Message}");
            }
        }
    }
}
