namespace tikkaman.apacheLogParser.CLI;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using System.Text.Json.Serialization;

public class LogParser
{

    // Define an enum for HTTP methods
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HttpMethod
    {
        GET,
        POST,
        PUT,
        DELETE,
        PATCH,
        HEAD,
        OPTIONS,
        TRACE,
        CONNECT,
        UNKNOWN // For unsupported or unrecognized methods
        , ALL
    }

    public class LogObject_ApacheAccess
    {
        public string Host { get; set; } = "";
        public int Pid { get; set; }
        public string User { get; set; } = "";
        public DateTime Time { get; set; }
        public HttpMethod Method { get; set; } = HttpMethod.UNKNOWN;
        public string Url { get; set; } = "";
        public int Status { get; set; }
        public int Size { get; set; }
        public string Referer { get; set; } = "";
        public string UserAgent { get; set; } = "";
        public override string ToString()
        {
            return $"{Host} - {User} [{Time}] \"{Method} {Url}\" {Status} {Size} \"{Referer}\" \"{UserAgent}\"";
        }
    }
    
    public class RequestEntryFilters
    {
        public Range Status { get; private set; }
        public HttpMethod Method { get; private set; }

        public RequestEntryFilters() { }
        public RequestEntryFilters(Range status, HttpMethod method)
        {
            Status = status;
            Method = method;
        }
        public static bool All(LogObject_ApacheAccess logEntry)
        {
            return true;
        }
        public static bool All_Successful(LogObject_ApacheAccess logEntry)
        {
            return logEntry.Status < 400;
        }
        public static bool All_Failed(LogObject_ApacheAccess logEntry)
        {
            return logEntry.Status >= 400;
        }
        public bool ByMethod_ByStatus(LogObject_ApacheAccess logEntry)
        {
            if(this.Method == HttpMethod.UNKNOWN)
                throw new ArgumentException($"Invalid filter instruction - unsupported request method: '{this.Method}'");
            if(this.Method == HttpMethod.ALL)
                return logEntry.Status >= this.Status.Start.Value && logEntry.Status < this.Status.End.Value;

            return logEntry.Status >= this.Status.Start.Value && logEntry.Status < this.Status.End.Value && logEntry.Method == this.Method;
        }        
    }

    private static List<string> logExtensions =  new(){".log", ".xxx"};
    public string LogsPath {get; private set;}
    public List<string> LogFiles {get; private set;}

    public LogParser(string path = "")
    {
        if (string.IsNullOrEmpty(path)) 
            LogsPath = Directory.GetCurrentDirectory();
        else 
                LogsPath = Path.GetFullPath(path);
        
        LogFiles = findLogFiles(LogsPath);
        Console.WriteLine($"Logs in {LogsPath}: \n{string.Join("\n\t- ", LogFiles)}");
    }
    public List<string> findLogFiles(string path, bool setParser = false)
    {
        List<string> files = new();
        foreach (string file in Directory.GetFiles(Path.GetFullPath(path)))
        {
            string ext = Path.GetExtension(file);
            if (logExtensions.Contains(ext))
            {
                files.Add(file);
            }
        }

        if (setParser) LogsPath = path;

        Console.WriteLine($"Log files in [{path}]: \n\t - {string.Join("\n\t - ", files)}");
        return files;
    }
    public List<LogObject_ApacheAccess> parseApacheAccessLogFile(string file, Func<LogObject_ApacheAccess, bool>? funcFilter = null, List< bool>? funcOut = null, string logFormat = @"^(?<ip>(?:[0-9]{1,3}\.){3}[0-9]{1,3})+ <<(?<pid>\d+)>> \[(?<time>.*) (?<timeOffset>[+-]\d+)\] (?<usTime>\d+us) ""(?<method>[A-Z]+) (?<url>.*) (?<version>HTTP\W?/\d\.\d)"" (?<status>\d+) (?<size>\d+)? ""(?<referer>.*)"" ""(?<agent>.*)"" - -$")
    {
        // Should use apache_log_parser of some kind that is aligned to standard apache formats for more universal approach 
        // otherwise parse using RegEx
        var rxApacheAccess = new Regex(logFormat);
        
        List<LogObject_ApacheAccess> logEntries = new();
        using (StreamReader reader = new(Path.GetFullPath(file)))
        {
            string? line;
            int lineNum = 0;
            while ((line = reader.ReadLine()) is not null)
            {
                lineNum++;
                var match = rxApacheAccess.Match(line);

                LogObject_ApacheAccess data;
                if(match.Success)
                {
                    var offset = int.Parse(match.Groups["timeOffset"].Value); 
                    data = new LogObject_ApacheAccess
                    {
                        Host = match.Groups["ip"].Value,
                        Pid = int.Parse(match.Groups["pid"].Value),
                        User = match.Groups["user"].Value,
                        Time = DateTime.ParseExact(match.Groups["time"].Value, "dd/MMM/yyyy:HH:mm:ss", null),
                        Method = Enum.TryParse<HttpMethod>(match.Groups["method"].Value, out var method) ? method : HttpMethod.UNKNOWN,
                        Url = match.Groups["url"].Value,
                        Status = int.Parse(match.Groups["status"].Value),
                        Size = int.Parse(match.Groups["size"].Value),
                        Referer = match.Groups["referer"].Value,
                        UserAgent = match.Groups["agent"].Value
                    };
                    data.Time = data.Time.AddHours(offset); // Adjust for timezone if needed
                }
                else
                {
                    var msg = $"Line {lineNum} in {file} does not match the expected format.";
                    Console.WriteLine(msg);
                    throw new InvalidDataException(msg);
                    // continue; 
                }
            
                if (funcFilter != null)
                {
                    bool resFilter = funcFilter(data);
                    if (funcOut != null) 
                        funcOut.Add(resFilter);
                    if (resFilter)
                        logEntries.Add(data);
                }
                else
                {
                    logEntries.Add(data);
                }
            }
        }
        return logEntries;
    }
    
    public static void Main(string[] args)
    {
        LogParser parser = new LogParser("_logs");
        List<string> logFiles = new List<string>(){Path.GetFullPath("access3hFailes3RPM.log", parser.LogsPath)};
        foreach (string logFile in parser.LogFiles)//logFiles)//
        {
            List<LogObject_ApacheAccess> postsSuccessful = parser.parseApacheAccessLogFile(logFile, RequestEntryFilters.All_Successful);

            var Q1 = postsSuccessful.Count();
            Console.WriteLine($"\n\n\n***** Q1. Successful POSTs in [{logFile}]: {Q1}\n");

            var Q2 = postsSuccessful.GroupBy(g => g.Url).Select(g => new { url = g.Key, count = g.Count()}).ToList(); 
            Console.WriteLine($"\n\n\n***** Q2. Successful POST' size per URL in [{logFile}]: \n\t-{string.Join("\n\t-", Q2)}\n");

            List<LogObject_ApacheAccess> postsFailed = parser.parseApacheAccessLogFile(logFile, RequestEntryFilters.All_Failed);
            var timeBucketSize = 15;
            var Q3 = postsFailed.GroupBy(g =>
            {
                var ts = g.Time;
                ts = ts.AddMinutes(-(ts.Minute % timeBucketSize));
                ts = ts.AddMilliseconds(-ts.Millisecond - 1000 * ts.Second);
                return ts;
            })
            .Select(g => new { timeBucket = g.Key, value = g })
            .ToList();
            
            var groupedPostsFailed = postsFailed
                .GroupBy(g =>
                {
                    // Group by time bucket
                    var ts = g.Time;
                    ts = ts.AddMinutes(-(ts.Minute % timeBucketSize));
                    ts = ts.AddMilliseconds(-ts.Millisecond - 1000 * ts.Second);
                    // ts = ts.AddMinutes(-ts.Minute - 1 * ts.Second);
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

            int unsafeRequestThreshold = 3;

            var unsafePosts = groupedPostsFailed
                .Where(a => a.Hosts
                    .Any(h => h.Urls
                        .Any(u => u.Count >= unsafeRequestThreshold)))
                .ToList();
            var unsafePostsFlatList = unsafePosts
                .SelectMany(a => a.Hosts
                    .SelectMany(h => h.Urls
                        .Where(u => u.Count >= unsafeRequestThreshold)
                            .Select(u => new {
                                TimeBucket = a.TimeBucket, 
                                Host = h.Host, 
                                Url = u.Url, 
                                Count = u.Count,
                                _data = u })))
                .ToList();
            // Example output - unsafe posts
            Console.WriteLine($"\n\n\n***** Q3 & 4 unsafePosts in [{logFile}] *****\n");
            foreach (var timeGroup in unsafePosts)
            {
                Console.WriteLine($"Time Bucket: {timeGroup.TimeBucket}");
                foreach (var hostGroup in timeGroup.Hosts)
                {
                    Console.WriteLine($"\tHost: {hostGroup.Host}");
                    foreach (var urlGroup in hostGroup.Urls)
                    {
                        Console.WriteLine($"\t\tUrl: {urlGroup.Url}, Count: {urlGroup.Count}");

                        if (urlGroup.Count >= unsafeRequestThreshold)
                        {
                            Console.WriteLine($"\t\t\t*** Unsafe Request: {urlGroup.Url}, Count: {urlGroup.Count}");
                        }
                    }
                }
            }

            

            var filteredPostsList = new List<List<object>>();
            var unsafePostList = new List<List<object>>();
            // Example output
            foreach (var timeGroup in groupedPostsFailed)
            {
                Console.WriteLine($"Time Bucket: {timeGroup.TimeBucket}");
                foreach (var hostGroup in timeGroup.Hosts)
                {
                    Console.WriteLine($"\tHost: {hostGroup.Host}");
                    foreach (var urlGroup in hostGroup.Urls)
                    {
                        Console.WriteLine($"\t\tUrl: {urlGroup.Url}, Count: {urlGroup.Count}");
                        filteredPostsList.Add(new List<object>(){timeGroup.TimeBucket, hostGroup.Host, urlGroup.Url, urlGroup.Count});
                        if (urlGroup.Count >= unsafeRequestThreshold)
                        {
                            unsafePostList.Add(new List<object>(){timeGroup.TimeBucket, hostGroup.Host, urlGroup.Url, urlGroup.Count});
                            Console.WriteLine($"\t\t\tUnsafe Request: {urlGroup.Url}, Count: {urlGroup.Count}");
                        }
                    }
                }
            }
        }
    }
}