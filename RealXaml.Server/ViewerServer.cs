using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AdMaiora.RealXaml.Server
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSignalR();
            services.AddMvc().SetCompatibilityVersion(Microsoft.AspNetCore.Mvc.CompatibilityVersion.Version_2_1);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(
            IApplicationBuilder app, 
            IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSignalR(routes =>
            {
                routes.MapHub<MessageHub>("/hub",
                    options =>
                    {
                        options.ApplicationMaxBufferSize = 1024 * 1024;
                    });
            });

            app.UseHttpsRedirection();
            app.UseMvc();
        }
    }

    public class ViewerServer : IDisposable
    {
        #region Constants and Fields

        private IWebHost _host;

        private CancellationTokenSource _cts;

        private bool _disposed = false;

        #endregion

        #region Public Methods

        public void Start()
        {
            _cts = new CancellationTokenSource();


            Task.Run(() =>
            {
                UdpClient server = new UdpClient(5002);
                byte[] responseData = Encoding.ASCII.GetBytes("YesIamTheServer!");
                while (!_cts.IsCancellationRequested)
                {
                    IPEndPoint clientEp = new IPEndPoint(IPAddress.Any, 0);
                    byte[] clientRequestData = server.Receive(ref clientEp);
                    string clientRequest = Encoding.ASCII.GetString(clientRequestData);

                    if (clientRequest.Contains("AreYouTheServer?"))
                    {                        
                        System.Diagnostics.Debug.WriteLine($"A new peer is connecting @ {clientEp.Address.ToString()}:{clientEp.Port}");
                        server.Send(responseData, responseData.Length, clientEp);
                    }
                }

            }, _cts.Token);
            
            _host = WebHost.CreateDefaultBuilder()
                .UseKestrel()               
                .UseUrls($"http://localhost:5001", $"http://{GetLocalIPAddress()}:5002")
                .UseStartup<Startup>()
                .Build();

            _host.Run();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);         
        }

        #endregion

        #region Methods

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }

            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

        private void Dispose(bool disposing)
        {
            if(!_disposed && disposing)
            {
                _cts?.Cancel();
            }

            _disposed = true;
        }

        #endregion
    }
}
