using tikkaman.apacheLogParser.CLI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;

namespace sreSandbox.Controllers
{
    [ApiController]
    [Route("api/log")]
    public class LogController : ControllerBase
    {
        private readonly LogParser _parser;

        public LogController(IConfiguration configuration)
        {
            var baseDir = configuration["LogParserSettings:BaseDir"];
            if (string.IsNullOrEmpty(baseDir))
            {
                throw new DirectoryNotFoundException("Base directory for log files is not configured.");
            }

            _parser = new LogParser(baseDir);
        }

        [HttpGet("parseAccessLog/files")]
        public IActionResult GetLogFiles()
        {
            var files = _parser.LogFiles
                .Select(file => Path.GetRelativePath(_parser.LogsPath, file))
                .ToList();
            if (files.Count == 0)
                return NoContent();
            return Ok(files);
        }

        [HttpGet("parseAccessLog/get/{method}/{status}/{file}")]
        public IActionResult GetAll(LogParser.HttpMethod method = LogParser.HttpMethod.ALL, string status = "200-600", string file = "")
        {
            if (string.IsNullOrEmpty(file))
                return BadRequest("File parameter is required.");
            else
            {
                file = Path.GetFullPath(file, _parser.LogsPath);
                if (!_parser.LogFiles.Contains(file))
                    return NotFound($"File '{file}' not found in the logs directory.");
            }

            List<LogParser.LogObject_ApacheAccess> logEntries = new();
            if(status.ToLower() == "successful")
            {
                status = "200-400";
            }
            else if (status.ToLower() == "clienterror")
            {
                status = "400-500";
            }
            else if (status.ToLower() == "servererror")
            {
                status = "500-600";
            }
            else if (status.ToLower() == "failed")
            {
                status = "400-600";
            }
            var _statusRange = status.Split('-').Select(s => int.Parse(s.Trim())).ToList();
            var statusRange = new Range(_statusRange[0], _statusRange[1]);

            var filter = new LogParser.RequestEntryFilters(statusRange, method);
            logEntries = _parser.parseApacheAccessLogFile(file, filter.ByMethod_ByStatus);

            if (logEntries.Count == 0)
                return NoContent();
            return Ok(logEntries);
        }

        [HttpGet("parseAccessLog/count/{method}/{status}/{file}")]
        public IActionResult GetCount(LogParser.HttpMethod method = LogParser.HttpMethod.ALL, string status = "200-600", string file = "")
        {
            if (string.IsNullOrEmpty(file))
                return BadRequest("File parameter is required.");
            else
            {
                file = Path.GetFullPath(file, _parser.LogsPath);
                if (!_parser.LogFiles.Contains(file))
                    return NotFound($"File '{file}' not found in the logs directory.");
            }

            List<LogParser.LogObject_ApacheAccess> logEntries = new();
            var _statusRange = status.Split('-').Select(s => int.Parse(s.Trim())).ToList();
            var statusRange = new Range(_statusRange[0], _statusRange[1]);

            var filter = new LogParser.RequestEntryFilters(statusRange, method);
            logEntries = _parser.parseApacheAccessLogFile(file, filter.ByMethod_ByStatus);

            return Ok(logEntries.Count);
        }
        [HttpGet("parseAccessLog/count/groupBy/url/{method}/{status}/{file}")]
        public IActionResult GetCountGroupByUrl(LogParser.HttpMethod method = LogParser.HttpMethod.ALL, string status = "200-600", string file = "")
        {
            if (string.IsNullOrEmpty(file))
                return BadRequest("File parameter is required.");
            else
            {
                file = Path.GetFullPath(file, _parser.LogsPath);
                if (!_parser.LogFiles.Contains(file))
                    return NotFound($"File '{file}' not found in the logs directory.");
            }

            List<LogParser.LogObject_ApacheAccess> logEntries = new();
            var _statusRange = status.Split('-').Select(s => int.Parse(s.Trim())).ToList();
            var statusRange = new Range(_statusRange[0], _statusRange[1]);

            var filter = new LogParser.RequestEntryFilters(statusRange, method);
            logEntries = _parser.parseApacheAccessLogFile(file, filter.ByMethod_ByStatus);

            var grouped = logEntries
                        .GroupBy(g => g.Url)
                        .Select(g => new { Url = g.Key, Count = g.Count() })
                        .ToList();
            if (logEntries.Count == 0)
                return NoContent();
            return Ok(grouped);
        }

        [HttpGet("parseAccessLog/get/groupBy/timeItervals/{timeBucketSize}/{method}/{status}/{file}")]
        public IActionResult GetGroupByTimeItervals(int timeBucketSize = 15, LogParser.HttpMethod method = LogParser.HttpMethod.ALL, string status = "400-600", string file = "")
        {
            if (string.IsNullOrEmpty(file))
                return BadRequest("File parameter is required.");
            else
            {
                file = Path.GetFullPath(file, _parser.LogsPath);
                if (!_parser.LogFiles.Contains(file))
                    return NotFound($"File '{file}' not found in the logs directory.");
            }

            List<LogParser.LogObject_ApacheAccess> logEntries = new();
            var _statusRange = status.Split('-').Select(s => int.Parse(s.Trim())).ToList();
            var statusRange = new Range(_statusRange[0], _statusRange[1]);

            var filter = new LogParser.RequestEntryFilters(statusRange, method);
            logEntries = _parser.parseApacheAccessLogFile(file, filter.ByMethod_ByStatus);

            var groupedByTime = logEntries.GroupBy(g =>
            {
                var ts = g.Time;
                ts = ts.AddMinutes(-(ts.Minute % timeBucketSize));
                ts = ts.AddMilliseconds(-ts.Millisecond - 1000 * ts.Second);
                return ts;
            })
            .Select(g => new { timeBucket = g.Key, value = g })
            .ToList();

            if (logEntries.Count == 0)
                return NoContent();
            return Ok(groupedByTime);
        }
    
        [HttpGet("parseAccessLog/unsafe/timeItervals/{timeBucketSize}/threshold/{unsafeRequestThreshold}/{method}/{status}/{file}")]
        public IActionResult GetUsafe(int timeBucketSize = 15, int unsafeRequestThreshold = 5, LogParser.HttpMethod method = LogParser.HttpMethod.ALL, string status = "400-600", string file = "")
        {
            if (string.IsNullOrEmpty(file))
                return BadRequest("File parameter is required.");
            else
            {
                file = Path.GetFullPath(file, _parser.LogsPath);
                if (!_parser.LogFiles.Contains(file))
                    return NotFound($"File '{file}' not found in the logs directory.");
            }

            List<LogParser.LogObject_ApacheAccess> logEntries = new();
            var _statusRange = status.Split('-').Select(s => int.Parse(s.Trim())).ToList();
            var statusRange = new Range(_statusRange[0], _statusRange[1]);

            var filter = new LogParser.RequestEntryFilters(statusRange, method);
            logEntries = _parser.parseApacheAccessLogFile(file, filter.ByMethod_ByStatus);

            var groupedEntries = logEntries
                .GroupBy(g =>
                {
                    // Group by time bucket
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
                        .GroupBy(g => g.Host) // Group by Host within each time bucket
                        .Select(hostGroup => new
                        {
                            Host = hostGroup.Key,
                            Urls = hostGroup
                                .GroupBy(g => g.Url) // Group by Url within each Host
                                .Where(urlGroup => urlGroup.Count() >= unsafeRequestThreshold) // Filter by threshold
                                .Select(urlGroup => new
                                {
                                    Url = urlGroup.Key,
                                    Count = urlGroup.Count() // Count of entries for each Url
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

                if (unsafeRequests.Count == 0)
                    return NoContent();
                return Ok(unsafeRequests);
            // var unsafePosts = groupedEntries
            //     .Where(a => a.Hosts
            //         .Any(h => h.Urls
            //             .Any(u => u.Count >= unsafeRequestThreshold)))
            //     .ToList();
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