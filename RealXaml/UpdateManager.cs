using AdMaiora.RealXaml.Common;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading.Tasks;

namespace AdMaiora.RealXaml.Extension
{
    public class ClientNotificationEventArgs
    {
        #region Properties

        public string ClientId
        {
            get;
            private set;
        }

        #endregion

        #region Constructors

        public ClientNotificationEventArgs(string clientId)
        {
            this.ClientId = clientId;
        }

        #endregion
    }

    public class PageNotificationEventArgs : EventArgs
    {
        #region Properties

        public string PageId
        {
            get;
            private set;
        }
        

        public string Xaml
        {
            get;
            private set;
        }

        #endregion

        #region Construtor

        public PageNotificationEventArgs(string pageId)
            : this(pageId, null)
        {
        }

        public PageNotificationEventArgs(string pageId, string xaml)
        {
            this.PageId = pageId;
            this.Xaml = xaml;
        }
    
        #endregion  
    }

    public class AssemblyNotificationEventArgs : EventArgs
    {
        #region Properties

        public string AssemblyName
        {
            get;
            private set;
        }

        public string Version
        {
            get;
            private set;
        }

        #endregion

        #region Constructor

        public AssemblyNotificationEventArgs(string assemblyName, string version)
        {
            this.AssemblyName = assemblyName;
            this.Version = version;
        }

        #endregion
    }

    public class ExceptionNotificationEventArgs : EventArgs
    {
        #region Properties

        public string Message
        {
            get;
            private set;
        }

        #endregion

        #region Constructors

        public ExceptionNotificationEventArgs(string message)
        {
            this.Message = message;
        }
        
        #endregion
    }

    public class IdeNotificationEventArgs
    {
        #region Properties

        public string Message
        {
            get;
            private set;
        }

        #endregion

        #region Constructors

        public IdeNotificationEventArgs(string message)
        {
            this.Message = message;
        }

        #endregion
    }

    public class UpdateManager
    {
        #region Constants and Fields

        private HubConnection _hubConnection;

        private static Lazy<UpdateManager> _current = new Lazy<UpdateManager>();

        private readonly string _processDatFilePath;

        private Process _pServer;

        private Guid _ideId;

        #endregion

        #region Events

        public event EventHandler IdeRegistered;
        public event EventHandler<ClientNotificationEventArgs> ClientRegistered;
        public event EventHandler<PageNotificationEventArgs> PageAppeared;
        public event EventHandler<PageNotificationEventArgs> PageDisappeared;
        public event EventHandler<PageNotificationEventArgs> XamlUpdated;
        public event EventHandler<AssemblyNotificationEventArgs> AssemblyLoaded;
        public event EventHandler<ExceptionNotificationEventArgs> ExceptionThrown;
        public event EventHandler<IdeNotificationEventArgs> IdeNotified;

        #endregion

        #region Properties

        public static UpdateManager Current
        {
            get
            {
                return _current.Value;
            }
        }

        public bool IsConnected
        {
            get
            {
                return _hubConnection.State == HubConnectionState.Connected;
            }
        }

        #endregion

        #region Constructor

        public UpdateManager()
        {
            _processDatFilePath = 
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"process.dat");
        }

        #endregion

        #region Public Methods

        public async Task StartAsync()
        {
            // Kill any previous running processes
            KillRunningProcesses();

            string serverAssemblyPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Server\RealXamlServer.dll");
            if (File.Exists(serverAssemblyPath))
            {
                ProcessStartInfo psiServer = new ProcessStartInfo("dotnet");
                psiServer.Arguments = $"\"{serverAssemblyPath}\"";
                psiServer.Verb = "runas";
                //psiServer.CreateNoWindow = true;
                //psiServer.WindowStyle = ProcessWindowStyle.Hidden;
                _pServer = Process.Start(psiServer);
                System.Threading.Thread.Sleep(1500);
            }

            if (_pServer == null)
                throw new InvalidOperationException("Unable to start RealXaml server.");

            if (File.Exists(_processDatFilePath))
                File.Delete(_processDatFilePath);

            using (var s = File.CreateText(_processDatFilePath))
            {
                await s.WriteAsync(_pServer.Id.ToString());
            }

            string ipAddress = NetworkHelper.GetLocalIPAddress();
            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"http://localhost:5001/hub")
                .Build();

            await _hubConnection.StartAsync();
            if (_hubConnection.State != HubConnectionState.Connected)
            {
                _hubConnection = null;
                throw new InvalidOperationException("Unable to connect.");
            }

            _hubConnection.On("HelloIde", WhenHelloIde);
            _hubConnection.On("ByeIde", WhenByeIde);
            _hubConnection.On<string>("ClientRegistered", WhenClientRegistered);
            _hubConnection.On<string, byte[]>("XamlReceived", WhenXamlReceived);
            _hubConnection.On<string, string>("AssemblyReceived", WhenAssemblyReceived);
            _hubConnection.On<string>("PageAppeared", WhenPageAppeared);
            _hubConnection.On<string>("PageDisappeared", WhenPageDisappeared);
            _hubConnection.On<string>("ExceptionThrown", WhenExceptionThrown);
            _hubConnection.On<string>("IdeNotified", WhenIdeNotified);


            _ideId = Guid.NewGuid();
            await _hubConnection.SendAsync("RegisterIde", _ideId.ToString());
        }

        public async Task StopAsync()
        {
            if (_hubConnection != null)
            {                
                if (_hubConnection.State == HubConnectionState.Connected)
                {
                    await _hubConnection.SendAsync("DisconnectIde", _ideId.ToString());

                    await Task.Delay(300);
                    await _hubConnection.StopAsync();
                }
            }

            foreach (var p in new[] { _pServer })
            {
                if (p is null)
                    continue;

                if (p.HasExited)
                    continue;

                p.Kill();
                p.Close();
                p.Dispose();
            }

            _pServer = null;

            // Ensure processes are killed
            KillRunningProcesses();
        }

        public async Task SendXamlAsync(string pageId, string xaml, bool refresh)
        {
            byte[] data = CompressXaml(xaml);
            await _hubConnection.SendAsync("SendXaml", pageId, data, refresh);
        }

        public async Task SendAssemblyAsync(string assemblyName, byte[] data)
        {
            data = CompressData(data);
            await _hubConnection.SendAsync("SendAssembly", assemblyName, data);
        }

        #endregion

        #region Methods

        #endregion

        #region SignalR Hub Callback Methods

        private void WhenHelloIde()
        {
            IdeRegistered?.Invoke(this, EventArgs.Empty);
       }

        private void WhenByeIde()
        {
            IdeNotified?.Invoke(this, new IdeNotificationEventArgs("RealXaml is not running anymore."));
        }

        private void WhenClientRegistered(string clientId)
        {
            ClientRegistered?.Invoke(this, new ClientNotificationEventArgs(clientId));
        }

        private void WhenXamlReceived(string pageId, byte[] data)
        {
            string xaml = DecompressXaml(data);
            XamlUpdated?.Invoke(this, new PageNotificationEventArgs(pageId, xaml));
        }

        private void WhenAssemblyReceived(string assemblyName, string version)
        {
            AssemblyLoaded?.Invoke(this, new AssemblyNotificationEventArgs(assemblyName, version));
        }

        private void WhenPageAppeared(string pageId)
        {
            PageAppeared?.Invoke(this, new PageNotificationEventArgs(pageId));
        }

        private void WhenPageDisappeared(string pageId)
        {
            PageDisappeared?.Invoke(this, new PageNotificationEventArgs(pageId));
        }

        private void WhenExceptionThrown(string message)
        {
            ExceptionThrown?.Invoke(this, new ExceptionNotificationEventArgs(message));
        }

        private void WhenIdeNotified(string message)
        {
            IdeNotified?.Invoke(this, new IdeNotificationEventArgs(message));
        }

        #endregion

        #region Methods

        private byte[] CompressData(byte[] data)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(memory, CompressionLevel.Fastest))
                {
                    gzip.Write(data, 0, data.Length);
                }

                return memory.ToArray();
            }
        }

        private byte[] CompressXaml(string xaml)
        {            
            return CompressData(Encoding.ASCII.GetBytes(xaml));
        }

        private Byte[] DecompressData(byte[] data)
        {
            // Create a GZIP stream with decompression mode.
            // ... Then create a buffer and write into while reading from the GZIP stream.
            using (GZipStream stream = new GZipStream(new MemoryStream(data),
                CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            memory.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return memory.ToArray();
                }
            }
        }

        private string DecompressXaml(byte[] data)
        {
            return Encoding.ASCII.GetString(DecompressData(data));
        }

        private void KillRunningProcesses()
        {
            if (File.Exists(_processDatFilePath))
            {
                using (var s = File.OpenText(_processDatFilePath))
                {
                    int processId = 0;

                    try
                    {
                        while(!s.EndOfStream)
                        {
                            processId = Int32.Parse(s.ReadLine());
                            if (processId == 0)
                                continue;

                            var process = Process.GetProcessById(processId);
                            if (process != null && !process.HasExited)
                                process.Kill();
                        }
                    }
                    catch (Exception ex)
                    {                       
                        if (processId != 0)
                            System.Diagnostics.Debug.WriteLine($"RealXaml was unable to kill process with id {processId}");

                        System.Diagnostics.Debug.WriteLine(ex);
                    }
                }
            }
        }

        #endregion
    }
}
