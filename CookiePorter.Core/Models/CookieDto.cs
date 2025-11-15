using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CookiePorter.Core.Models
{
    public sealed class CookieDto
    {
        public string Name { get; init; } = "";
        public string Value { get; init; } = "";
        public string Domain { get; init; } = "";
        public string Path { get; init; } = "/";
        public bool HttpOnly { get; init; }
        public bool Secure { get; init; }
        public long ExpiresUtcChrome { get; init; } // microseconds since 1601-01-01
    }
}
