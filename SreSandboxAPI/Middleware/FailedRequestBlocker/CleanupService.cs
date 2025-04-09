using System.ComponentModel.DataAnnotations;
using System.Reflection.Metadata.Ecma335;
using Microsoft.EntityFrameworkCore;

namespace tikkaman.sreSandbox.Middleware
{
    public record PeriodicCleanupServiceState(bool IsEnabled, TimeSpan Period);
    public class PeriodicCleanupService : BackgroundService
    {
        private readonly ILogger<PeriodicCleanupService> _logger;
        private readonly IServiceScopeFactory _factory;
        private int _executionCount = 0;
        public bool IsEnabled { get; set; }
        public TimeSpan Period { get; set; } = TimeSpan.FromSeconds(30);

        public PeriodicCleanupService(
            ILogger<PeriodicCleanupService> logger,
            IServiceScopeFactory factory)
        {
            _logger = logger;
            _factory = factory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timerConfigCheck = new PeriodicTimer(TimeSpan.FromSeconds(30));
            
            var lastAction = DateTime.UtcNow;
            
            while (!stoppingToken.IsCancellationRequested && await timerConfigCheck.WaitForNextTickAsync(stoppingToken))
            {
                while (!stoppingToken.IsCancellationRequested && DateTime.UtcNow - lastAction < Period)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                try
                    {
                        lastAction = DateTime.UtcNow;
                        if (IsEnabled)
                        {
                            await using AsyncServiceScope asyncScope = _factory.CreateAsyncScope();
                            FailedRequestCleanupService cleanupService = asyncScope.ServiceProvider.GetRequiredService<FailedRequestCleanupService>();
                            await cleanupService.CleanupOldRquestCounters();
                            _executionCount++;
                            _logger.LogInformation(
                                $"Executed PeriodicCleanupService - Count: {_executionCount}");
                        }
                        else
                        {
                            _logger.LogInformation(
                                "Skipped PeriodicCleanupService");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation(
                            $"Failed to execute PeriodicCleanupService with exception message {ex.Message}.");
                    }
            }
        }
    }
    public class FailedRequestCleanupService
    {
        private readonly ILogger<FailedRequestCleanupService> _logger;

        public FailedRequestCleanupService(ILogger<FailedRequestCleanupService> logger)
        {
            _logger = logger;
        }

        public async Task<int> CleanupOldRquestCounters()
        {
            _logger.LogInformation("Cleanup expired request counters:");
            int countNamed = 0;
            int countAbandoned = 0;


            using (var db = new FailedRequestLoggingContext())
            {
                var now = DateTime.UtcNow;
                _logger.LogInformation("\t* By rule:");


                var rulesAll = await db.FailedRequestBlockingRules.ToListAsync();
                var requestsAll = await db.Requests.ToListAsync();

                var requestsByRule = requestsAll
                    .GroupBy(r => r.RuleName)
                    .Select(g => new
                    {
                        RuleName = g.Key,
                        Requests = g//.OrderByDescending(r => r.Timestamp)
                    })
                    .ToDictionary(g => g.RuleName, g => g.Requests.ToList());


                foreach (var rule in rulesAll)
                {
                    if(!requestsByRule.ContainsKey(rule.Name))
                        continue;
                        
                    var entriesToForgetByRule = requestsByRule[rule.Name]
                        .Where(req => now - req.Timestamp > rule.ForgetAfter)
                        .ToList();

                    if (entriesToForgetByRule.Count == 0)
                        if (requestsByRule[rule.Name].Count > 0)
                        {
                            requestsByRule.Remove(rule.Name);
                            continue;
                        }
                    db.Requests.RemoveRange(entriesToForgetByRule);
                    countNamed += entriesToForgetByRule.Count;
                    _logger.LogInformation($"\t\t- Removed {entriesToForgetByRule.Count} entries to forget by rule {rule.Name}.");

                    requestsByRule.Remove(rule.Name);
                }
                _logger.LogInformation($"\t* By rule - total: {countNamed}");

                _logger.LogInformation("\t* No rule - abandoned:");
                foreach (var rule in requestsByRule) //remaning assumed to be abandoned
                {
                    if (requestsByRule[rule.Key].Count > 0)
                    {
                        countAbandoned += requestsByRule[rule.Key].Count;
                        db.Requests.RemoveRange(requestsByRule[rule.Key]);
                        _logger.LogInformation(
                                $"Removed {requestsByRule[rule.Key].Count} entries with inactive rule {rule.Key}.");

                        requestsByRule.Remove(rule.Key);
                    }
                }
                _logger.LogInformation($"\t* By rule - total: {countAbandoned}");

                await db.SaveChangesAsync();
            }

            return countNamed + countAbandoned;
        }
    }
}