using tikkaman.apacheLogParser.CLI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace sreSandbox.Controllers
{
    [ApiController]
    [Route("api/logAsync")]
    public class AsyncLogController : ControllerBase
    {
        private readonly LogParser _parser;

        public AsyncLogController(IConfiguration configuration)
        {
            var baseDir = configuration["LogParserSettings:BaseDir"];
            if (string.IsNullOrEmpty(baseDir))
            {
                throw new DirectoryNotFoundException("Base directory for log files is not configured.");
            }

            _parser = new LogParser(baseDir);
        }

        [HttpGet("parseAccessLog/files")]
        public async Task<IActionResult> GetLogFilesAsync()
        {
            var files = await Task.Run(() =>
                _parser.LogFiles
                    .Select(file => Path.GetRelativePath(_parser.LogsPath, file))
                    .ToList()
            );
            if (files.Count == 0)
                return NoContent();
            return Ok(files);
        }

        [HttpGet("parseAccessLog/get/{method}/{status}/{file}")]
        public async Task<IActionResult> GetAllAsync(LogParser.HttpMethod method = LogParser.HttpMethod.ALL, string status = "200-600", string file = "")
        {
            if (string.IsNullOrEmpty(file))
                return BadRequest("File parameter is required.");

            file = Path.GetFullPath(file, _parser.LogsPath);
            if (!_parser.LogFiles.Contains(file))
                return NotFound($"File '{file}' not found in the logs directory.");

            var logEntries = await Task.Run(() =>
            {
                if (status.ToLower() == "successful") status = "200-400";
                else if (status.ToLower() == "clienterror") status = "400-500";
                else if (status.ToLower() == "servererror") status = "500-600";
                else if (status.ToLower() == "failed") status = "400-600";

                var _statusRange = status.Split('-').Select(s => int.Parse(s.Trim())).ToList();
                var statusRange = new Range(_statusRange[0], _statusRange[1]);

                var filter = new LogParser.RequestEntryFilters(statusRange, method);
                return _parser.parseApacheAccessLogFile(file, filter.ByMethod_ByStatus);
            });

            if (logEntries.Count == 0)
                return NoContent();
            return Ok(logEntries);
        }

        [HttpGet("parseAccessLog/count/{method}/{status}/{file}")]
        public async Task<IActionResult> GetCountAsync(LogParser.HttpMethod method = LogParser.HttpMethod.ALL, string status = "200-600", string file = "")
        {
            if (string.IsNullOrEmpty(file))
                return BadRequest("File parameter is required.");

            file = Path.GetFullPath(file, _parser.LogsPath);
            if (!_parser.LogFiles.Contains(file))
                return NotFound($"File '{file}' not found in the logs directory.");

            var count = await Task.Run(() =>
            {
                var _statusRange = status.Split('-').Select(s => int.Parse(s.Trim())).ToList();
                var statusRange = new Range(_statusRange[0], _statusRange[1]);

                var filter = new LogParser.RequestEntryFilters(statusRange, method);
                var logEntries = _parser.parseApacheAccessLogFile(file, filter.ByMethod_ByStatus);
                return logEntries.Count;
            });

            return Ok(count);
        }

        [HttpGet("parseAccessLog/count/groupBy/url/{method}/{status}/{file}")]
        public async Task<IActionResult> GetCountGroupByUrlAsync(LogParser.HttpMethod method = LogParser.HttpMethod.ALL, string status = "200-600", string file = "")
        {
            if (string.IsNullOrEmpty(file))
                return BadRequest("File parameter is required.");

            file = Path.GetFullPath(file, _parser.LogsPath);
            if (!_parser.LogFiles.Contains(file))
                return NotFound($"File '{file}' not found in the logs directory.");

            var grouped = await Task.Run(() =>
            {
                var _statusRange = status.Split('-').Select(s => int.Parse(s.Trim())).ToList();
                var statusRange = new Range(_statusRange[0], _statusRange[1]);

                var filter = new LogParser.RequestEntryFilters(statusRange, method);
                var logEntries = _parser.parseApacheAccessLogFile(file, filter.ByMethod_ByStatus);

                return logEntries
                    .GroupBy(g => g.Url)
                    .Select(g => new { Url = g.Key, Count = g.Count() })
                    .ToList();
            });

            if (grouped.Count == 0)
                return NoContent();
            return Ok(grouped);
        }

        [HttpGet("parseAccessLog/get/groupBy/timeIntervals/{timeBucketSize}/{method}/{status}/{file}")]
        public async Task<IActionResult> GetGroupByTimeIntervalsAsync(int timeBucketSize = 15, LogParser.HttpMethod method = LogParser.HttpMethod.ALL, string status = "400-600", string file = "")
        {
            if (string.IsNullOrEmpty(file))
                return BadRequest("File parameter is required.");

            file = Path.GetFullPath(file, _parser.LogsPath);
            if (!_parser.LogFiles.Contains(file))
                return NotFound($"File '{file}' not found in the logs directory.");

            var groupedByTime = await Task.Run(() =>
            {
                var _statusRange = status.Split('-').Select(s => int.Parse(s.Trim())).ToList();
                var statusRange = new Range(_statusRange[0], _statusRange[1]);

                var filter = new LogParser.RequestEntryFilters(statusRange, method);
                var logEntries = _parser.parseApacheAccessLogFile(file, filter.ByMethod_ByStatus);

                return logEntries.GroupBy(g =>
                {
                    var ts = g.Time;
                    ts = ts.AddMinutes(-(ts.Minute % timeBucketSize));
                    ts = ts.AddMilliseconds(-ts.Millisecond - 1000 * ts.Second);
                    return ts;
                })
                .Select(g => new { TimeBucket = g.Key, Value = g })
                .ToList();
            });

            if (groupedByTime.Count == 0)
                return NoContent();
            return Ok(groupedByTime);
        }

        [HttpGet("parseAccessLog/unsafe/timeItervals/{timeBucketSize}/threshold/{unsafeRequestThreshold}/{method}/{status}/{file}")]
        public async Task<IActionResult> GetUsafe(int timeBucketSize = 15, int unsafeRequestThreshold = 5, LogParser.HttpMethod method = LogParser.HttpMethod.ALL, string status = "400-600", string file = "")
        {
            IActionResult result = NoContent();

            if (string.IsNullOrEmpty(file))
                return BadRequest("File parameter is required.");
            else
            {
                file = Path.GetFullPath(file, _parser.LogsPath);
                if (!_parser.LogFiles.Contains(file))
                    return NotFound($"File '{file}' not found in the logs directory.");
            }

            var _statusRange = status.Split('-').Select(s => int.Parse(s.Trim())).ToList();
            var statusRange = new Range(_statusRange[0], _statusRange[1]);

            var filter = new LogParser.RequestEntryFilters(statusRange, method);
            List<LogParser.LogObject_ApacheAccess> logEntries = new();
            await Task.Run(() =>
            {
                logEntries = _parser.parseApacheAccessLogFile(file, filter.ByMethod_ByStatus);

                var groupedEntries =
                    logEntries
                    .GroupBy(g =>
                    {
                        var ts = g.Time;
                        ts = ts.AddMinutes(-(ts.Minute % timeBucketSize));
                        ts = ts.AddMilliseconds(-ts.Millisecond - 1000 * ts.Second);
                        ts = ts.AddMinutes(-ts.Minute - 1 * ts.Second);
                        return ts;
                    })
                    .Select(timeGroup => new
                    {
                        TimeBucket = timeGroup.Key,
                        Hosts = timeGroup
                            .GroupBy(g => g.Host)
                            .Select(hostGroup => new
                            {
                                Host = hostGroup.Key,
                                Urls = hostGroup
                                    .GroupBy(g => g.Url)
                                    .Where(urlGroup => urlGroup.Count() >= unsafeRequestThreshold)
                                    .Select(urlGroup => new
                                    {
                                        Url = urlGroup.Key,
                                        Count = urlGroup.Count()
                                    })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .ToList();

                var unsafeRequests = groupedEntries
                    .Where(a => a.Hosts
                        .Where(h => h.Urls.
                            Where(u => u.Count >= unsafeRequestThreshold)
                            .ToList().Count > 0)
                        .ToList().Count > 0)
                    .ToList();

                if (unsafeRequests.Count > 0)
                    result = Ok(unsafeRequests);
            });

            return result;
            //FLATTEN?
            // var unsafePostsFlatList = unsafePosts
            //     .SelectMany(a => a.Hosts
            //         .SelectMany(h => h.Urls
            //             .Where(u => u.Count >= unsafeRequestThreshold)
            //                 .Select(u => new {
            //                     TimeBucket = a.TimeBucket, 
            //                     Host = h.Host, 
            //                     Url = u.Url, 
            //                     Count = u.Count,
            //                     // _data = u
            //                      })))
            //     .ToList();

            // if (unsafePosts.Count == 0)
            //     return NoContent();
            // return Ok(unsafePosts);
        }


    }
}