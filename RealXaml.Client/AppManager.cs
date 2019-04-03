using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using Microsoft.AspNetCore.SignalR.Client;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Xamarin.Forms.Internals;
using System.IO;
using System.IO.Compression;

namespace AdMaiora.RealXaml.Client
{
    public sealed class AppManager
    {
        #region Constants and Fields

        private static Lazy<AppManager> _current = new Lazy<AppManager>();

        private Action _init;

        private WeakReference<Application> _app;
        private Dictionary<string, WeakReference> _pages;        
        
        private string _serverAddress;

        private bool _isConnected;
        private bool _useLocalHost;

        private TaskCompletionSource<bool> _connectionTCS;

        private HubConnection _hubConnection;

        private Dictionary<string, byte[]> _xamlCache;

        #endregion

        #region Properties

        internal static AppManager Current
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
                return _isConnected;
            }
        }

        #endregion

        #region Constructors

        public AppManager()
        {
            _pages = new Dictionary<string, WeakReference>();
            _xamlCache = new Dictionary<string, byte[]>();
        }

        #endregion

        #region Public Methods

        public static void Init(Application application)
        {
            AppManager.Current.Setup(application);
        }

        public static void Init(Page page)
        {
            AppManager.Current.Setup(page);
        }

        public static void Debug(string message)
        {
            Task.Run(async () => 
                await AppManager.Current.OutputDebugMessage(message));
        }

        internal void Setup(Application application)
        {
            if (application == null)
                throw new ArgumentNullException("application");

            AppDomain.CurrentDomain.UnhandledException += this.CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += this.TaskScheduler_UnobservedTaskException;

            _app = new WeakReference<Application>(application);

            application.PageAppearing += Application_PageAppearing;
            application.PageDisappearing += Application_PageDisappearing;

            _connectionTCS = new TaskCompletionSource<bool>();
            Task.Run(
                async () =>
                {
                    try
                    {                        
                        await ConnectAsync();
                        if (_isConnected)
                        {
                            // Connection successfully estabilished
                            _connectionTCS.SetResult(_isConnected);
                        }
                        else
                        {
                            // Unable to connect, we should retry later
                            _connectionTCS.SetResult(_isConnected);
                            System.Diagnostics.Debug.WriteLine("Unable to connect to the RealXaml server.");

                            while (true)
                            {
                                System.Diagnostics.Debug.WriteLine("Trying to reconnect again...");
                                await ConnectAsync();
                                if (_isConnected)
                                {                                    
                                    break;
                                }

                                System.Diagnostics.Debug.WriteLine("Unable to connect. Retrying in 5secs.");
                                await Task.Delay(5000);                                
                            }
                        }
                    }
                    catch(Exception ex)
                    {                        
                        _connectionTCS.SetException(ex);
                    }
                });
        }

        internal void Setup(Page page)
        {
            string pageId = page.GetType().FullName;

            byte[] data = null;
            if(_xamlCache.TryGetValue(pageId, out data))
                Task.Run(async () => await ReloadXaml(page, data));
        }

        internal async Task OutputDebugMessage(string message)
        {
            if (_isConnected)
                await _hubConnection.SendAsync("ThrowException", message);
        }

        internal async Task MonitorExceptionAsync(Exception exception)
        {
            if (_isConnected)
                await _hubConnection.SendAsync("ThrowException", exception.ToString());
        }

        #endregion

        #region Methods

        private async Task ConnectAsync()
        {
            if (_isConnected)
                return;

            // Emulators loopback addresses
            IPAddress[] loopbackAddresses = new[]
            {
                IPAddress.Parse("10.0.2.2"),
                IPAddress.Parse("10.0.3.2"),
                IPAddress.Parse("169.254.80.80")
            };

            // Check if we are an emulator instance
            List<Task<string>> waitTasks = new List<Task<string>>();
            CancellationTokenSource cts = new CancellationTokenSource();

            // Look for server using localhost (an emulator device)
            foreach (var ipAddress in loopbackAddresses.Take(1))
            {
                waitTasks.Add(Task.Run<string>(
                    async () =>
                    {
                        try
                        {
                            bool isPortOpen = TryPing(ipAddress.ToString(), 5001, 300);
                            if (!isPortOpen)
                                return null;

                            var connection = new HubConnectionBuilder()
                                .WithUrl($"http://{ipAddress.ToString()}:5001/hub")
                                .Build();

                            await connection.StartAsync(cts.Token);
                            if (cts.IsCancellationRequested)
                                return null;

                            _useLocalHost = true;
                            _hubConnection = connection;

                            cts.Cancel();
                            return ipAddress.ToString();
                        }
                        catch (Exception ex)
                        {
                            return null;
                        }

                    }, cts.Token));
            }

            // Look for server using broadcast (a real device)
            waitTasks.Add(Task.Run<string>(
                async () =>
                {
                    // Discover the server
                    using (UdpClient client = new UdpClient())
                    {
                        client.EnableBroadcast = true;

                        byte[] requestData = Encoding.ASCII.GetBytes($"AreYouTheServer?");
                        Task<int> sendTask = client.SendAsync(requestData, requestData.Length, new IPEndPoint(IPAddress.Broadcast, 5002));
                        await Task.WhenAny(new[] { sendTask, Task.Delay(300) });
                        if (sendTask.IsCompleted)
                        {
                            if (cts.IsCancellationRequested)
                                return null;

                            Task<UdpReceiveResult> receiveTask = client.ReceiveAsync();
                            await Task.WhenAny(new[] { receiveTask, Task.Delay(300) });
                            if (receiveTask.IsCompleted)
                            {
                                if (cts.IsCancellationRequested)
                                    return null;

                                UdpReceiveResult serverResponseData = receiveTask.Result;
                                string serverResponse = Encoding.ASCII.GetString(serverResponseData.Buffer);
                                if (serverResponse == "YesIamTheServer!")
                                {
                                    string ipAddress = serverResponseData.RemoteEndPoint.Address.ToString();
                                    _useLocalHost = false;
                                    _hubConnection = null;

                                    cts.Cancel();
                                    return ipAddress.ToString();

                                }
                            }
                        }

                        client.Close();
                    }

                    return null;
                }));

            // Timeout task 
            waitTasks.Add(Task.Run<string>(
                async () =>
                {
                    try
                    {
                        await Task.Delay(5000, cts.Token);
                        cts.Cancel();
                        return null;
                    }
                    catch
                    {
                        return null;
                    }
                }));

            try
            {
                string ipAddress = await WaitForAnyGetHostIpTaskAsync(waitTasks);
                if (ipAddress != null)
                {
                    if (_hubConnection == null)
                    {
                        string port = _useLocalHost ? "5001" : "5002";
                        _hubConnection = new HubConnectionBuilder()
                            .WithUrl($"http://{ipAddress.ToString()}:{port}/hub")
                            .Build();

                        await _hubConnection.StartAsync();
                    }

                    _isConnected = true;
                    _serverAddress = ipAddress;

                    _hubConnection.Closed +=
                        async (error) =>
                        {
                            System.Diagnostics.Debug.WriteLine("Connection with RealXaml has been lost.");                            

                            while(_hubConnection.State == HubConnectionState.Disconnected)
                            {
                                bool isPortOpen = TryPing(ipAddress.ToString(), 5001, 300);
                                if (isPortOpen)
                                {
                                    System.Diagnostics.Debug.WriteLine("Trying to reconnect again...");
                                    await _hubConnection.StartAsync();
                                    if (_hubConnection.State == HubConnectionState.Connected)
                                    {
                                        await Task.Delay(300);
                                        await _hubConnection.SendAsync("NotifyIde", "Connection was lost. Here I'am again.");

                                        System.Diagnostics.Debug.WriteLine($"Successfully restored lost to the RealXaml server.");
                                        break;
                                    }
                                }

                                System.Diagnostics.Debug.WriteLine("Unable to connect. Retrying in 5secs.");
                                await Task.Delay(5000);
                            }
                        };                    

                    _hubConnection.On<string, byte[], bool>("ReloadXaml", 
                        async (pageId, data, refresh) => await WhenReloadXaml(pageId, data, refresh));

                    _hubConnection.On<string, byte[]>("ReloadAssembly", 
                        async (assemblyName, data) => await WhenReloadAssembly(assemblyName, data));

                    string clientId = $"RXID-{DateTime.Now.Ticks}";
                    await _hubConnection.SendAsync("RegisterClient", clientId);

                    System.Diagnostics.Debug.WriteLine($"Successfully connected to the RealXaml server.");
                    System.Diagnostics.Debug.WriteLine($"Your client ID is {clientId}");

                    return;
                }
            }
            catch(Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error while trying to connect to the RealXaml server.");
                System.Diagnostics.Debug.WriteLine(ex);
            }       
        }

        private async Task<string> WaitForAnyGetHostIpTaskAsync(IEnumerable<Task<string>> tasks)
        {
            IList<Task<string>> customTasks = tasks.ToList();
            Task<string> completedTask;
            string ipAddress = null;

            do
            {
                completedTask = await Task.WhenAny(customTasks);
                ipAddress = completedTask.Result;
                customTasks.Remove(completedTask);

            } while (ipAddress == null && customTasks.Count > 0);

            return ipAddress;
        }

        private async Task ReloadXaml(Page page, byte[] data = null)
        {
            try
            {
                Type pageType = page.GetType();
                MethodInfo configureAfterLoadMethodInfo =
                    pageType.GetMethods(
                      BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Static
                    | BindingFlags.Instance
                    | BindingFlags.InvokeMethod)
                    .Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(RunAfterXamlLoadAttribute)))
                    .SingleOrDefault();

                bool useAsyncConfigureAfterLoad = (configureAfterLoadMethodInfo?.CustomAttributes
                    .Any(x => x.AttributeType == typeof(AsyncStateMachineAttribute))).GetValueOrDefault(false);

                Device.BeginInvokeOnMainThread(
                    async () =>
                    {
                        try
                        {                            
                            // We need to clear tool bar items
                            // because reloading the xaml seems
                            // not resetting the buttons
                            page.ToolbarItems?.Clear();

                            // Reload the xaml!
                            page.Resources.Clear();
                            if (data != null)
                            {
                                string xaml = DecompressXaml(data);
                                page.LoadFromXaml(xaml);
                            }
                            else
                            {
                                string pageId = page.GetType().FullName;
                                if (_xamlCache.TryGetValue(pageId, out data))
                                {
                                    string xaml = DecompressXaml(data);
                                    page.LoadFromXaml(xaml);
                                }
                            }

                            // Configure after xaml reload
                            // This is usally needed when some data is binded via code                        
                            if (useAsyncConfigureAfterLoad)
                            {
                                await (Task)configureAfterLoadMethodInfo?.Invoke(page, null);
                            }
                            else
                            {
                                configureAfterLoadMethodInfo?.Invoke(page, null);
                            }

                            // Notify that the xaml was loaded correctly
                            await _hubConnection.SendAsync("XamlReloaded", page.GetType().FullName, data);

                            System.Diagnostics.Debug.WriteLine($"Page '{page.GetType().FullName}' received a new xaml.");
                        }
                        catch (Exception ex)
                        {
                            // Notify that something went wrong
                            await _hubConnection.SendAsync("ThrowException", ex.ToString());

                            System.Diagnostics.Debug.WriteLine($"Unable to update the xaml for page '{page.GetType().FullName}'");
                            System.Diagnostics.Debug.WriteLine(ex);
                        }
                    });
            }
            catch (Exception ex)
            {
                // Notify that something went wrong
                await _hubConnection.SendAsync("ThrowException", ex.ToString());

                System.Diagnostics.Debug.WriteLine($"Unable to update the xaml for page '{page.GetType().FullName}'");
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private async Task ReloadAssmbly(string assemblyName, byte[] data)
        {
            try
            {
                data = DecompressData(data);
                Assembly assembly = Assembly.Load(data);

                /*
                 * We use the two attributes MainPage and RootPage to let RealXaml know
                 * how he need to restart our application on assembly reload. 
                 * Different scenario are possibile:
                 * 
                 * At least we need to define one MainPage (for single page, no navigation)
                 * or we need to define one RootPage (for multi page with navigation). 
                 * When defining only a RootPage, a NavigationPage will be used as MainPage.
                 * 
                 * We can use them both to specify which class will be used as MainPage and RootPage.
                 * Using them togheter means that your custom MainPage needs to have a RootPage specified in the constructor.
                 * 
                 */

                Type mainPageType = assembly.GetTypes()
                    .Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(MainPageAttribute))).FirstOrDefault();

                Type rootPageType = assembly.GetTypes()
                    .Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(RootPageAttribute))).FirstOrDefault();

                if (mainPageType == null && rootPageType == null)
                    throw new InvalidOperationException("Unable to create a new MainPage. Did you mark a page with the [MainPage] or the [RootPage] attribute? ");

                Application app = null;
                if (_app.TryGetTarget(out app))
                {
                    Device.BeginInvokeOnMainThread(
                        async () =>
                        {
                            try
                            {
                                Page rootPage = null;

                                // In case of single page, no navigation
                                if(mainPageType != null 
                                    && rootPageType == null)
                                {
                                    // Create the new main page
                                    app.MainPage = (Page)Activator.CreateInstance(mainPageType);
                                }
                                // In case of multi page with navigation
                                else if(rootPageType != null
                                    && mainPageType == null)
                                {
                                    mainPageType = typeof(NavigationPage);
                                    app.MainPage = new NavigationPage((Page)Activator.CreateInstance(rootPageType));
                                }
                                // In case of custom configuration
                                else if(mainPageType != null
                                    && rootPageType != null)
                                {
                                    // Create the new main page which must host a root page
                                    rootPage = (Page)Activator.CreateInstance(rootPageType);
                                    app.MainPage = (Page)Activator.CreateInstance(mainPageType, rootPage);

                                }

                                // Reset collected pages 
                                _pages.Clear();

                                // Re collect the root page
                                if (rootPageType != null)
                                {
                                    _pages.Add(rootPageType.FullName, new WeakReference(rootPage));
                                    await ReloadXaml(rootPage);
                                }

                                // Re collect the main page (could be a NavigationPage)
                                if (app.MainPage != null)
                                {
                                    _pages.Add(mainPageType.FullName, new WeakReference(app.MainPage));
                                    if (app.MainPage.GetType() != typeof(NavigationPage))
                                        await ReloadXaml(app.MainPage);
                                }

                                // Notify that the assembly was loaded correctly
                                await _hubConnection.SendAsync("AssemblyReloaded", assemblyName, assembly.GetName().Version.ToString());

                                System.Diagnostics.Debug.WriteLine($"A new main page of type '{mainPageType.FullName}' has been loaded!", "Ok");
                            }
                            catch (Exception ex)
                            {
                                // Notify that the assembly was loaded correctly
                                await _hubConnection.SendAsync("ThrowException", ex.ToString());

                                System.Diagnostics.Debug.WriteLine($"Unable to load the assembly '{assemblyName}'");
                                System.Diagnostics.Debug.WriteLine(ex);
                            }
                        });
                }
            }
            catch (Exception ex)
            {
                // Notify that the assembly was loaded correctly
                await _hubConnection.SendAsync("ThrowException", ex.ToString());

                System.Diagnostics.Debug.WriteLine($"Unable to load the assembly '{assemblyName}'");
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

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

        private bool TryPing(string strIpAddress, int intPort, int nTimeoutMsec)
        {
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, false);


                IAsyncResult result = socket.BeginConnect(strIpAddress, intPort, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(nTimeoutMsec, true);

                return socket.Connected;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (null != socket)
                    socket.Close();
            }
        }

        #endregion

        #region SignalR Hub Callback Methods

        private async Task WhenReloadXaml(string pageId, byte[] data, bool refresh)
        {
            Application app = null;
            _app.TryGetTarget(out app);
            if (app == null)
                return;

            string xaml = DecompressXaml(data);

            Assembly assembly = app.GetType().Assembly;
            Type itemType = assembly.GetType(pageId);
            if (itemType != null
                && itemType.BaseType == typeof(Xamarin.Forms.Application))
            {
                app.Resources.Clear();
                app.LoadFromXaml(xaml);

                // Do we need to refresh pages?
                if (refresh)
                {
                    var pages = _pages.Values
                        .Where(x => x.IsAlive)
                        .Select(x => x.Target as Page)
                        .Where(x => x.Parent != null)
                        .ToArray();

                    foreach (var page in pages)
                        await ReloadXaml(page);
                }
            }
            else
            {
                // For each page store the latest xaml
                // sended by the IDE or saved by the user. 
                // This allow every new page instace to have the latest xaml
                _xamlCache[pageId] = data;

                // Do we need to refresh pages?
                if (refresh)
                {
                    var pages = _pages.Values
                        .Where(x => x.IsAlive)
                        .Select(x => x.Target as Page)
                        .Where(x => x.Parent != null)
                        .ToArray();

                    foreach (var page in pages)
                    {
                        string pid = page.GetType().FullName;
                        if (pid == pageId)
                            await ReloadXaml(page, data);
                    }
                }
            }
        }

        private async Task WhenReloadAssembly(string assemblyName, byte[] data)
        {
            await ReloadAssmbly(assemblyName, data);
        }

        #endregion

        #region Event Handlers

        private async void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            if (_isConnected)
                await _hubConnection.SendAsync("ThrowException", e.Exception.ToString());
        }

        private async void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if(_isConnected)
                await _hubConnection.SendAsync("ThrowException", (e.ExceptionObject as Exception)?.ToString());
        }

        private async void Application_PageAppearing(object sender, Page page)
        {
            if(!_isConnected)
                await _connectionTCS.Task;

            if(_isConnected)
            {
                string pageId = page.GetType().FullName;
                string pageKey = page.GetHashCode().ToString("x");
                if(!_pages.ContainsKey(pageKey))
                    _pages.Add(pageKey, new WeakReference(page));

                await _hubConnection.SendAsync("PageAppearing", pageId);
            }
        }

        private async void Application_PageDisappearing(object sender, Page page)
        {
            if (_isConnected)
            {
                string pageId = page.GetType().FullName;
                await _hubConnection.SendAsync("PageDisappearing", pageId);
            }
        }

        #endregion
    }
}
