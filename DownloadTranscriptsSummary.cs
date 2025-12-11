using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

public class DownloadTranscriptsSummary
{
    private const string OUTPUT_DIR = "transcripts_download";
    private const int PAGE_SIZE = 5;

    public static async Task FetchAndProcess(string apiKey, string baseUrl)
    {
        Directory.CreateDirectory(OUTPUT_DIR);
        
        string cursor = null;
        int pageNum = 1;
        bool hasMore = true;

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        while (hasMore)
        {
            Console.WriteLine($"\n--- Fetching Page {pageNum} (Cursor: {cursor ?? "None"}) ---");

            var queryParams = new List<string> { $"limit={PAGE_SIZE}" };
            if (!string.IsNullOrEmpty(cursor))
            {
                queryParams.Add($"cursor={cursor}");
            }
            string queryString = string.Join("&", queryParams);
            string url = $"{baseUrl}/calls/?{queryString}";

            try
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var data = await response.Content.ReadFromJsonAsync<CallsResponse>();
                
                if (data == null)
                {
                    Console.WriteLine("Failed to parse response.");
                    break;
                }

                var calls = data.Calls ?? new List<Call>();
                cursor = data.Cursor;
                hasMore = data.HasMore;

                if (calls.Count == 0)
                {
                    Console.WriteLine("No calls found on this page.");
                }

                foreach (var call in calls)
                {
                    string summary = call.ExecutiveSummary ?? "No summary available";
                    if (summary.Length > 100) summary = summary.Substring(0, 100) + "...";
                    
                    Console.WriteLine($"\n[Call {call.CallId}]");
                    Console.WriteLine($"Summary: {summary}");

                    if (call.Transcript != null && !string.IsNullOrEmpty(call.Transcript.TranscriptUrl))
                    {
                        try
                        {
                            // Use a separate client without default headers (specifically Authorization)
                            // because sending the Bearer token to S3/R2 presigned URLs causes 400 errors.
                            using var downloadClient = new HttpClient();
                            var tResponse = await downloadClient.GetAsync(call.Transcript.TranscriptUrl);
                            
                            if (!tResponse.IsSuccessStatusCode)
                            {
                                var errorContent = await tResponse.Content.ReadAsStringAsync();
                                Console.WriteLine($"Failed to download transcript. Status: {tResponse.StatusCode}, Response: {errorContent}");
                            }
                            else
                            {
                                string content = await tResponse.Content.ReadAsStringAsync();
                                string filename = Path.Combine(OUTPUT_DIR, $"{call.CallId}.md");
                                await File.WriteAllTextAsync(filename, content);
                                Console.WriteLine($"Saved transcript to {filename}");
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Failed to download transcript: {e.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("No transcript URL available.");
                    }
                }

                pageNum++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                break;
            }
        }
    }
}

public class CallsResponse
{
    [JsonPropertyName("calls")]
    public List<Call> Calls { get; set; }

    [JsonPropertyName("cursor")]
    public string Cursor { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }
}

public class Call
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("callId")]
    public string CallId { get; set; }

    [JsonPropertyName("executiveSummary")]
    public string ExecutiveSummary { get; set; }

    [JsonPropertyName("transcript")]
    public TranscriptInfo Transcript { get; set; }
}

public class TranscriptInfo
{
    [JsonPropertyName("transcriptUrl")]
    public string TranscriptUrl { get; set; }
}
