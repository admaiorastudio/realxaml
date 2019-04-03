using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.Win32;
using Task = System.Threading.Tasks.Task;

namespace AdMaiora.RealXaml.Extension
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
    [Guid(RealXamlPacakge.PackageGuidString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(RealXamlPacakge.PackageGuidString, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideBindingPath]
    public sealed class RealXamlPacakge : AsyncPackage, IVsRunningDocTableEvents, IAsyncDisposable
    {
        #region Inner Classes

        public class ManualAssemblyResolver : IDisposable
        {
            #region Costants and Fields

            /// <summary>
            /// list of the known assemblies by this resolver
            /// </summary>
            private readonly List<Assembly> _assemblies;

            #endregion

            #region Properties

            /// <summary>
            /// function to be called when an unknown assembly is requested that is not yet kown
            /// </summary>
            public Func<ResolveEventArgs, Assembly> OnUnknowAssemblyRequested { get; set; }

            #endregion

            #region Constructor

            public ManualAssemblyResolver(params Assembly[] assemblies)
            {
                _assemblies = new List<Assembly>();

                if (assemblies != null)
                    _assemblies.AddRange(assemblies);

                AppDomain.CurrentDomain.AssemblyResolve += Domain_AssemblyResolve;
            }

            #endregion

            #region Implement IDisposeable

            public void Dispose()
            {
                AppDomain.CurrentDomain.AssemblyResolve -= Domain_AssemblyResolve;
            }

            #endregion

            #region Event Handlers

            /// <summary>
            /// will be called when an unknown assembly should be resolved
            /// </summary>
            /// <param name="sender">sender of the event</param>
            /// <param name="args">event that has been sent</param>
            /// <returns>the assembly that is needed or null</returns>
            private Assembly Domain_AssemblyResolve(object sender, ResolveEventArgs args)
            {
                foreach (Assembly assembly in _assemblies)
                    if (assembly.FullName.Contains(args.Name.Split(',')[0]))
                        return assembly;

                if (OnUnknowAssemblyRequested != null)
                {
                    Assembly assembly = OnUnknowAssemblyRequested(args);

                    if (assembly != null)
                        _assemblies.Add(assembly);

                    return assembly;
                }

                return null;
            }

            #endregion
        }

        #endregion

        #region Constants and Fields

        /// <summary>
        /// RealXamlPacakge GUID string.
        /// </summary>
        public const string PackageGuidString = "f325ad02-8b8b-478f-87f7-ecc77cbed5be";

        private DTE _dte;

        private uint _rdtCookie;
        private IVsRunningDocumentTable _rdt;

        private EnvDTE.Project _mainDllProject;

        private ManualAssemblyResolver _assemblyResolver;

        private Guid _paneGuid;
        private string _paneTitle;
        private IVsOutputWindowPane _outputPane;

        private Dictionary<string, string> _xamlCache;

        private List<CommandEvents> _cmdEvents;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="RealXamlPacakge"/> class.
        /// </summary>
        public RealXamlPacakge()
        {
            // Inside this method you can place any initialization code that does not require
            // any Visual Studio service because at this point the package object is created but
            // not sited yet inside Visual Studio environment. The place to do all the other
            // initialization is the Initialize method.

            _xamlCache = new Dictionary<string, string>();
        }

        #endregion

        #region Properties

        public bool IsBuilding
        {
            get;
            private set;
        }

        public IVsOutputWindowPane OutputPane
        {
            get
            {
                return _outputPane;
            }
        }

        #endregion

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _dte = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            if (_dte == null)
                return;

            // Intercept build commands
            string[] buildCommandNames = new[]
            {                
                "Build.BuildSolution",
                "Build.RebuildSolution",
                "Build.BuildSelection",
                "Build.RebuildSelection",
                "ClassViewContextMenus.ClassViewProject.Build",
                "ClassViewContextMenus.ClassViewProject.Rebuild",
                "Build.ProjectPickerBuild",
                "Build.ProjectPickerRebuild",
                "Build.BuildOnlyProject",
                "Build.RebuildOnlyProject"
            };

            _cmdEvents = new List<CommandEvents>();
            foreach (string buildCommandName in buildCommandNames)
            {                
                var buildCommand = _dte.Commands.Item(buildCommandName);
                var cmdev = _dte.Events.CommandEvents[buildCommand.Guid, buildCommand.ID];
                cmdev.BeforeExecute += this.BuildCommand_BeforeExecute;
                _cmdEvents.Add(cmdev);
            }

            _dte.Events.SolutionEvents.BeforeClosing += SolutionEvents_BeforeClosing;            
            _dte.Events.BuildEvents.OnBuildBegin += BuildEvents_OnBuildBegin;
            _dte.Events.BuildEvents.OnBuildDone += BuildEvents_OnBuildDone;
            _dte.Events.BuildEvents.OnBuildProjConfigBegin += BuildEvents_OnBuildProjConfigBegin;                        


            _rdt = (IVsRunningDocumentTable)(await GetServiceAsync(typeof(SVsRunningDocumentTable)));
            _rdt.AdviseRunningDocTableEvents(this, out _rdtCookie);            
                       
            await AdMaiora.RealXaml.Extension.Commands.EnableRealXamlCommand.InitializeAsync(this, _dte);
            await AdMaiora.RealXaml.Extension.Commands.DisableRealXamlCommand.InitializeAsync(this, _dte);

            CreateOutputPane();

            try
            {
                string currentPath = Path.GetDirectoryName(GetType().Assembly.Location);
                _assemblyResolver = new ManualAssemblyResolver(
                    Assembly.LoadFile(Path.Combine(currentPath, "Newtonsoft.Json.dll")),
                    Assembly.LoadFile(Path.Combine(currentPath, "System.Buffers.dll")),
                    Assembly.LoadFile(Path.Combine(currentPath, "System.Numerics.Vectors.dll"))
                    );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                _outputPane.OutputString("Something went wrong loading assemblies.");
                _outputPane.OutputString(ex.ToString());
            }

            UpdateManager.Current.IdeRegistered += this.UpdateManager_IdeRegistered;
            UpdateManager.Current.ClientRegistered += this.UpdateManager_ClientRegistered;
            UpdateManager.Current.PageAppeared += this.UpdateManager_PageAppeared;
            UpdateManager.Current.PageDisappeared += this.UpdateManager_PageDisappeared;
            UpdateManager.Current.XamlUpdated += this.UpdateManager_XamlUpdated;
            UpdateManager.Current.AssemblyLoaded += this.UpdateManager_AssemblyLoaded;
            UpdateManager.Current.ExceptionThrown += this.UpdateManager_ExceptionThrown;
            UpdateManager.Current.IdeNotified += this.Current_IdeNotified;

            _xamlCache.Clear();
        }

        #endregion

        #region Public Methods

        public async Task DisposeAsync()
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            IVsRunningDocumentTable rdt = (IVsRunningDocumentTable)(await GetServiceAsync(typeof(SVsRunningDocumentTable)));
            rdt.UnadviseRunningDocTableEvents(_rdtCookie);

            if (_outputPane != null)
            {
                _outputPane?.Hide();
                IVsOutputWindow output = (IVsOutputWindow)GetService(typeof(SVsOutputWindow));
                output.DeletePane(ref _paneGuid);
            }
        }

        #endregion

        #region Methods

        private string IncrementDottedVersionNumber(string versionNumber)
        {
            if (String.IsNullOrWhiteSpace(versionNumber))
                throw new ArgumentNullException("versionNumber");

            if (!versionNumber.Contains("."))
                throw new InvalidOperationException("Invalid dotted version number.");

            string[] tokens = versionNumber.Split('.');
            int version = Int32.Parse(tokens.Last());
            tokens[tokens.Length - 1] = (++version).ToString();

            return String.Join(".", tokens);
        }

        private void CreateOutputPane()
        {
            _paneGuid = Guid.NewGuid();
            _paneTitle = "Real Xaml";

            IVsOutputWindow output = (IVsOutputWindow)GetService(typeof(SVsOutputWindow));
            
            output.CreatePane(ref _paneGuid, _paneTitle, Convert.ToInt32(true), Convert.ToInt32(true));
            output.GetPane(ref _paneGuid, out _outputPane);            
        }

        private Document FindDocumentByCookie(uint docCookie)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            var documentInfo = _rdt.GetDocumentInfo(docCookie, out uint p1, out uint p2, out uint p3, out string p4, out IVsHierarchy p5, out uint p6, out IntPtr p7);
            return _dte.Documents.Cast<Document>().FirstOrDefault(doc => doc.FullName == p4);
        }

        #endregion

        #region IVsRunningDocTableEvents Methods

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie)
        {
            if (!UpdateManager.Current.IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("RealXaml was unable to send the xaml. No connection to the notifier.");
                return VSConstants.S_OK;
            }

            ThreadHelper.ThrowIfNotOnUIThread();

            if (_dte == null)
                return VSConstants.S_OK;

            try
            {
                Document doc = FindDocumentByCookie(docCookie);
                if (doc == null)
                    return VSConstants.S_OK;

                string kind = doc.Kind;
                string lang = doc.Language;

                string filePath = doc.FullName;
                string fileExt = Path.GetExtension(filePath)?.ToLower() ?? ".unknown";
                if (fileExt != ".xaml")
                    return VSConstants.S_OK;

                XDocument xdoc = XDocument.Load(filePath);
                XNamespace xnsp = "http://schemas.microsoft.com/winfx/2009/xaml";
                string pageId = xdoc.Root.Attribute(xnsp + "Class").Value;

                TextDocument textdoc = (TextDocument)(doc.Object("TextDocument"));
                var p = textdoc.StartPoint.CreateEditPoint();
                string xaml = p.GetText(textdoc.EndPoint);

                _xamlCache[pageId] = xaml;

                // Save is due to a project build
                // Xaml will be sent after the build
                if (!this.IsBuilding)
                {
                    Task.Run(
                        async () =>
                        {
                            try
                            {
                                await UpdateManager.Current.SendXamlAsync(pageId, xaml, true);
                            }
                            catch (Exception ex)
                            {
                                _outputPane.OutputString($"Something went wrong! RealXaml was unable to send the xaml.");
                                _outputPane.OutputString(Environment.NewLine);
                                _outputPane.OutputString(ex.ToString());
                                _outputPane.OutputString(Environment.NewLine);

                                System.Diagnostics.Debug.WriteLine("Something went wrong! RealXaml was unable to send the xaml.");
                                System.Diagnostics.Debug.WriteLine(ex);

                            }
                        });
                }
            }
            catch (Exception ex)
            {
                _outputPane.OutputString($"Something went wrong! RealXaml was unable to send the xaml.");
                _outputPane.OutputString(Environment.NewLine);
                _outputPane.OutputString(ex.ToString());
                _outputPane.OutputString(Environment.NewLine);

                System.Diagnostics.Debug.WriteLine("Something went wrong! RealXaml was unable to send the xaml.");
                System.Diagnostics.Debug.WriteLine(ex);
            }

            return VSConstants.S_OK;
        }

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        #endregion

        #region Events

        private void SolutionEvents_BeforeClosing()
        {
            try
            {
                UpdateManager.Current.StopAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RealXaml was unable to deactivate itself!");
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private void BuildCommand_BeforeExecute(string Guid, int ID, object CustomIn, object CustomOut, ref bool CancelDefault)
        {
            this.IsBuilding = true;
        }

        private void BuildEvents_OnBuildBegin(vsBuildScope Scope, vsBuildAction Action)
        {
            if (!UpdateManager.Current.IsConnected)
                return;

            _mainDllProject = null;
        }

        private async void BuildEvents_OnBuildProjConfigBegin(string Project, string ProjectConfig, string Platform, string SolutionConfig)
        {
            if (!UpdateManager.Current.IsConnected)
                return;            

            try
            {
                await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
                EnvDTE.ProjectItem pi = _dte.Solution.FindProjectItem("App.xaml");
                if (pi.ContainingProject.UniqueName == Project)
                {
                    _mainDllProject = pi.ContainingProject;

                    _mainDllProject.Properties.Item("Version").Value = 
                        IncrementDottedVersionNumber(_mainDllProject.Properties.Item("Version").Value?.ToString());

                    _mainDllProject.Properties.Item("FileVersion").Value =
                        IncrementDottedVersionNumber(_mainDllProject.Properties.Item("FileVersion").Value?.ToString());

                    _mainDllProject.Properties.Item("AssemblyVersion").Value =
                        IncrementDottedVersionNumber(_mainDllProject.Properties.Item("AssemblyVersion").Value?.ToString());
                }
            }
            catch (Exception ex)
            {
                _mainDllProject = null;
            }
        }

        private async void BuildEvents_OnBuildDone(vsBuildScope Scope, vsBuildAction Action)
        {
            if (!UpdateManager.Current.IsConnected)
                return;

            try
            {
                await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
                if (_mainDllProject != null
                    && Scope == vsBuildScope.vsBuildScopeProject
                    && Action == vsBuildAction.vsBuildActionBuild)
                {
                    EnvDTE.Property property = _mainDllProject.ConfigurationManager.ActiveConfiguration.Properties.Item("OutputPath");
                    string fullPath = _mainDllProject.Properties.Item("FullPath").Value.ToString();
                    string outputFileName = _mainDllProject.Properties.Item("OutputFileName").Value.ToString();
                    string outputPath = property.Value.ToString();

                    string assemblyPath = Path.Combine(fullPath, outputPath, outputFileName);

                    using (MemoryStream ms = new MemoryStream())
                    {
                        // Make a copy of the file into memory to avoid any file lock                   
                        using (FileStream fs = File.OpenRead(assemblyPath))
                            await fs.CopyToAsync(ms);

                        await UpdateManager.Current.SendAssemblyAsync(outputFileName, ms.ToArray());

                        if(_xamlCache.Count > 0)
                        {
                            // Force a xaml update for every page
                            foreach(var cacheItem in _xamlCache)                            
                                await UpdateManager.Current.SendXamlAsync(cacheItem.Key, cacheItem.Value, true);

                            _xamlCache.Clear();
                        }

                        _outputPane.OutputString("Requesting assembly update...");
                        _outputPane.OutputString(Environment.NewLine);
                    }
                }
            }
            catch(Exception ex)
            {
                _outputPane.OutputString($"Something went wrong! RealXaml was unable to send the updated assembly.");
                _outputPane.OutputString(Environment.NewLine);
                _outputPane.OutputString(ex.ToString());
                _outputPane.OutputString(Environment.NewLine);

                System.Diagnostics.Debug.WriteLine($"Something went wrong! RealXaml was unable to send the updated assembly.");
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }

            this.IsBuilding = false;
        }

        private async void UpdateManager_IdeRegistered(object sender, EventArgs e)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            _outputPane.OutputString($"RealXaml is up and running. Welcome commander!");
            _outputPane.OutputString(Environment.NewLine);
        }

        private async void UpdateManager_ClientRegistered(object sender, ClientNotificationEventArgs e)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            _outputPane.OutputString($"A new client with ID {e.ClientId} is now connected.");
            _outputPane.OutputString(Environment.NewLine);

            System.Diagnostics.Debug.WriteLine($"A new client with ID {e.ClientId} is now connected.");
        }

        private async void UpdateManager_PageAppeared(object sender, PageNotificationEventArgs e)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            _outputPane.OutputString($"The page '{e.PageId}' is now visible.");
            _outputPane.OutputString(Environment.NewLine);

            System.Diagnostics.Debug.WriteLine($"The page '{e.PageId}' is now visible.");

            try
            {
                EnvDTE.ProjectItem appPi = _dte.Solution.FindProjectItem("App.xaml");
                if (appPi != null)
                {
                    foreach (ProjectItem pi in appPi.ContainingProject.ProjectItems)
                    {
                        if (!pi.Name.Contains(".xaml"))
                            continue;

                        string fileName = pi.FileNames[0];
                        XDocument xdoc = XDocument.Load(fileName);
                        XNamespace xnsp = "http://schemas.microsoft.com/winfx/2009/xaml";
                        string pageId = xdoc.Root.Attribute(xnsp + "Class").Value;
                        if (pageId != e.PageId)
                            continue;

                        var document = pi.Document;
                        string localPath = pi.Properties.Item("LocalPath").Value?.ToString();
                        string xaml = System.IO.File.ReadAllText(localPath);

                        await UpdateManager.Current.SendXamlAsync(pageId, xaml, false);
                    }
                }
            }
            catch(Exception ex)
            {
                _outputPane.OutputString($"Something went wrong! RealXaml was unable to send the xaml.");
                _outputPane.OutputString(Environment.NewLine);
                _outputPane.OutputString(ex.ToString());
                _outputPane.OutputString(Environment.NewLine);

                System.Diagnostics.Debug.WriteLine("Something went wrong! RealXaml was unable to send the xaml.");
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private async void UpdateManager_PageDisappeared(object sender, PageNotificationEventArgs e)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            _outputPane.OutputString($"The page '{e.PageId}' went away.");
            _outputPane.OutputString(Environment.NewLine);

            System.Diagnostics.Debug.WriteLine($"The page '{e.PageId}' went away.");
        }

        private async void UpdateManager_XamlUpdated(object sender, PageNotificationEventArgs e)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            _outputPane.OutputString($"The page '{e.PageId}' received a new xaml.");
            _outputPane.OutputString(Environment.NewLine);

            System.Diagnostics.Debug.WriteLine($"The page '{e.PageId}' received a new xaml.");
        }

        private async void UpdateManager_AssemblyLoaded(object sender, AssemblyNotificationEventArgs e)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            _outputPane.OutputString($"A new version of the assembly '{e.AssemblyName}' has been loaded. Now running version '{e.Version}'.");
            _outputPane.OutputString(Environment.NewLine);

            System.Diagnostics.Debug.WriteLine($"A new version of the assembly '{e.AssemblyName}' has been loaded. Now running version '{e.Version}'.");
        }

        private async void UpdateManager_ExceptionThrown(object sender, ExceptionNotificationEventArgs e)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            _outputPane.OutputString($"Something went wrong!");
            _outputPane.OutputString(Environment.NewLine);
            _outputPane.OutputString(e.Message);
            _outputPane.OutputString(Environment.NewLine);

            System.Diagnostics.Debug.WriteLine($"Something went wrong!");
            System.Diagnostics.Debug.WriteLine(e.Message);
        }

        private async void Current_IdeNotified(object sender, IdeNotificationEventArgs e)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(this.DisposalToken);
            _outputPane.OutputString(e.Message);
            _outputPane.OutputString(Environment.NewLine);
        }

        #endregion
    }
}
