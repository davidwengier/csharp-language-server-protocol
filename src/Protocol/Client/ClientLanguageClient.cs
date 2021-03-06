using System;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Progress;

namespace OmniSharp.Extensions.LanguageServer.Protocol.Client
{
    internal class ClientLanguageClient : LanguageProtocolProxy, IClientLanguageClient
    {
        public ClientLanguageClient(
            IResponseRouter requestRouter, IServiceProvider serviceProvider, IProgressManager progressManager,
            ILanguageProtocolSettings languageProtocolSettings
        ) : base(requestRouter, serviceProvider, progressManager, languageProtocolSettings)
        {
        }
    }
}
