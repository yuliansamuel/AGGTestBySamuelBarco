namespace Agg.Test.DTOs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Net.Http;
    public class HttpRequestDto
    {
        public string Endpoint { get; set; }

        public HttpMethod Method { get; set; }

        public object Body { get; set; }

        public string Authorization { get; set; }
    }
}
