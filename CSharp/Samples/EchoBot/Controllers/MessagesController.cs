﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Connector.Utilities;
using Newtonsoft.Json;
using Microsoft.Bot.Builder;

namespace Microsoft.Bot.Sample.EchoBot
{
    [BotAuthentication]
    public class MessagesController : ApiController
    {
        /// <summary>
        /// POST: api/Messages
        /// receive a message from a user and reply to it
        /// </summary>
        [ResponseType(typeof(Message))]
        public async Task<HttpResponseMessage> Post([FromBody]Message message)
        {
            var echoDialog = EchoDialog.Instance;
            var echoCommandDialog = EchoCommandDialog.Instance;
            var dialogs = new DialogCollection().Add(echoDialog).Add(echoCommandDialog);
            return await ConnectorSession.MessageReceivedAsync(Request, message, dialogs, echoCommandDialog);
        }
    }
}
