using tikkaman.apacheLogParser.CLI;
using tikkaman.sreSandbox.Middleware;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;


namespace tikkaman.sreSandbox.Controllers
{

    [ApiController]
    [Route("api/requestBlocker/cleanup")]
    public class FailedRequestCleanupServiceController : ControllerBase
    {

        private readonly ILogger<FailedRequestCleanupServiceController> _logger;

        public FailedRequestCleanupServiceController()
        {
            _logger = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            }).CreateLogger<FailedRequestCleanupServiceController>();
            _logger.LogDebug("Creating FailedRequestCleanupServiceController");
        }
        #region cleanup service actions

        [HttpGet("/state/")]
        public PeriodicCleanupServiceState GetState(PeriodicCleanupService service)
        {
            return new PeriodicCleanupServiceState(service.IsEnabled, service.Period);
        }
        [HttpPut("/cleanup/enable/{enable}")]
        [HttpPut("/cleanup/enable/{enable}/period/{period}")]
        public PeriodicCleanupServiceState SetState(PeriodicCleanupService service, bool enable, string period = "00:05:00")
        {
            service.IsEnabled = enable;
            service.Period = TimeSpan.Parse(period);
            return new PeriodicCleanupServiceState(service.IsEnabled, service.Period);
        }

        [HttpPatch("/state")]
        public PeriodicCleanupServiceState SetState(PeriodicCleanupServiceState state, 
                                                  PeriodicCleanupService service)
        {
            service.IsEnabled = state.IsEnabled;
            service.Period = state.Period;
            return new PeriodicCleanupServiceState(service.IsEnabled, service.Period);
        }
        [HttpDelete("/cleanup")]
        public async Task<IActionResult> CleanupNow(FailedRequestCleanupService service)
        {
            return Ok(await service.CleanupOldRquestCounters());
        }

        #endregion



    }

}