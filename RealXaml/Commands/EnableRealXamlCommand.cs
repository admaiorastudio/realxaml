using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace AdMaiora.RealXaml.Extension.Commands
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class EnableRealXamlCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("1c1452ad-2740-48a5-b013-186397af19ad");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// DTE
        /// </summary>
        private DTE dte;

        /// <summary>
        /// Initializes a new instance of the <see cref="EnableRealXamlCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private EnableRealXamlCommand(AsyncPackage package, DTE dte, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            this.dte = dte ?? throw new ArgumentNullException(nameof(dte));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
            this.MenuItem = menuItem;
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static EnableRealXamlCommand Instance
        {
            get;
            private set;
        }

        public MenuCommand MenuItem
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package, DTE dte)
        {
            // Switch to the main thread - the call to AddCommand in EnableRealXamlCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            Instance = new EnableRealXamlCommand(package, dte, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private async void Execute(object sender, EventArgs e)
        {
            // Switch to the main thread - the call to AddCommand in SendAssemblyCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.package.DisposalToken);

            try
            {
                ProjectItem appPI = this.dte.Solution.FindProjectItem("App.xaml") as ProjectItem;
                if (appPI == null)
                {
                    DisableRealXamlCommand.Instance.MenuItem.Enabled = false;
                    VsShellUtilities.ShowMessageBox(
                        this.package,
                        "You need to open a valid soultion first!",
                        "RealXaml",
                        OLEMSGICON.OLEMSGICON_WARNING,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                    return;
                }

                await UpdateManager.Current.StartAsync();
                this.MenuItem.Enabled = false;
                DisableRealXamlCommand.Instance.MenuItem.Enabled = true;

                VsShellUtilities.ShowMessageBox(
                    this.package,
                    "RealXaml is activated!",
                    "RealXaml",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch(Exception ex)
            {                
                DisableRealXamlCommand.Instance.MenuItem.Enabled = false;

                System.Diagnostics.Debug.WriteLine("RealXaml was unable to activate itself.");
                System.Diagnostics.Debug.WriteLine(ex);

                VsShellUtilities.ShowMessageBox(
                    this.package,
                    "Unable to activate RealXaml!",
                    "RealXaml",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }

        /// <summary>
        /// This is the event handler used to manage the availability of the command
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void MenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            try
            {
                // Switch to the main thread - the call to AddCommand in SendAssemblyCommand's constructor requires
                // the UI thread.
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.package.DisposalToken);

                OleMenuCommand menuItem = sender as OleMenuCommand;
                menuItem.Enabled = this.dte.Solution.FindProjectItem("App.xaml") != null;
            }
            catch(Exception ex)
            {
                OleMenuCommand menuItem = sender as OleMenuCommand;
                menuItem.Enabled = this.dte.Solution.FindProjectItem("App.xaml") != null;
            }
        }

    }
}
