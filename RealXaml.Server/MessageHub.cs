using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdMaiora.RealXaml.Server
{
    public class MessageHub : Hub
    {
        #region Hub Methods for Ide Client

        public async Task RegisterIde(string ideId)
        {
            await this.Clients.Caller.SendAsync("HelloIde");
        }

        public async Task DisconnectIde(string ideId)
        {
            await this.Clients.Caller.SendAsync("ByeIde");
        }

        public async Task SendXaml(string pageId, byte[] data, bool refresh)
        {
            await this.Clients.All.SendAsync("ReloadXaml", pageId, data, refresh);
        }

        public async Task SendAssembly(string assemblyName, byte[] data)
        {
            await this.Clients.All.SendAsync("ReloadAssembly", assemblyName, data);
        }


        #endregion

        #region Hub Methods for App Client

        public async Task RegisterClient(string clientId)
        {
            await this.Clients.All.SendAsync("ClientRegistered", clientId);
        }

        public async Task PageAppearing(string pageId)
        {
            await this.Clients.All.SendAsync("PageAppeared", pageId);
        }

        public async Task PageDisappearing(string pageId)
        {
            await this.Clients.All.SendAsync("PageDisappeared", pageId);
        }

        public async Task XamlReloaded(string pageId, byte[] data)
        {
            await this.Clients.All.SendAsync("XamlReceived", pageId, data);
        }

        public async Task AssemblyReloaded(string assemblyName, string version)
        {
            await this.Clients.All.SendAsync("AssemblyReceived", assemblyName, version);
        }

        public async Task NotifyIde(string message)
        {
            await this.Clients.All.SendAsync("IdeNotified", message);
        }

        public async Task ThrowException(string message)
        {
            await this.Clients.All.SendAsync("ExceptionThrown", message);
        }

        #endregion
    }
}
