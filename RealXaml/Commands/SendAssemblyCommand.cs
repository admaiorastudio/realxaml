using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using System.Linq;
using System.IO;

namespace AdMaiora.RealXaml.Extension.Commands
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class SendAssemblyCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 4129;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("9587945A-CB02-4D8C-8F8A-3B5F36C645CA");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="SendAssemblyCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private SendAssemblyCommand(AsyncPackage package, OleMenuCommandService commandService)
        {           
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);

            var menuItem = new OleMenuCommand(this.Execute, menuCommandID);
            menuItem.BeforeQueryStatus += OleMenuItem_BeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static SendAssemblyCommand Instance
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
        /// 
        /// </summary>
        private IVsOutputWindowPane OutputPane
        {
            get
            {
                return ((RealXamlPacakge)this.package)?.OutputPane;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in SendAssemblyCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            Instance = new SendAssemblyCommand(package, commandService);
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
            try
            {
                // Switch to the main thread - the call to AddCommand in SendAssemblyCommand's constructor requires
                // the UI thread.
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.package.DisposalToken);

                var dte = await this.package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                var projects = dte.ActiveSolutionProjects as Array;
                var project = projects.Cast<Project>().FirstOrDefault();
                if (project == null)
                    return;

                EnvDTE.Property property = project.ConfigurationManager.ActiveConfiguration.Properties.Item("OutputPath");
                string fullPath = project.Properties.Item("FullPath").Value.ToString();
                string outputFileName = project.Properties.Item("OutputFileName").Value.ToString();
                string outputPath = property.Value.ToString();

                string assemblyPath = Path.Combine(fullPath, outputPath, outputFileName);

                using (MemoryStream ms = new MemoryStream())
                {
                    // Make a copy of the file into memory to avoid any file lock                   
                    using (FileStream fs = File.OpenRead(assemblyPath))
                        await fs.CopyToAsync(ms);

                    UpdateManager.Current.NotifyAssembly(outputFileName, ms.ToArray());

                    this.OutputPane?.OutputString("Requesting assembly update...");
                    this.OutputPane?.OutputString(Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                this.OutputPane?.OutputString($"Something went wrong! RealXaml was unable to send the updated assembly.");
                this.OutputPane?.OutputString(Environment.NewLine);
                this.OutputPane?.OutputString(ex.ToString());
                this.OutputPane?.OutputString(Environment.NewLine);

                System.Diagnostics.Debug.WriteLine($"Something went wrong! RealXaml was unable to send the updated assembly.");
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }

            //dte.Solution.SolutionBuild.BuildProject(
            //    project.ConfigurationManager.ActiveConfiguration.ConfigurationName, 
            //    project.UniqueName);               
        }

        /// <summary>
        /// This is the event handler used to handle the availability of the command
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private async void OleMenuItem_BeforeQueryStatus(object sender, EventArgs e)
        {
            // Switch to the main thread - the call to AddCommand in SendAssemblyCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(this.package.DisposalToken);

            OleMenuCommand menuItem = sender as OleMenuCommand;

            if (!UpdateManager.Current.IsStarted
                || ((RealXamlPacakge)this.package).IsBuilding)
            {
                menuItem.Visible = false;
                return;
            }

            var dte = await this.package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var projects = dte.ActiveSolutionProjects as Array;
            if (projects == null
                || projects.Length > 1)
            {
                menuItem.Visible = false;
                return;
            }

            EnvDTE.Project project = projects.Cast<EnvDTE.Project>().FirstOrDefault();
            EnvDTE.ProjectItem projectItem = project.ProjectItems.Cast<ProjectItem>().SingleOrDefault(x => x.Name == "App.xaml");
            menuItem.Visible = projectItem != null;            
        }
    }
}
