
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace tikkaman.sreSandbox.Middleware
{
    public class FailedRequestBlockerMiddleware
    {
        private readonly RequestDelegate _next;

        private static FailedRequestBlockRule _defaultFailedRequestBlockRule = new FailedRequestBlockRule();
        private readonly ILogger<FailedRequestBlockerMiddleware> _logger;

        public FailedRequestBlockerMiddleware(RequestDelegate next)
        {
            _logger = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            }).CreateLogger<FailedRequestBlockerMiddleware>();
            _logger.LogDebug("Creating FailedRequestBlockerMiddleware");
            
            _next = next;

            _defaultFailedRequestBlockRule = new FailedRequestBlockRule()
            {
                Threshold = 5,
                IP = new List<string>(),
                MethodsMatch = new List<Middleware.HttpMethod>() { 
                    Middleware.HttpMethod.POST, 
                    Middleware.HttpMethod.PATCH, 
                    Middleware.HttpMethod.PUT,
                    Middleware.HttpMethod.DELETE,
                    Middleware.HttpMethod.UNKNOWN},
                PathMatch = new List<string>() { "regex:/api/responder/respond/as/.*" },
                PathBlock = "/api/responder/respond/as/",
                Status = new List<int>() { 400, 401, 403, 404, 500, 503 }, //change to list<delegates>
                Period = TimeSpan.FromMinutes(3),
                Name = "Default Rule - MW",
            };
            _defaultFailedRequestBlockRule.Status.Clear();
            for (int i = 400; i < 600; i++)
                _defaultFailedRequestBlockRule.Status.Add(i);
            _defaultFailedRequestBlockRule.MethodsBlock = new() { Middleware.HttpMethod.ALL};

            using (var db = new FailedRequestLoggingContext())
            {
                // db.Database.EnsureCreated();
                if (!db.FailedRequestBlockingRules.Any())
                {
                    db.FailedRequestBlockingRules.Add(_defaultFailedRequestBlockRule);
                    db.SaveChanges();
                }
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            Enum.TryParse(typeof(HttpMethod), context.Request.Method, true, out var method);
            var requestIpAddress = context.Connection.RemoteIpAddress?.MapToIPv4().ToString();
            context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor);
            if (!string.IsNullOrEmpty(forwardedFor))
                requestIpAddress = forwardedFor.ToString();
            var requestPath = context.Request.Path.ToString();
            var requestMethod = method != null ? (HttpMethod)method : HttpMethod.UNKNOWN;

            var requestStartTs = DateTime.UtcNow;

            #region FILTER - ON Response: failedRequestBlock
            context.Response.OnStarting(async context =>
            {
                DateTime middlewareProcessingStartedOnResponse = DateTime.UtcNow;
                HttpContext httpContext = (HttpContext)context;

                await filter_OnResponse(httpContext, requestMethod, requestIpAddress, requestPath, requestStartTs);

                var overhead = middlewareProcessingStartedOnResponse.Subtract(DateTime.UtcNow);
                httpContext.Response.Headers.Append("X-Overhead-Time-AtMiddleware-Response", overhead.TotalMilliseconds.ToString());
    
                // httpContext.Response.WriteAsync($"{prefix}Overhead: {overhead.TotalMilliseconds}ms");

                return;//Task.CompletedTask;
            }, context);
            #endregion


            #region FILTER - ON Request: failedRequestBlock

            await filter_OnRequest(context, method, requestIpAddress, requestPath, requestStartTs);
            
            #endregion
        }

        private async Task filter_OnRequest(HttpContext context, object? method, string? requestIpAddress, string requestPath, DateTime requestStartTs)
        {
            var middlewareProcessingStartedOnRequest = DateTime.UtcNow;
            using (var db = new FailedRequestLoggingContext())
            {
                #region match rules
                var rulesByBlockingPath = await db.FailedRequestBlockingRules
                    .Where(r => context.Request.Path.ToString().StartsWith(r.PathBlock))
                    .ToListAsync();

                if (rulesByBlockingPath.Count > 1)
                    throw new NotImplementedException($"Multiple rules found for PathBlock {context.Request.Path}.");

                var _failedRequestBlockRule = rulesByBlockingPath.FirstOrDefault();
                if (_failedRequestBlockRule == null)
                    _failedRequestBlockRule = _defaultFailedRequestBlockRule;

                #endregion

                #region match request
                bool isPath = false;
                foreach (var rpath in _failedRequestBlockRule.PathMatch)
                {
                    var regexPrefix = "regex:";
                    if (rpath.StartsWith(regexPrefix))
                    {
                        Regex rx = new(rpath.Replace(regexPrefix, ""));
                        if (rx.IsMatch(requestPath))
                        {
                            isPath = true;
                            break;
                        }
                    }
                    else
                    {
                        if (requestPath.StartsWith(rpath))
                        {
                            isPath = true;
                            break;
                        }
                    }
                }
                bool isIP = _failedRequestBlockRule.IP == null ||
                            (_failedRequestBlockRule.IP != null && (_failedRequestBlockRule.IP.Contains("*") || 
                            (requestIpAddress != null && _failedRequestBlockRule.IP.Contains(requestIpAddress)))) ||
                            _failedRequestBlockRule.IP?.Count == 0;

                bool isMethod = _failedRequestBlockRule.MethodsBlock.Contains(Middleware.HttpMethod.ALL) ||
                                method != null && _failedRequestBlockRule.MethodsBlock.Contains((Middleware.HttpMethod)method);
                #endregion
                #region filter 
                if (isPath && isIP & isMethod)
                {
                    var lastBlockedRequest = await db.Requests
                        .Where(r => r.Path.StartsWith(_failedRequestBlockRule.PathBlock) &&
                                    r.IP == requestIpAddress)
                        // && r.Method == (LogParser.HttpMethod)method 
                        // && r.Status == status)
                        .OrderByDescending(r => r.Timestamp)
                        .FirstOrDefaultAsync();

                    if (lastBlockedRequest != null)
                    {
                        var timeDiff = requestStartTs - lastBlockedRequest.Timestamp;
                        if (timeDiff < _failedRequestBlockRule.Period)
                        {
                            if (lastBlockedRequest.Count >= _failedRequestBlockRule.Threshold)
                            {
                                context.Response.StatusCode = _failedRequestBlockRule.ResponseStatus;
                                context.Response.OnStarting(
                                    async context =>
                                    {
                                        var overhead = middlewareProcessingStartedOnRequest.Subtract(DateTime.UtcNow);
                                        ((HttpContext)context).Response.Headers.Append("X-Overhead-Time-AtMiddleware-Request", overhead.TotalMilliseconds.ToString());
                                        await Task.CompletedTask;//return;
                                    }, context);
                                await context.Response.WriteAsync(_failedRequestBlockRule.ResponseMessage);

                                db.Update(lastBlockedRequest);
                                await db.SaveChangesAsync();

                                return;
                            }
                        }
                        else
                        {
                            if (timeDiff > _failedRequestBlockRule.ForgetAfter)
                            {
                                db.Remove(lastBlockedRequest);
                            }
                            else
                            {
                                lastBlockedRequest.Count = 0;
                                lastBlockedRequest.Timestamp = requestStartTs;
                                db.Update(lastBlockedRequest);
                            }
                            await db.SaveChangesAsync();
                        }
                    }
                }
                var overhead = middlewareProcessingStartedOnRequest.Subtract(DateTime.UtcNow);
                ((HttpContext)context).Response.Headers.Append("X-Overhead-Time-AtMiddleware-Request", overhead.TotalMilliseconds.ToString());
                await _next(context);
                #endregion
            }
        }

        private async Task filter_OnResponse(HttpContext context, HttpMethod? method, string? requestIpAddress, string requestPath, DateTime requestStartTs)
        {
            var middlewareProcessingStartedOnResponse = DateTime.UtcNow;
            var httpContext = (HttpContext)context;
            var responseStatus = httpContext.Response.StatusCode;

            using (var db = new FailedRequestLoggingContext())
            {
                #region match rules
                var rulesByBlockingPath = await db.FailedRequestBlockingRules
                    .Where(r => httpContext.Request.Path.ToString().StartsWith(r.PathBlock))
                    .ToListAsync();

                if (rulesByBlockingPath.Count > 1)
                    throw new NotImplementedException($"Multiple rules found for PathBlock {httpContext.Request.Path}.");

                var _failedRequestBlockRule = rulesByBlockingPath.FirstOrDefault();
                if (_failedRequestBlockRule == null)
                    _failedRequestBlockRule = _defaultFailedRequestBlockRule;

                #endregion

                #region match request
                bool isPath = false;
                foreach (var rpath in _failedRequestBlockRule.PathMatch)
                {
                    var regexPrefix = "regex:";
                    if (rpath.StartsWith(regexPrefix))
                    {
                        Regex rx = new(rpath.Replace(regexPrefix, ""));
                        if (rx.IsMatch(requestPath))
                        {
                            isPath = true;
                            break;
                        }
                    }
                    else
                    {
                        if (requestPath.StartsWith(rpath))
                        {
                            isPath = true;
                            break;
                        }
                    }
                }
                bool isIP = _failedRequestBlockRule.IP == null ||
                            (_failedRequestBlockRule.IP != null && (_failedRequestBlockRule.IP.Contains("*") || 
                            (requestIpAddress != null && _failedRequestBlockRule.IP.Contains(requestIpAddress)))) ||
                            _failedRequestBlockRule.IP?.Count == 0;

                bool isStatus = _failedRequestBlockRule.Status.Contains(responseStatus);
                bool isMethod = _failedRequestBlockRule.MethodsMatch.Contains(Middleware.HttpMethod.ALL) ||
                                method != null && _failedRequestBlockRule.MethodsMatch.Contains((Middleware.HttpMethod)method);
                #endregion
                #region filter 
                if (isPath && isIP)
                {
                    var lastBlockedRequest = await db.Requests
                        .Where(r => r.Path.StartsWith(_failedRequestBlockRule.PathBlock) &&
                                    r.IP == requestIpAddress)
                        // && r.Method == (LogParser.HttpMethod)method 
                        // && r.Status == status)
                        .OrderByDescending(r => r.Timestamp)
                        .FirstOrDefaultAsync();

                    if (lastBlockedRequest != null)
                    {
                        var timeDiff = requestStartTs - lastBlockedRequest.Timestamp;
                        if (timeDiff < _failedRequestBlockRule.Period)
                        {
                            if(isMethod)
                                lastBlockedRequest.Count++;
                            if (lastBlockedRequest.Count >= _failedRequestBlockRule.Threshold)
                            {
                                httpContext.Response.StatusCode = _failedRequestBlockRule.ResponseStatus;
                                httpContext.Response.Headers.Append("X-Rate-Limit-Limit", $"{_failedRequestBlockRule.Threshold}");
                                httpContext.Response.Headers.Append("X-Rate-Limit-Remaining", $"0");
                                httpContext.Response.Headers.Append("X-Rate-Limit-Reset", $"{_failedRequestBlockRule.Period.TotalSeconds}");
                            }
                            db.Update(lastBlockedRequest);
                        }
                        else
                        {
                            if (timeDiff > _failedRequestBlockRule.ForgetAfter)
                            {
                                db.Remove(lastBlockedRequest);
                            }
                            else
                            {
                                lastBlockedRequest.Count = 1;
                                lastBlockedRequest.Timestamp = requestStartTs;
                                db.Update(lastBlockedRequest);
                            }
                        }
                    }
                    else
                    {
                        if (isMethod && isStatus)
                        {
                            RequestCount requestCount = new RequestCount()
                            {
                                Count = 1,
                                IP = requestIpAddress ?? "Unknown",
                                Method = method != null ? (HttpMethod)method : HttpMethod.ALL,
                                Path = httpContext.Request.Path,
                                Status = responseStatus,
                                Timestamp = requestStartTs,
                                RuleName = _failedRequestBlockRule.Name
                            };
                            await db.AddAsync(requestCount);
                        }
                    }
                    await db.SaveChangesAsync();
                }
                #endregion
            }

            return;
        }
    }
    public static class FailedRequestBlockerMiddlewareExtensions
    {
        public static IApplicationBuilder UseFailedRequestBlocker(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<FailedRequestBlockerMiddleware>();
        }
    }
}