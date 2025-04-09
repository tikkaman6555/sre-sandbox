using tikkaman.apacheLogParser.CLI;
using tikkaman.sreSandbox.Middleware;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;


namespace tikkaman.sreSandbox.Controllers
{
  
    [ApiController]
    [Route("api/requestBlocker/log")]
    public class FailedRequestLogDBController : ControllerBase
    {
        private static List<string> _testData_ipGoodList = new() { "192.168.100.100", "192.168.100.101", "192.168.100.102", "192.168.100.103", "192.168.100.104", "192.168.100.105", "192.168.100.106", "192.168.100.107", "192.168.100.108", "192.168.100.109" };
        private static List<string> _testData_ipBadList = new() { "172.168.200.100", "172.168.200.101", "172.168.200.102", "172.168.200.103", "172.168.200.104", "172.168.200.105", "172.168.200.106", "172.168.200.107", "172.168.200.108", "172.168.200.109" };

        static Random _random = new Random();

        private readonly RequestBlockingEnabledConfig _requestBlockingEnabledConfig;
        private readonly ILogger<FailedRequestLogDBController> _logger;

        public FailedRequestLogDBController(RequestBlockingEnabledConfig requestBlockingEnabledConfig)
        {
            _logger = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            }).CreateLogger<FailedRequestLogDBController>();
            _logger.LogDebug("Creating FailedRequestLogDBController");

            _requestBlockingEnabledConfig = requestBlockingEnabledConfig;
            // Load the IP whitelist and blacklist from the configuration file
            // ipWhiteList = configuration.GetSection("ResponderSettings:IPWhitelist").Get<List<string>>() ?? new List<string>();
            // ipBlackList = configuration.GetSection("ResponderSettings:IPBlacklist").Get<List<string>>() ?? new List<string>();

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
            for(int i = 400; i < 600; i++)
                    _requestBlockingEnabledConfig.DefaultFailedRequestBlockRule.Status.Add(i);
            _requestBlockingEnabledConfig.DefaultFailedRequestBlockRule.MethodsBlock = new() { Middleware.HttpMethod.ALL};

            if(_requestBlockingEnabledConfig.Enabled)
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
#region filterDB actions
#region test actions
        [HttpPost("/filterDb/{count}")]
        public async Task<IActionResult> FilterDBAddDummies(int count = 10000)
        {
            int size = -1;
            using (var db = new FailedRequestLoggingContext())
            {
                for (int i = 0; i < count; i++)
                {
                    var requestCount = new RequestCount
                    {
                        Count = 1,
                        IP = _testData_ipGoodList[_random.Next(0, _testData_ipGoodList.Count)],
                        Method = Middleware.HttpMethod.POST,
                        Path = "/api/responder/respond/as/200",
                        Status = 200,
                        Timestamp = DateTime.UtcNow
                    };
                    await db.AddAsync(requestCount);
                }
                await db.SaveChangesAsync();
                size = await db.Requests.CountAsync();
            }
            return Ok(size);
        }
        
        [HttpDelete("/filterDb/{count}")]
        public async Task<IActionResult> FilterDBRemoveDummies(int count = 10000)
        {
            int size = -1;
            using (var db = new FailedRequestLoggingContext())
            {
                var recordsToRemove = await db.Requests
                    // .OrderByDescending(r => r.Timestamp)
                    .Take(count != 0 ? count : db.Requests.Count())
                    .ToListAsync();

                db.Requests.RemoveRange(recordsToRemove);
                await db.SaveChangesAsync();
                size = await db.Requests.CountAsync();
            }
            return Ok(size);
        }
        #endregion
        
        [HttpDelete("/filterDb/id/{id}")]
        public async Task<IActionResult> FilterDBRemove(int id)
        {
            int size = -1;
            using (var db = new FailedRequestLoggingContext())
            {
                var recordToRemove = await db.Requests
                    .Where(r => r.id == id)
                    .FirstOrDefaultAsync();

                if (recordToRemove == null)
                    return NotFound($"Record with ID {id} not found.");

                db.Requests.Remove(recordToRemove);
                await db.SaveChangesAsync();
                size = await db.Requests.CountAsync();
            }
            return Ok(size);
        }
        
        [HttpGet("/filterDb/count")]
        public async Task<IActionResult> FilterDBGetCount()
        {
            using (var db = new FailedRequestLoggingContext())
            {
                return Ok(await db.Requests.CountAsync());
            }
        }
        
        [HttpGet("/filterDb/all")]
        [HttpGet("/filterDb/from/{from}/to/{count}")]
        public async Task<IActionResult> FilterDBGet(int? from = 0, int? count = 1000)
        {
            using (var db = new FailedRequestLoggingContext())
            {
                List<RequestCount>? records = null;
                if(from == null || count == null)
                    records = await db.Requests
                        .OrderByDescending(r => r.Timestamp)
                        .ToListAsync();
                else
                    records = await db.Requests
                        .Skip((int)from)
                        .Take((int)count)
                        .ToListAsync();

                return Ok(records);
            }
        }
#endregion
      

    }

}