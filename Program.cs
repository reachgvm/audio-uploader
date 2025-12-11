using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

class Program
{
    // --- CONFIGURATION ---
    private const string API_KEY = "cw_GGIKmVnh44_yIDG1tyH6C50pYOtnkgWAEBfsIfeQMgc5i0nE";
    private const string API_URL = "https://api.comswise.in/v1";
    private const string FROM_NUMBER = "9342502751";
    // ---------------------

    private static readonly HttpClient _httpClient = new HttpClient();
    private static int _successCount = 0;
    private static int _processedCount = 0;
    private static int _totalFiles = 0;

    static async Task Main(string[] args)
    {
        string command = "";
        string argPath = "";

        if (args.Length > 0)
        {
            if (args[0].Equals("download", StringComparison.OrdinalIgnoreCase))
            {
                command = "download";
            }
            else if (args[0].Equals("process_audio_files", StringComparison.OrdinalIgnoreCase))
            {
                command = "process_audio_files";
                if (args.Length > 1) argPath = args[1];
            }
            else
            {
                // Fallback for backward compatibility or direct path usage
                // Checks if the first argument looks like a path
                if (Directory.Exists(args[0]) || args[0].Contains("/") || args[0].Contains("\\"))
                {
                     command = "process_audio_files";
                     argPath = args[0];
                }
            }
        }

        if (string.IsNullOrEmpty(command))
        {
            Console.WriteLine("Please select a mode:");
            Console.WriteLine("1. Enter 'download' to fetch transcripts.");
            Console.WriteLine("2. Enter 'process_audio_files' (or a directory path) to upload audio.");
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim() ?? string.Empty;
            
            if (input.Equals("download", StringComparison.OrdinalIgnoreCase))
            {
                command = "download";
            }
            else if (input.StartsWith("process_audio_files", StringComparison.OrdinalIgnoreCase))
            {
                 command = "process_audio_files";
                 var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                 if (parts.Length > 1) argPath = parts[1];
            }
            else
            {
                 // Assume path
                 command = "process_audio_files";
                 argPath = input;
            }
        }

        if (command == "download")
        {
            await DownloadTranscriptsSummary.FetchAndProcess(API_KEY, API_URL);
        }
        else if (command == "process_audio_files")
        {
            if (string.IsNullOrWhiteSpace(argPath))
            {
                Console.WriteLine("Please provide the directory path containing WAV files:");
                argPath = Console.ReadLine()?.Trim() ?? string.Empty;
            }
            
            await ProcessDirectory(argPath);
        }
        else
        {
             Console.WriteLine("Invalid command or input.");
        }
    }

    private static async Task ProcessDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            Console.WriteLine($"Error: Directory not found: {directoryPath}");
            return;
        }

        var files = Directory.GetFiles(directoryPath, "*.wav", SearchOption.TopDirectoryOnly);
        _totalFiles = files.Length;
        Console.WriteLine($"Found {_totalFiles} WAV files in {directoryPath}");
        Console.WriteLine("Starting upload with 5 threads...");

        var options = new ParallelOptions { MaxDegreeOfParallelism = 5 };
        var stopwatch = Stopwatch.StartNew();

        await Parallel.ForEachAsync(files, options, async (filePath, ct) =>
        {
            await ProcessFileAsync(filePath);
            var finished = Interlocked.Increment(ref _processedCount);
            if (finished % 10 == 0)
            {
                Console.WriteLine($"Progress: {finished}/{_totalFiles} files processed.");
            }
        });

        stopwatch.Stop();
        Console.WriteLine($"\nInbound processing complete.");
        Console.WriteLine($"Total Successful Uploads+Creation: {_successCount}/{_totalFiles}");
        Console.WriteLine($"Time elapsed: {stopwatch.Elapsed}");
    }

    private static async Task ProcessFileAsync(string filePath)
    {
        string filename = Path.GetFileName(filePath);
        string filenameNoExt = Path.GetFileNameWithoutExtension(filePath);

        try
        {
            // 1. Parse Metadata
            // Format: 1034-119-17-2075-2029-7678334829-o-0-251125-180353.wav
            var parts = filenameNoExt.Split('-');
            if (parts.Length < 10)
            {
                Console.WriteLine($"[Skip] Invalid filename format: {filename}");
                return;
            }

            string toNumber = parts[5];
            string datePart = parts[8]; // 251125 (ddMMyy)
            string timePart = parts[9]; // 180353 (HHmmss)

            // Parse DateTime
            if (!DateTime.TryParseExact($"{datePart}-{timePart}", "ddMMyy-HHmmss", null, System.Globalization.DateTimeStyles.None, out DateTime startTimeDt))
            {
                Console.WriteLine($"[Skip] Could not parse date/time from {filename}");
                return;
            }



            // Calculate Duration
            double durationSeconds = GetWavDuration(filePath);
            if (durationSeconds <= 0)
            {
                Console.WriteLine($"[Skip] Could not calculate duration or empty file: {filename}");
                return;
            }
            
            // Calculate ISO Times
            var startUtc = DateTime.SpecifyKind(startTimeDt, DateTimeKind.Utc);
            var endUtc = startUtc.AddSeconds(durationSeconds);

            string startTimeIso = startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string endTimeIso = endUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // 2. Request Upload URL
            string uploadUrl, fileKey;
            try
            {
                var requestBody = new { fileName = filename, contentType = "audio/wav" };
                var requestContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                
                using var requestMsg = new HttpRequestMessage(HttpMethod.Post, $"{API_URL}/upload/request");
                requestMsg.Headers.Add("Authorization", $"Bearer {API_KEY}");
                requestMsg.Content = requestContent;

                using var resp = await _httpClient.SendAsync(requestMsg);
                if (!resp.IsSuccessStatusCode)
                {
                    string errorBody = await resp.Content.ReadAsStringAsync();
                    string errorDetail = GetErrorDetail(errorBody);
                    Console.WriteLine($"[Error] Upload request failed for {filename}: {resp.StatusCode} - {errorDetail}");
                    return;
                }
                
                var respData = await resp.Content.ReadFromJsonAsync<UploadResponse>();
                if (respData == null) throw new Exception("Empty upload response");
                
                uploadUrl = respData.uploadUrl;
                fileKey = respData.fileKey;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Upload request failed for {filename}: {ex.Message}");
                return;
            }

            // 3. Upload File to R2
            try
            {
                // Read all bytes (be mindful of memory, but for typical wavs it's okay. For huge files, stream.)
                // Python code used f.read(), so we doReadAllBytes.
                byte[] fileContent = await File.ReadAllBytesAsync(filePath);
                
                using var uploadContent = new ByteArrayContent(fileContent);
                uploadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");

                var r2Stopwatch = Stopwatch.StartNew();
                using var uploadResp = await _httpClient.PutAsync(uploadUrl, uploadContent);
                r2Stopwatch.Stop();
                double sizeMb = fileContent.Length / (1024.0 * 1024.0);
                Console.WriteLine($"[Time] R2 Upload for {filename} ({sizeMb:F2} MB) took {r2Stopwatch.ElapsedMilliseconds} ms. Speed: {sizeMb / r2Stopwatch.Elapsed.TotalSeconds:F2} MB/s");
                if (!uploadResp.IsSuccessStatusCode)
                {
                    string err = await uploadResp.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Error] R2 Upload failed for {filename}: {uploadResp.StatusCode} {err}");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] R2 Upload exception for {filename}: {ex.Message}");
                return;
            }

            // 4. Create Call Record
            try
            {
                var callPayload = new
                {
                    callId = Guid.NewGuid().ToString(),
                    direction = "inbound",
                    @from = FROM_NUMBER, 
                    to = toNumber,
                    voipNumber = "8005998888", 
                    startTime = startTimeIso,
                    endTime = endTimeIso,
                    status = "completed",
                    recordingUrl = fileKey
                };

                using var createCallMsg = new HttpRequestMessage(HttpMethod.Post, $"{API_URL}/calls/create");
                createCallMsg.Headers.Add("Authorization", $"Bearer {API_KEY}");
                createCallMsg.Content = JsonContent.Create(callPayload);

                var apiStopwatch = Stopwatch.StartNew();
                using var callResp = await _httpClient.SendAsync(createCallMsg);
                apiStopwatch.Stop();
                Console.WriteLine($"[Time] Create call API for {filename} took {apiStopwatch.ElapsedMilliseconds} ms");
                if (!callResp.IsSuccessStatusCode)
                {
                    string errorBody = await callResp.Content.ReadAsStringAsync();
                    string errorDetail = GetErrorDetail(errorBody);
                    Console.WriteLine($"[Error] Create call failed for {filename}: {callResp.StatusCode} - {errorDetail}");
                    return;
                }

                // Success
                Interlocked.Increment(ref _successCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Create call failed for {filename}: {ex.Message}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] Unhandled exception processing {filename}: {e.Message}");
        }
    }

    private static string GetErrorDetail(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.ToString();
            }
        }
        catch { }
        return json;
    }

    private static double GetWavDuration(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            if (fs.Length < 44) return 0;

            byte[] header = new byte[44];
            fs.Read(header, 0, 44);

            // ByteRate at offest 28 (4 bytes)
            int byteRate = BitConverter.ToInt32(header, 28);
            
            // Subchunk2Size at offset 40 (4 bytes) - Note: This presumes standard canonical header logic 
            // where the data chunk immediately follows the fmt chunk. 
            // Robust parsing would sweep for 'data' chunk but let's try standard offset first.
            int dataSize = BitConverter.ToInt32(header, 40);

            // Verify 'data' marker?
            // Bytes 36-40 should be 'data'
            if (header[36] != 'd' || header[37] != 'a' || header[38] != 't' || header[39] != 'a')
            {
                // Fallback: This is not a standard strict 44-byte-header-only WAV (might have extra chunks). 
                // Since per-file scanning is expensive, let's keep it simple: 
                // Duration = (FileSize - 44) / ByteRate is a decent approximation if data chunk is huge.
                // Or better: Use file size - 44 (header).
                // Precise duration = DataSize / ByteRate.
                // If we can't find data chunk at 40, let's look for it? 
                // For this implementation, I will assume roughly standard parsing.
                // If 'data' is missing at 36, I'll use file length approach as fallback.
                dataSize = (int)(fs.Length - 44);
            }

            if (byteRate <= 0) return 0;

            return (double)dataSize / byteRate;
        }
        catch
        {
            return 0;
        }
    }
}

public class UploadResponse
{
    [JsonPropertyName("uploadUrl")]
    public string uploadUrl { get; set; } = "";
    
    [JsonPropertyName("fileKey")]
    public string fileKey { get; set; } = "";
}
