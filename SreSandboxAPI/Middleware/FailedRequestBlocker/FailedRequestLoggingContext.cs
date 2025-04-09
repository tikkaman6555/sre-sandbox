namespace tikkaman.sreSandbox.Middleware
{

    using Microsoft.EntityFrameworkCore;
    using System.ComponentModel.DataAnnotations;

    using System.Text.Json.Serialization;

    public class RequestBlockingEnabledConfig
    {
        public RequestBlockingEnabledConfig(bool enabled = false)
        {
            Enabled = enabled;
            DefaultFailedRequestBlockRule = new FailedRequestBlockRule();
        }

        public FailedRequestBlockRule DefaultFailedRequestBlockRule;
        public bool Enabled { get; }
    }
    public class RequestCount
    {
        [Key]
        public int id { get; set; }
        public int Count { get; set; } = 0;
        public string IP { get; set; } = "*";
        public HttpMethod Method { get; set; } = HttpMethod.ALL;
        public string Path { get; set; } = "*";
        public int Status { get; set; } = 0;
        public DateTime Timestamp { get; set; }

        public string RuleName { get; set; } = "NOT_SET";

        public override string ToString()
        {
            return $"RequestCount: {id} - {Count} - {IP} - {Method} - {Path} - {Status} - {Timestamp}";
        }

        public RequestCount() { }
    }
    public class FailedRequestBlockRule
    {
        [Key]
        public int id { get; set; }

        public string Name { get; set; } = "Default Rule - Class";
        public int Threshold { get; set; } = 5;
        public List<string> IP { get; set; } = new() { "*" };
        public List<HttpMethod> MethodsMatch { get; set; } = new() { HttpMethod.POST };
        public List<HttpMethod> MethodsBlock { get; set; } = new() { HttpMethod.POST };
        public List<string> PathMatch { get; set; } = new();
        public string PathBlock { get; set; } = "*";
        public List<int> Status { get; set; } = new();
        public TimeSpan Period { get; set; } = TimeSpan.FromMinutes(1);
        public TimeSpan ForgetAfter { get; set; } = TimeSpan.FromMinutes(5);
        public int ResponseStatus { get; set; } = 429;
        public string ResponseMessage { get; set; } = $"Too Many Requests within the time period.";

        public FailedRequestBlockRule() { ResponseMessage = $"Too Many Requests within the {Period.TotalMinutes}m period."; }
        public FailedRequestBlockRule(int threshold, List<string> ip, List<HttpMethod> method, List<HttpMethod> methodBlock, List<string> pathMatch, string pathBlock, List<int> status, TimeSpan period)
        {
            Threshold = threshold;
            IP = ip;
            MethodsMatch = method;
            MethodsBlock = methodBlock;
            PathMatch = pathMatch;
            PathBlock = pathBlock;
            Status = status;
            Period = period;
            ResponseMessage = $"Too Many Requests within the {this.Period.TotalMinutes}m period.";
        }
    }

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

    public class FailedRequestLoggingContext : DbContext
    {
        public DbSet<RequestCount> Requests { get; set; }
        public DbSet<FailedRequestBlockRule> FailedRequestBlockingRules { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseInMemoryDatabase("FailedRequestLoggingDB");
            optionsBuilder.UseInMemoryDatabase("FailedRequestBlockingRulesDB");
        }
    }

}