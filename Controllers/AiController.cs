using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using backend.Services;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AiController : ControllerBase
    {
        private readonly GeminiService _geminiService;

        public AiController(GeminiService geminiService)
        {
            _geminiService = geminiService;
        }

        // POST: api/ai/chat
        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest("Message cannot be empty.");
            }

            var systemInstruction = 
                "You are an expert friendly language tutor. Your goal is to help the user learn their target language. " +
                "Respond in a helpful, conversational manner. Use Vietnamese to explain grammar, vocabulary, " +
                "pronunciation, or translations. Keep explanations clear, and provide practical examples. " +
                "If the user asks questions in their target language, chat back in that language but offer translations/explanations in Vietnamese if necessary.";

            var prompt = request.Message;
            if (!string.IsNullOrEmpty(request.Context))
            {
                prompt = $"[Context: {request.Context}]\n\nUser Question: {request.Message}";
            }

            var responseText = await _geminiService.GenerateTextAsync(prompt, systemInstruction);
            return Ok(new { response = responseText });
        }

        // POST: api/ai/evaluate-speech
        [HttpPost("evaluate-speech")]
        public async Task<IActionResult> EvaluateSpeech([FromBody] EvaluateSpeechRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Target) || string.IsNullOrWhiteSpace(request.Spoken))
            {
                return BadRequest("Target and Spoken sentences cannot be empty.");
            }

            try
            {
                var evaluationJson = await _geminiService.EvaluatePronunciationJsonAsync(request.Target, request.Spoken);
                return Content(evaluationJson, "application/json");
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? Context { get; set; }
    }

    public class EvaluateSpeechRequest
    {
        public string Target { get; set; } = string.Empty;
        public string Spoken { get; set; } = string.Empty;
    }
}
