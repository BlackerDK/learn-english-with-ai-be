using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using backend.Services;

namespace backend.Middlewares
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        public ErrorHandlingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // Ghi log lỗi vào file
                FileLogger.LogError(ex.Message, "Backend API", ex.StackTrace);

                // Trả về JSON lỗi cho client
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";

            // Flatten AggregateException to get the root message
            var rootEx = exception is AggregateException agg ? agg.InnerExceptions[0] : exception;
            var message = rootEx.Message;

            // Detect API key missing — return 503 with a clear error code
            bool isApiKeyMissing =
                message.Contains("API Key is not configured") ||
                message.Contains("API_KEY_INVALID") ||
                message.Contains("PERMISSION_DENIED") ||
                message.Contains("401") ||
                message.Contains("403");

            context.Response.StatusCode = isApiKeyMissing
                ? (int)HttpStatusCode.ServiceUnavailable   // 503
                : (int)HttpStatusCode.InternalServerError; // 500

            var response = new
            {
                statusCode = context.Response.StatusCode,
                errorCode  = isApiKeyMissing ? "API_KEY_MISSING" : "INTERNAL_ERROR",
                message    = isApiKeyMissing
                    ? "Chưa cấu hình API Key AI. Vui lòng vào Cài đặt để nhập Gemini hoặc Groq API Key."
                    : "Internal Server Error. Lỗi đã được hệ thống ghi nhận.",
                detail = exception.Message
            };

            var jsonString = JsonSerializer.Serialize(response);
            return context.Response.WriteAsync(jsonString);
        }
    }
}
