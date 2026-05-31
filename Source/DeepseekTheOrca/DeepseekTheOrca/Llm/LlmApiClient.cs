using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DeepseekTheOrca
{
    public sealed partial class LlmApiClient
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan StreamingTimeout = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan DefaultChatTimeout = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan ToolChatTimeout = TimeSpan.FromSeconds(60);
        private const int MaxTransportAttempts = 2;

        private static TimeSpan ChatTimeoutForRole(OrcaLlmModelRole role)
        {
            if (role == OrcaLlmModelRole.Tool || role == OrcaLlmModelRole.WebSearch || role == OrcaLlmModelRole.Vision)
            {
                return ToolChatTimeout;
            }

            return DefaultChatTimeout;
        }

    }

}
