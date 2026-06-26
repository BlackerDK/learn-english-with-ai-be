using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using backend.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace backend.Services
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GeminiService(HttpClient httpClient, IConfiguration configuration, AppDbContext dbContext, IHttpContextAccessor httpContextAccessor)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }

        private async Task<string> GetApiKeyAsync()
        {
            var userIdStr = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdStr, out var userId))
            {
                throw new InvalidOperationException("User is not authenticated. Cannot fetch API Key.");
            }

            // 1. Try reading from Database
            var dbSetting = await _dbContext.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "GeminiApiKey" && s.UserId == userId);
            if (dbSetting != null && !string.IsNullOrWhiteSpace(dbSetting.Value))
            {
                return dbSetting.Value;
            }

            // 2. Try reading from Environment Variable
            var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                return envKey;
            }

            // 3. Try reading from Configuration (appsettings.json)
            var configKey = _configuration["GeminiApiKey"];
            if (!string.IsNullOrWhiteSpace(configKey))
            {
                return configKey;
            }

            throw new InvalidOperationException("Gemini API Key is not configured. Please set it in Settings, environment variables, or appsettings.json.");
        }

        // GROQ API FALLBACK BACKEND
        private async Task<string?> GetGroqApiKeyAsync()
        {
            var userIdStr = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdStr, out var userId))
            {
                var dbSetting = await _dbContext.SystemSettings
                    .FirstOrDefaultAsync(s => s.Key == "GroqApiKey" && s.UserId == userId);
                if (dbSetting != null && !string.IsNullOrWhiteSpace(dbSetting.Value))
                {
                    return dbSetting.Value;
                }
            }

            var envKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                return envKey;
            }

            var configKey = _configuration["GroqApiKey"];
            if (!string.IsNullOrWhiteSpace(configKey))
            {
                return configKey;
            }

            return null;
        }

        public async Task<string> GenerateTextViaGroqAsync(string prompt, string? systemInstruction = null, bool requestJson = false)
        {
            var apiKey = await GetGroqApiKeyAsync();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Groq API Key is not configured for fallback.");
            }

            var url = "https://api.groq.com/openai/v1/chat/completions";
            
            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemInstruction))
            {
                messages.Add(new { role = "system", content = systemInstruction });
            }
            messages.Add(new { role = "user", content = prompt });

            var requestBody = new
            {
                model = "llama-3.3-70b-versatile",
                messages = messages,
                response_format = requestJson ? new { type = "json_object" } : null
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(requestBody);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq API request failed: {response.StatusCode} - {errorMsg}");
            }

            var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var text = jsonDoc?.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return text ?? "No response received from Groq.";
        }

        // --- PUBLIC ENDPOINTS WITH FALLBACK ---

        public async Task<string> GenerateTextAsync(string prompt, string? systemInstruction = null)
        {
            try
            {
                return await GenerateTextViaGeminiAsync(prompt, systemInstruction);
            }
            catch (Exception geminiEx)
            {
                try
                {
                    var groqKey = await GetGroqApiKeyAsync();
                    if (!string.IsNullOrEmpty(groqKey))
                    {
                        return await GenerateTextViaGroqAsync(prompt, systemInstruction, false);
                    }
                }
                catch (Exception groqEx)
                {
                    throw new AggregateException("Both Gemini and fallback Groq failed.", geminiEx, groqEx);
                }
                throw;
            }
        }

        public async Task<string> GenerateQuizJsonAsync(string topic, int count, string vocabularyContext = "")
        {
            var systemInstruction = 
                "You are an expert language teacher. Generate a list of language learning quiz questions in JSON format. " +
                "Do NOT wrap the output in markdown code blocks. The output must be raw JSON conforming to this schema:\n" +
                "[\n" +
                "  {\n" +
                "    \"question\": \"The question prompt here...\",\n" +
                "    \"type\": \"multiple-choice\" or \"fill-in-the-blank\" or \"translation\",\n" +
                "    \"options\": [\"Option A\", \"Option B\", \"Option C\", \"Option D\"], // Only for multiple-choice, otherwise empty array\n" +
                "    \"correctAnswer\": \"The exact correct answer (or one of the valid options)\",\n" +
                "    \"explanation\": \"Short explanation of why this answer is correct and grammar tips in Vietnamese.\"\n" +
                "  }\n" +
                "]\n" +
                "Ensure the options contain the correctAnswer. For fill-in-the-blank, use '___' in the question text. " +
                "Explain the answer clearly in Vietnamese.";

            var prompt = $"Generate {count} quiz questions about the topic/context: '{topic}'.";
            if (!string.IsNullOrEmpty(vocabularyContext))
            {
                prompt += $" Focus on using these vocabulary words:\n{vocabularyContext}";
            }

            try
            {
                return await GenerateQuizJsonViaGeminiAsync(prompt, systemInstruction);
            }
            catch (Exception geminiEx)
            {
                try
                {
                    var groqKey = await GetGroqApiKeyAsync();
                    if (!string.IsNullOrEmpty(groqKey))
                    {
                        return await GenerateTextViaGroqAsync(prompt, systemInstruction, true);
                    }
                }
                catch (Exception groqEx)
                {
                    throw new AggregateException("Both Gemini and fallback Groq failed.", geminiEx, groqEx);
                }
                throw;
            }
        }

        public async Task<string> EvaluatePronunciationJsonAsync(string targetSentence, string userTranscription)
        {
            var systemInstruction = 
                "You are an expert pronunciation coach. Evaluate the user's spoken sentence against the target correct sentence. " +
                "Compare the two texts, calculate an accuracy score (0 to 100), identify mispronounced or missing words, and give constructive feedback in Vietnamese. " +
                "Do NOT wrap the output in markdown code blocks. The output must be raw JSON conforming to this schema:\n" +
                "{\n" +
                "  \"score\": 85, // integer score from 0 to 100\n" +
                "  \"accuracy\": \"excellent\" or \"good\" or \"needs-improvement\",\n" +
                "  \"feedback\": \"Detailed feedback in Vietnamese on pronunciation, rhythm, and tips to improve.\",\n" +
                "  \"wordAnalysis\": [ // Analyze each word in the target sentence\n" +
                "    {\n" +
                "      \"word\": \"target_word\",\n" +
                "      \"status\": \"correct\" or \"mispronounced\" or \"missing\"\n" +
                "    }\n" +
                "  ]\n" +
                "}";

            var prompt = $"Target Sentence: \"{targetSentence}\"\nUser Spoke: \"{userTranscription}\"";

            try
            {
                return await EvaluatePronunciationJsonViaGeminiAsync(prompt, systemInstruction);
            }
            catch (Exception geminiEx)
            {
                try
                {
                    var groqKey = await GetGroqApiKeyAsync();
                    if (!string.IsNullOrEmpty(groqKey))
                    {
                        return await GenerateTextViaGroqAsync(prompt, systemInstruction, true);
                    }
                }
                catch (Exception groqEx)
                {
                    throw new AggregateException("Both Gemini and fallback Groq failed.", geminiEx, groqEx);
                }
                throw;
            }
        }

        public async Task<string> ExtractVocabularyJsonAsync(string documentText)
        {
            var systemInstruction = 
                "You are an expert linguist and language teacher. Read the provided text/document and extract important, difficult, or key vocabulary words. " +
                "Do NOT wrap the output in markdown code blocks. The output must be raw JSON conforming to this schema:\n" +
                "[\n" +
                "  {\n" +
                "    \"word\": \"the vocabulary word\",\n" +
                "    \"pronunciation\": \"IPA pronunciation\",\n" +
                "    \"meaning\": \"meaning in Vietnamese\",\n" +
                "    \"example\": \"an example sentence using the word (preferably from the text if possible)\",\n" +
                "    \"exampleTranslation\": \"Vietnamese translation of the example sentence\",\n" +
                "    \"tags\": \"Document,AI Generated\"\n" +
                "  }\n" +
                "]";

            var prompt = $"Extract vocabulary from this text:\n\n{documentText}";

            try
            {
                return await ExtractVocabularyJsonViaGeminiAsync(prompt, systemInstruction);
            }
            catch (Exception geminiEx)
            {
                try
                {
                    var groqKey = await GetGroqApiKeyAsync();
                    if (!string.IsNullOrEmpty(groqKey))
                    {
                        return await GenerateTextViaGroqAsync(prompt, systemInstruction, true);
                    }
                }
                catch (Exception groqEx)
                {
                    throw new AggregateException("Both Gemini and fallback Groq failed.", geminiEx, groqEx);
                }
                throw;
            }
        }

        public async Task<string> EvaluateWritingJsonAsync(string topicTitle, string submittedText, string targetLevel)
        {
            var systemInstruction = 
                "You are an expert language teacher and writing evaluator. Evaluate the user's essay based on the topic and target level. " +
                "Calculate a score (0 to 100), identify grammar mistakes with corrections, suggest better vocabulary, and give general feedback in Vietnamese. " +
                "Do NOT wrap the output in markdown code blocks. The output must be raw JSON conforming to this schema:\n" +
                "{\n" +
                "  \"score\": 85,\n" +
                "  \"generalFeedback\": \"Detailed feedback in Vietnamese about structure, coherence, and task achievement.\",\n" +
                "  \"grammarMistakes\": [\n" +
                "    {\n" +
                "      \"mistake\": \"The exact wrong text from the essay\",\n" +
                "      \"correction\": \"How to fix it\",\n" +
                "      \"explanation\": \"Why it is wrong in Vietnamese\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"vocabularySuggestions\": [\n" +
                "    {\n" +
                "      \"original\": \"word used by user\",\n" +
                "      \"suggestion\": \"better alternative\",\n" +
                "      \"reason\": \"Why this is better in Vietnamese\"\n" +
                "    }\n" +
                "  ]\n" +
                "}";

            var prompt = $"Topic: {topicTitle}\nTarget Level: {targetLevel}\n\nUser's Essay:\n{submittedText}";

            try
            {
                var apiKey = await GetApiKeyAsync();
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

                var requestBody = new GeminiRequest
                {
                    Contents = new[]
                    {
                        new GeminiContent { Parts = new[] { new GeminiPart { Text = prompt } } }
                    },
                    SystemInstruction = new GeminiSystemInstruction
                    {
                        Parts = new[] { new GeminiPart { Text = systemInstruction } }
                    },
                    GenerationConfig = new GeminiGenerationConfig
                    {
                        ResponseMimeType = "application/json"
                    }
                };

                var response = await _httpClient.PostAsJsonAsync(url, requestBody);
                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Gemini API Writing evaluation failed: {errorMsg}");
                }

                var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
                return geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "{}";
            }
            catch (Exception geminiEx)
            {
                try
                {
                    var groqKey = await GetGroqApiKeyAsync();
                    if (!string.IsNullOrEmpty(groqKey))
                    {
                        return await GenerateTextViaGroqAsync(prompt, systemInstruction, true);
                    }
                }
                catch (Exception groqEx)
                {
                    throw new AggregateException("Both Gemini and fallback Groq failed.", geminiEx, groqEx);
                }
                throw;
            }
        }

        public async Task<string> ExtractVocabularyFromPdfTextAsync(string pdfText)
        {
            var systemInstruction = 
                "You are an expert English-Vietnamese dictionary and parser. Your task is to extract ALL vocabulary words from the provided text, which was extracted from a PDF. " +
                "Do not skip any words. For each word, extract or determine: the English word, its IPA pronunciation, and its Vietnamese meaning. " +
                "Do NOT wrap the output in markdown code blocks. The output must be raw JSON conforming to this schema:\n" +
                "[\n" +
                "  {\n" +
                "    \"word\": \"the vocabulary word\",\n" +
                "    \"pronunciation\": \"IPA pronunciation\",\n" +
                "    \"meaning\": \"meaning in Vietnamese\",\n" +
                "    \"example\": \"an example sentence using the word\",\n" +
                "    \"exampleTranslation\": \"Vietnamese translation of the example sentence\",\n" +
                "    \"tags\": \"PDF Import\"\n" +
                "  }\n" +
                "]";

            var prompt = $"Extract ALL vocabulary words from this PDF text:\n\n{pdfText}";

            try
            {
                return await ExtractVocabularyJsonViaGeminiAsync(prompt, systemInstruction);
            }
            catch (Exception geminiEx)
            {
                try
                {
                    var groqKey = await GetGroqApiKeyAsync();
                    if (!string.IsNullOrEmpty(groqKey))
                    {
                        return await GenerateTextViaGroqAsync(prompt, systemInstruction, true);
                    }
                }
                catch (Exception groqEx)
                {
                    throw new AggregateException("Both Gemini and fallback Groq failed.", geminiEx, groqEx);
                }
                throw;
            }
        }

        public async Task<string> LookUpVocabularyJsonAsync(string word)
        {
            var systemInstruction = 
                "You are an expert English-Vietnamese dictionary. Provide definition and details for the requested English word. " +
                "Do NOT wrap the output in markdown code blocks. The output must be raw JSON conforming to this schema:\n" +
                "{\n" +
                "  \"pronunciation\": \"IPA pronunciation\",\n" +
                "  \"meaning\": \"Concise meaning in Vietnamese\",\n" +
                "  \"example\": \"An example sentence using this word\",\n" +
                "  \"exampleTranslation\": \"Vietnamese translation of the example sentence\"\n" +
                "}";

            var prompt = $"Look up the word: '{word}'";

            try
            {
                return await LookUpVocabularyJsonViaGeminiAsync(prompt, systemInstruction);
            }
            catch (Exception geminiEx)
            {
                try
                {
                    var groqKey = await GetGroqApiKeyAsync();
                    if (!string.IsNullOrEmpty(groqKey))
                    {
                        return await GenerateTextViaGroqAsync(prompt, systemInstruction, true);
                    }
                }
                catch (Exception groqEx)
                {
                    throw new AggregateException("Both Gemini and fallback Groq failed.", geminiEx, groqEx);
                }
                throw;
            }
        }

        // --- GEMINI INTERNAL METHODS ---

        private async Task<string> GenerateTextViaGeminiAsync(string prompt, string? systemInstruction)
        {
            var apiKey = await GetApiKeyAsync();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            var requestBody = new GeminiRequest
            {
                Contents = new[]
                {
                    new GeminiContent { Parts = new[] { new GeminiPart { Text = prompt } } }
                }
            };

            if (!string.IsNullOrEmpty(systemInstruction))
            {
                requestBody.SystemInstruction = new GeminiSystemInstruction
                {
                    Parts = new[] { new GeminiPart { Text = systemInstruction } }
                };
            }

            var response = await _httpClient.PostAsJsonAsync(url, requestBody);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini API request failed: {response.StatusCode} - {errorMsg}");
            }

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
            var responseText = geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text;

            return responseText ?? "No response received from AI.";
        }

        private async Task<string> GenerateQuizJsonViaGeminiAsync(string prompt, string systemInstruction)
        {
            var apiKey = await GetApiKeyAsync();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            var requestBody = new GeminiRequest
            {
                Contents = new[]
                {
                    new GeminiContent { Parts = new[] { new GeminiPart { Text = prompt } } }
                },
                SystemInstruction = new GeminiSystemInstruction
                {
                    Parts = new[] { new GeminiPart { Text = systemInstruction } }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    ResponseMimeType = "application/json"
                }
            };

            var response = await _httpClient.PostAsJsonAsync(url, requestBody);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini API Quiz generation failed: {errorMsg}");
            }

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
            return geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "[]";
        }

        private async Task<string> EvaluatePronunciationJsonViaGeminiAsync(string prompt, string systemInstruction)
        {
            var apiKey = await GetApiKeyAsync();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            var requestBody = new GeminiRequest
            {
                Contents = new[]
                {
                    new GeminiContent { Parts = new[] { new GeminiPart { Text = prompt } } }
                },
                SystemInstruction = new GeminiSystemInstruction
                {
                    Parts = new[] { new GeminiPart { Text = systemInstruction } }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    ResponseMimeType = "application/json"
                }
            };

            var response = await _httpClient.PostAsJsonAsync(url, requestBody);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini API Pronunciation evaluation failed: {errorMsg}");
            }

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
            return geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "{}";
        }

        private async Task<string> ExtractVocabularyJsonViaGeminiAsync(string prompt, string systemInstruction)
        {
            var apiKey = await GetApiKeyAsync();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            var requestBody = new GeminiRequest
            {
                Contents = new[]
                {
                    new GeminiContent { Parts = new[] { new GeminiPart { Text = prompt } } }
                },
                SystemInstruction = new GeminiSystemInstruction
                {
                    Parts = new[] { new GeminiPart { Text = systemInstruction } }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    ResponseMimeType = "application/json"
                }
            };

            var response = await _httpClient.PostAsJsonAsync(url, requestBody);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini API Vocabulary Extraction failed: {errorMsg}");
            }

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
            return geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "[]";
        }

        private async Task<string> LookUpVocabularyJsonViaGeminiAsync(string prompt, string systemInstruction)
        {
            var apiKey = await GetApiKeyAsync();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

            var requestBody = new GeminiRequest
            {
                Contents = new[]
                {
                    new GeminiContent { Parts = new[] { new GeminiPart { Text = prompt } } }
                },
                SystemInstruction = new GeminiSystemInstruction
                {
                    Parts = new[] { new GeminiPart { Text = systemInstruction } }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    ResponseMimeType = "application/json"
                }
            };

            var response = await _httpClient.PostAsJsonAsync(url, requestBody);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini API Vocabulary Lookup failed: {errorMsg}");
            }

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponse>();
            return geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "{}";
        }
    }

    #region Gemini API Payload Mapping Models
    public class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();

        [JsonPropertyName("systemInstruction")]
        public GeminiSystemInstruction? SystemInstruction { get; set; }

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    public class GeminiContent
    {
        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    public class GeminiSystemInstruction
    {
        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    public class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    public class GeminiGenerationConfig
    {
        [JsonPropertyName("responseMimeType")]
        public string? ResponseMimeType { get; set; }
    }

    public class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public GeminiCandidate[]? Candidates { get; set; }
    }

    public class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }
    #endregion
}
