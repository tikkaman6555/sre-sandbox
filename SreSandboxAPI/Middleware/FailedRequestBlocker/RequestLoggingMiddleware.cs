using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading.Tasks;

namespace tikkaman.sreSandbox.Middleware
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var reqquestStartTime = DateTime.UtcNow;
            var tz_offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
            var str_tz_offset = $"{(tz_offset < TimeSpan.Zero ? "-" : "+")}{tz_offset:hhmm}";

            using (var buffer = new MemoryStream())
            {
                var request = context.Request;
                var response = context.Response;

                var bodyStream = response.Body;
                response.Body = buffer;

                await _next(context);
                
                stopwatch.Stop();

   
                //TEST implementation ONLY
                //127.0.0.1 <<6113>> [16/Aug/2013:15:45:34 +0000] 1966093us "GET / HTTP/1.1" 200 3478 "https://example.com/" "Mozilla/5.0 (X11; U; Linux x86_64; en-US; rv:1.9.2.18)" - -
                _logger.LogInformation(@"{ip} <<{pid}>> [{timestamp} {tzoffset}] {elapsedMilliseconds}us ""{method} {url}"" {statusCode} {responseSize} ""{referrer}"" ""{userAgent}"" - -",
                    context.Connection.RemoteIpAddress?.MapToIPv4().ToString(),
                    Process.GetCurrentProcess().Id,
                    reqquestStartTime.ToString("dd'/'MMM'/'yyyy:hh:mm:ss"),
                    str_tz_offset,
                    stopwatch.ElapsedTicks / 10, // Convert ticks to microseconds   
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    response.ContentLength ?? buffer.Length, //context.Response.ContentLength,//not set by ASP.NET Core - would need to buffer/ count the stream length - no benefit ATM
                    context.Request.Headers["Referer"].ToString() ?? "-",
                    context.Request.Headers["User-Agent"].ToString() ?? "-");

            
                
                buffer.Position = 0;
                await buffer.CopyToAsync(bodyStream);
            }
        }
        public async Task InvokeAsync0(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var reqquestStartTime = DateTime.UtcNow;

            await _next(context);

            stopwatch.Stop();

            _logger.LogInformation(@"{ip} <<{pid}>> [{timestamp}] {elapsedMilliseconds}us ""{method} {url}"" {statusCode} {responseSize} ""{referrer}"" ""{userAgent}"" - -",
                context.Connection.RemoteIpAddress?.MapToIPv4().ToString(),
                Process.GetCurrentProcess().Id,
                reqquestStartTime.ToString("dd/MMM/yyyy:hh:mm:ss +zzz"),
                stopwatch.ElapsedTicks / 10, // Convert ticks to microseconds   
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                context.Response.ContentLength ?? 0, //not set by ASP.NET Core - would need to buffer/ count the stream length - no benefit ATM
                context.Request.Headers["Referer"].ToString() ?? "-",
                context.Request.Headers["User-Agent"].ToString() ?? "-");

            //127.0.0.1 <<6113>> [16/Aug/2013:15:45:34 +0000] 1966093us "GET / HTTP/1.1" 200 3478 "https://example.com/" "Mozilla/5.0 (X11; U; Linux x86_64; en-US; rv:1.9.2.18)" - -

        }
    }

    public static class RequestLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RequestLoggingMiddleware>();
        }
    }
}