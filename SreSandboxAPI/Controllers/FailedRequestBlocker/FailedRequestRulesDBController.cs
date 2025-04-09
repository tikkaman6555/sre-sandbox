using tikkaman.apacheLogParser.CLI;
using tikkaman.sreSandbox.Middleware;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;


namespace tikkaman.sreSandbox.Controllers
{
  
    [ApiController]
    [Route("api/requestBlocker/rules")]
    public class FailedRequestRulesDBController : ControllerBase
    {

        private readonly RequestBlockingEnabledConfig _requestBlockingEnabledConfig;
        private readonly ILogger<FailedRequestRulesDBController> _logger;

        public FailedRequestRulesDBController(RequestBlockingEnabledConfig requestBlockingEnabledConfig)
        {
            _logger = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            }).CreateLogger<FailedRequestRulesDBController>();
            _logger.LogDebug("Creating FailedRequestRulesDBController");

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
#region rulesDB actions
        [HttpPost("/rulesDB")]
        [HttpPost("/rulesDB/default")]
        public async Task<IActionResult> RulesDBAdd([FromBody] FailedRequestBlockRule? rule = null)
        {
            if(rule == null)
                rule = _requestBlockingEnabledConfig.DefaultFailedRequestBlockRule;
            using (var db = new FailedRequestLoggingContext())
            {
                var currentRule = await db.FailedRequestBlockingRules
                    .FirstOrDefaultAsync(r => 
                        r.PathBlock == rule.PathBlock
                        // && r.IP == rule.IP
                        // && r.Method == rule.Method
                        // && r.Status == rule.Status
                        // && r.Period == rule.Period
                    );
                if(currentRule != null)
                {
                    return BadRequest($"Rule with PathBlock {rule.PathBlock} already exists.");
                }
                await db.AddAsync(rule);
                await db.SaveChangesAsync();
            }
            return Ok(rule);
        }
        
        [HttpDelete("/rulesDB/{id}")]
        public async Task<IActionResult> RulesDBRemove(int id)
        {
            using (var db = new FailedRequestLoggingContext())
            {
                var ruleToRemove = await db.FailedRequestBlockingRules
                    .FirstOrDefaultAsync(r => r.id == id);

                if(ruleToRemove == null)
                    return NotFound($"Rule with ID {id} not found.");
                
                db.Remove(ruleToRemove);
                await db.SaveChangesAsync();
            }
            return NoContent();
        }
        [HttpPut("/rulesDB")]
        public async Task<IActionResult> RulesDBUpdate([FromBody] FailedRequestBlockRule rule)
        {
            using (var db = new FailedRequestLoggingContext())
            {
                var ruleToUpdate = await db.FailedRequestBlockingRules
                    .FirstOrDefaultAsync(r => r.id == rule.id);

                if(ruleToUpdate == null)
                    return NotFound($"Rule with ID {rule.id} not found.");
                
                db.Entry(ruleToUpdate).CurrentValues.SetValues(rule);
                // db.Update(ruleToUpdate);
                await db.SaveChangesAsync();
                return Ok(ruleToUpdate);
            }
        }
        [HttpGet("/rulesDB/all")]
        [HttpGet("/rulesDB/blockingPath/{blockingPath}")]
        public async Task<IActionResult> RulesDBGet(string blockingPath = "*")
        {
            if(blockingPath == null || blockingPath == "*")
                blockingPath = "*";
            blockingPath = Uri.UnescapeDataString(blockingPath);
            
            using (var db = new FailedRequestLoggingContext())
            {
                List<FailedRequestBlockRule> rules = new();

                if(blockingPath == "*"){
                    rules = await db.FailedRequestBlockingRules
                        .ToListAsync();

                    if(rules == null || rules.Count == 0)
                        return NoContent();
                }
                else{
                    rules = await db.FailedRequestBlockingRules
                        .Where(r => r.PathBlock == blockingPath)
                        .ToListAsync();

                    if(rules == null || rules.Count == 0)
                        return NotFound($"No rules found for PathBlock {blockingPath}.");
                }

                return Ok(rules);
            }            
        }
#endregion

        

    }

}