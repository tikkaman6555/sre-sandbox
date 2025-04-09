using tikkaman.apacheLogParser.CLI;
using tikkaman.sreSandbox.Middleware;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;


namespace tikkaman.sreSandbox.Controllers
{

    [ApiController]
    [Route("api/responder")]
    public class ResponderTestController : ControllerBase
    {
        private static List<string> ipWhiteList = new();
        private static List<string> ipBlackList = new();

        #region test
        private static List<string> _ipGoodList = new() { "192.168.100.100", "192.168.100.101", "192.168.100.102", "192.168.100.103", "192.168.100.104", "192.168.100.105", "192.168.100.106", "192.168.100.107", "192.168.100.108", "192.168.100.109" };
        private static List<string> _ipBadList = new() { "172.168.200.100", "172.168.200.101", "172.168.200.102", "172.168.200.103", "172.168.200.104", "172.168.200.105", "172.168.200.106", "172.168.200.107", "172.168.200.108", "172.168.200.109" };

        static Random _random = new Random();

        private readonly RequestBlockingEnabledConfig _requestBlockingEnabledConfig;
        private readonly ILogger<ResponderTestController> _logger;
        private readonly IConfiguration _configuration;

        #endregion
        public ResponderTestController(IConfiguration configuration, RequestBlockingEnabledConfig requestBlockingEnabledConfig)
        {
            _logger = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
            }).CreateLogger<ResponderTestController>();
            _logger.LogInformation("ResponderTestController initialized.");

            _requestBlockingEnabledConfig = requestBlockingEnabledConfig;
            _configuration = configuration;

            ipWhiteList = configuration.GetSection("ResponderSettings:IPWhitelist").Get<List<string>>() ?? new List<string>();
            ipBlackList = configuration.GetSection("ResponderSettings:IPBlacklist").Get<List<string>>() ?? new List<string>();

            _requestBlockingEnabledConfig.DefaultFailedRequestBlockRule = new FailedRequestBlockRule()
            {
                Threshold = 5,
                IP = new List<string>(),
                MethodsMatch = new List<Middleware.HttpMethod>() { Middleware.HttpMethod.ALL },
                PathMatch = new List<string>() { "regex:/api/responder/respond/as/.*" },
                PathBlock = "/api/responder/respond/as/",
                Status = new List<int>() { 400, 401, 403, 404, 500, 503 }, //change to list<delegates>
                Period = TimeSpan.FromMinutes(1)
            };
            _requestBlockingEnabledConfig.DefaultFailedRequestBlockRule.Status.Clear();
            for (int i = 400; i < 600; i++)
                _requestBlockingEnabledConfig.DefaultFailedRequestBlockRule.Status.Add(i);
            _requestBlockingEnabledConfig.DefaultFailedRequestBlockRule.MethodsBlock = new() { Middleware.HttpMethod.ALL };

            if (_requestBlockingEnabledConfig.Enabled)
                using (var db = new FailedRequestLoggingContext())
                {
                    // db.Database.EnsureCreated();
                    if (!db.FailedRequestBlockingRules.Any())
                    {
                        db.FailedRequestBlockingRules.Add(_requestBlockingEnabledConfig.DefaultFailedRequestBlockRule);
                        db.SaveChanges();
                    }
                }
        }

        #region responders
        [HttpGet("respond/as/{status}@{probability}")]
        [HttpHead("respond/as/{status}@{probability}")]
        [HttpOptions("respond/as/{status}@{probability}")]
        [HttpPost("respond/as/{status}@{probability}")]
        [HttpPatch("respond/as/{status}@{probability}")]
        [HttpPut("respond/as/{status}@{probability}")]
        [HttpDelete("respond/as/{status}@{probability}")]
        public async Task<IActionResult> RespondAsGet(int status = 400, int probability = 50)
        {
            return await GenerateResponse(status, probability);
        }

        [HttpGet("respond/as/200")]
        [HttpHead("respond/as/200")]
        [HttpOptions("respond/as/200")]
        [HttpPost("respond/as/200")]
        [HttpPatch("respond/as/200")]
        [HttpPut("respond/as/200")]
        [HttpDelete("respond/as/200")]
        public async Task<IActionResult> RespondAsGet200()
        {
            return await GenerateResponse(200, 100);
        }

        [HttpGet("respond/other/200")]
        [HttpHead("respond/other/200")]
        [HttpOptions("respond/other/200")]
        [HttpPost("respond/other/200")]
        [HttpPatch("respond/other/200")]
        [HttpPut("respond/other/200")]
        [HttpDelete("respond/other/200")]
        public async Task<IActionResult> RespondAsGet200_Other()
        {
            return await GenerateResponse(200, 100);
        }

        [HttpGet("respond/other/400")]
        [HttpHead("respond/other/400")]
        [HttpOptions("respond/other/400")]
        [HttpPost("respond/other/400")]
        [HttpPatch("respond/other/400")]
        [HttpPut("respond/other/400")]
        [HttpDelete("respond/other/400")]
        public async Task<IActionResult> RespondAsGet400_Other()
        {
            return await GenerateResponse(400, 100);
        }

        [NonAction]
        public async Task<IActionResult> GenerateResponse(int status = -1, int probability = 100, int delay = 500)
        {
            if (probability < 0 || probability > 100)
                return BadRequest("Probability must be between 0 and 100.");
            if (status < -1 || status > 599)
                return BadRequest("Status code must be between -1 and 599.");

            #region general request details
            var requestTs = DateTime.UtcNow;
            Enum.TryParse(typeof(LogParser.HttpMethod), HttpContext.Request.Method, true, out var method);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString();
            HttpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor);
            if (!string.IsNullOrEmpty(forwardedFor))
                ipAddress = forwardedFor.ToString();
            var path = HttpContext.Request.Path.ToString();

            #endregion

            #region  generate TEST response
            delay = _random.Next(0, delay);
            var prefix = $"[GenerateResponse] @ {probability}%: {method} {status} {ipAddress} {forwardedFor} {requestTs:yyyy-MM-dd HH:mm:ss.fffff} with delay of {delay}ms\n";

            var probabilityCheck = _random.Next(100) <= probability;
            if (!probabilityCheck)
            {
                status = 0;
                prefix += "Probability check failed, returning default response.\n";
            }

            var content = RandomString(requestTs.Millisecond, prefix);
            var responseOptions = new Dictionary<int, IActionResult>(){
                {200, Ok($"{prefix}OK")},
                {400, BadRequest($"{prefix}Bad Request")},
                {401, Unauthorized($"{prefix}Unauthorized")},
                {403, StatusCode(403, $"{prefix}Forbidden")},//Forbid($"{prefix}Forbidden")},
                {404, NotFound($"{prefix}Not Found")},
                {500, StatusCode(500, $"{prefix}Internal Server Error")},
                {0, Content(content, "text/plain")}
            };

            IActionResult response;
            if (responseOptions.ContainsKey(status))
            {
                response = responseOptions[status];
            }
            else
            {
                var randomResponse = _random.Next(0, responseOptions.Count);
                response = responseOptions.ElementAt(randomResponse).Value;
            }
            #endregion

            #region FILTER: failedRequestBlock
            if (_requestBlockingEnabledConfig.Enabled)
                using (var db = new FailedRequestLoggingContext())
                {
                    var route = ControllerContext.ActionDescriptor.AttributeRouteInfo?.Template != null
                        ? $"/{ControllerContext.ActionDescriptor.AttributeRouteInfo.Template}"
                        : "/";
                    var rulesByBlockingPath = await db.FailedRequestBlockingRules
                        .Where(r => route.StartsWith(r.PathBlock))
                        .ToListAsync();

                    if (rulesByBlockingPath.Count > 1)
                        throw new NotImplementedException($"Multiple rules found for PathBlock {route}.");

                    var _failedRequestBlockRule = rulesByBlockingPath.FirstOrDefault();
                    if (_failedRequestBlockRule == null)
                        _failedRequestBlockRule = _requestBlockingEnabledConfig.DefaultFailedRequestBlockRule;

                    bool isPath = false;
                    foreach (var rpath in _failedRequestBlockRule.PathMatch)
                    {
                        var regexPrefix = "regex:";
                        if (rpath.StartsWith(regexPrefix))
                        {
                            Regex rx = new(rpath.Replace(regexPrefix, ""));
                            if (rx.IsMatch(path))
                            {
                                isPath = true;
                                break;
                            }
                        }
                        else
                        {
                            if (path.StartsWith(rpath))
                            {
                                isPath = true;
                                break;
                            }
                        }
                    }
                    bool isIP = _failedRequestBlockRule.IP == null ||
                                (_failedRequestBlockRule.IP != null && (_failedRequestBlockRule.IP.Contains("*") ||
                                (ipAddress != null && _failedRequestBlockRule.IP.Contains(ipAddress)))) ||
                                _failedRequestBlockRule.IP?.Count == 0;

                    bool isStatus = _failedRequestBlockRule.Status.Contains(status);
                    bool isMethod = _failedRequestBlockRule.MethodsMatch.Contains(Middleware.HttpMethod.ALL) ||
                                    method != null && _failedRequestBlockRule.MethodsMatch.Contains((Middleware.HttpMethod)method);

                    if (isPath && isIP)
                    {
                        var lastBlockedRequest = await db.Requests
                            .Where(r => r.Path.StartsWith(_failedRequestBlockRule.PathBlock) &&
                                        r.IP == ipAddress)
                            // && r.Method == (LogParser.HttpMethod)method 
                            // && r.Status == status)
                            .OrderByDescending(r => r.Timestamp)
                            .FirstOrDefaultAsync();

                        if (lastBlockedRequest != null)
                        {
                            var timeDiff = requestTs - lastBlockedRequest.Timestamp;
                            if (timeDiff < _failedRequestBlockRule.Period)
                            {
                                lastBlockedRequest.Count++;
                                if (lastBlockedRequest.Count >= _failedRequestBlockRule.Threshold)
                                {
                                    response = StatusCode(_failedRequestBlockRule.ResponseStatus, $"{prefix}{_failedRequestBlockRule.ResponseMessage}");
                                    HttpContext.Response.Headers.Append("X-Rate-Limit-Limit", $"{_failedRequestBlockRule.Threshold}");
                                    HttpContext.Response.Headers.Append("X-Rate-Limit-Remaining", $"0");
                                    HttpContext.Response.Headers.Append("X-Rate-Limit-Reset", $"{_failedRequestBlockRule.Period.TotalSeconds}");
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
                                    lastBlockedRequest.Timestamp = requestTs;
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
                                    IP = ipAddress ?? "Unknown",
                                    Method = method != null ? (Middleware.HttpMethod)method : Middleware.HttpMethod.ALL,
                                    Path = HttpContext.Request.Path,
                                    Status = status,
                                    Timestamp = requestTs
                                };
                                await db.AddAsync(requestCount);
                            }
                        }
                        await db.SaveChangesAsync();
                    }
                }
            #endregion

            var overhead = requestTs.Subtract(DateTime.UtcNow).TotalMilliseconds;
            HttpContext.Response.Headers.Append("X-Overhead-Time-AtController", overhead.ToString());

            var property = response.GetType().GetProperty("Value");
            var value = property?.GetValue(response);
            var responseText = value?.ToString() ?? string.Empty;
            responseText = $"{responseText}\n\nProcessing overhead: {overhead}ms";
            if (property != null)
                property.SetValue(response, responseText);


            await Task.Delay(delay);
            return response;
        }

        internal static string RandomString(int stringLength, string prefix = "")
        {
            if (stringLength < 1)
            {
                return string.Empty;
            }

            if (stringLength > 100)
            {
                stringLength = 100;
            }

            if (!string.IsNullOrEmpty(prefix))
            {
                stringLength -= prefix.Length;
            }

            if (stringLength < 1)
            {
                return prefix;
            }

            // Generate a random string of the specified length
            return prefix + GenerateRandomString(stringLength);
        }
        private static string GenerateRandomString(int stringLength)
        {
            const string allowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz0123456789!@$?_-";
            char[] chars = new char[stringLength];

            for (int i = 0; i < stringLength; i++)
            {
                chars[i] = allowedChars[_random.Next(0, allowedChars.Length)];
            }

            return new string(chars);
        }

        #endregion

    }

}