//-----------------------------------------------------------------------
// <copyright file="Program.cs">(c) https://github.com/tfsbuildextensions/BuildManager. This source is subject to the Microsoft Permissive License. See http://www.microsoft.com/resources/sharedsource/licensingbasics/sharedsourcelicenses.mspx. All other rights reserved.</copyright>
//-----------------------------------------------------------------------
namespace TFSBuildManager.Console
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Microsoft.TeamFoundation.Build.Client;
    using Microsoft.TeamFoundation.Client;
    using TfsBuildManager.Views;
    using System.Net;
    using Newtonsoft.Json;
    using Microsoft.TeamFoundation.Build.Workflow;
    using Microsoft.TeamFoundation.Build.Workflow.Activities;

    public class Program
    {
        private static readonly Dictionary<string, string> Arguments = new Dictionary<string, string>();
        private static ReturnCode rc = ReturnCode.NoErrors;

        private enum ReturnCode
        {
            /// <summary>
            /// NoErrors
            /// </summary>
            NoErrors = 0,

            /// <summary>
            /// ArgumentsNotSupplied
            /// </summary>
            ArgumentsNotSupplied = -1000,

            /// <summary>
            /// InvalidArgumentsSupplied
            /// </summary>
            InvalidArgumentsSupplied = -1500,

            /// <summary>
            /// UnhandledException
            /// </summary>
            UnhandledException = -1999,

            /// <summary>
            /// UsageRequested
            /// </summary>
            UsageRequested = -9000
        }

        internal static string Action
        {
            get
            {
                string action;
                if (Arguments.TryGetValue("Action", out action))
                {
                    return action;
                }

                throw new ArgumentNullException("Action");
            }
        }

        internal static string TeamProject
        {
            get
            {
                string project;
                if (Arguments.TryGetValue("TeamProject", out project))
                {
                    return project;
                }

                throw new ArgumentNullException("TeamProject");
            }
        }

        internal static Uri ProjectCollection
        {
            get
            {
                string collection;
                if (Arguments.TryGetValue("ProjectCollection", out collection))
                {
                    return new Uri(collection);
                }

                throw new ArgumentNullException("ProjectCollection");
            }
        }

        internal static string ExportPath
        {
            get
            {
                string path;
                if (Arguments.TryGetValue("ExportPath", out path))
                {
                    return path;
                }

                throw new ArgumentNullException("ExportPath");
            }
        }

        internal static string Username
        {
            get
            {
                string username;
                if (Arguments.TryGetValue("Username", out username))
                {
                    return username;
                }

                throw new ArgumentNullException("Username");
            }
        }

        internal static string Password
        {
            get
            {
                string password;
                if (Arguments.TryGetValue("Password", out password))
                {
                    return password;
                }

                throw new ArgumentNullException("Password");
            }
        }

        internal static string ImportPath
        {
            get
            {
                string path;
                if (Arguments.TryGetValue("ImportPath", out path))
                {
                    return path;
                }

                throw new ArgumentNullException("ImportPath");
            }
        }
        private static int Main(string[] args)
        {
            Console.WriteLine("Community TFS Build Manager Console - {0}\n", GetFileVersion(Assembly.GetExecutingAssembly()));

            try
            {
                // ---------------------------------------------------
                // Process the arguments
                // ---------------------------------------------------
                int retval = ProcessArguments(args);
                if (retval != 0)
                {
                    return retval;
                }

                NetworkCredential netCred = new NetworkCredential(Username, Password);
                BasicAuthCredential basicCred = new BasicAuthCredential(netCred);
                TfsClientCredentials tfsCred = new TfsClientCredentials(basicCred);
                tfsCred.AllowInteractive = false;

                TfsTeamProjectCollection collection = new TfsTeamProjectCollection(ProjectCollection, tfsCred);

                collection.Authenticate();

                IBuildServer buildServer = (IBuildServer)collection.GetService(typeof(IBuildServer));

                switch (Action.ToUpper())
                {
                    case "EXPORT":
                        // ---------------------------------------------------
                        // Export the specified builds
                        // ---------------------------------------------------
                        retval = ExportBuilds(buildServer);
                        if (retval != 0)
                        {
                            return retval;
                        }

                        break;
                    case "IMPORT":
                        retval = ImportBuild(buildServer, ImportPath);
                        if (retval != 0)
                        {
                            return retval;
                        }

                        break;
                    default:
                        rc = ReturnCode.InvalidArgumentsSupplied;
                        return (int)rc;
                }
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                if (ex.InnerException != null)
                {
                    message += string.Format("Inner Exception: {0}", ex.InnerException.Message);
                }

                rc = ReturnCode.UnhandledException;
                LogMessage(message);
                return (int)rc;
            }

            return (int)rc;
        }

        private static int ExportBuilds(IBuildServer buildServer)
        {
            IBuildDefinition[] defs = buildServer.QueryBuildDefinitions(TeamProject);

            if (!Directory.Exists(ExportPath))
            {
                Console.WriteLine("ExportPath not found, creating: {0}", ExportPath);
                Directory.CreateDirectory(ExportPath);
            }

            Console.WriteLine("Exporting {0} definitions to: {1}", defs.Length, ExportPath);
            Console.WriteLine(string.Empty);

            foreach (var b in defs)
            {
                Console.WriteLine(b.Name);
                BuildManagerViewModel.ExportDefinition(new BuildDefinitionViewModel(b), ExportPath);
            }
            
            Console.WriteLine(string.Empty);
            Console.WriteLine("{0} definitions exported to: {1}", defs.Length, ExportPath);

            return 0;
        }

        private static int ImportBuild(IBuildServer buildServer, string importPath)
        {
            try
            {

                if (!File.Exists(importPath))
                {
                    // file does not exist
                }
                else
                {
                    ExportedBuildDefinition exdef = JsonConvert.DeserializeObject<ExportedBuildDefinition>(File.ReadAllText(importPath));
                    var newBuildDefinition = buildServer.CreateBuildDefinition(TeamProject);
                    newBuildDefinition.Name = exdef.Name;
                    newBuildDefinition.Description = exdef.Description;
                    newBuildDefinition.ContinuousIntegrationType = exdef.ContinuousIntegrationType;
                    newBuildDefinition.ContinuousIntegrationQuietPeriod = exdef.ContinuousIntegrationQuietPeriod;

                    newBuildDefinition.QueueStatus = exdef.QueueStatus;
                    if (exdef.SourceProviders.All(s => s.Name != "TFGIT"))
                    {
                        foreach (var mapping in exdef.Mappings)
                        {
                            newBuildDefinition.Workspace.AddMapping(mapping.ServerItem, mapping.LocalItem, mapping.MappingType);
                        }
                    }

                    newBuildDefinition.RetentionPolicyList.Clear();
                    foreach (var ret in exdef.RetentionPolicyList)
                    {
                        newBuildDefinition.AddRetentionPolicy(ret.BuildReason, ret.BuildStatus, ret.NumberToKeep, ret.DeleteOptions);
                    }

                    foreach (var sp in exdef.SourceProviders)
                    {
                        var provider = newBuildDefinition.CreateInitialSourceProvider(sp.Name);
                        if (exdef.SourceProviders.All(s => s.Name == "TFGIT"))
                        {
                            provider.Fields["RepositoryName"] = sp.Fields["RepositoryName"];
                            provider.Fields["DefaultBranch"] = sp.Fields["DefaultBranch"];
                            provider.Fields["CIBranches"] = sp.Fields["CIBranches"];
                            provider.Fields["RepositoryUrl"] = sp.Fields["RepositoryUrl"];
                        }

                        newBuildDefinition.SetSourceProvider(provider);
                    }

                    newBuildDefinition.BuildController = buildServer.GetBuildController(exdef.BuildController);
                    var x = buildServer.QueryProcessTemplates(TeamProject);
                    if (x.All(p => p.ServerPath != exdef.ProcessTemplate))
                    {
                        // process template not found
                    }

                    newBuildDefinition.Process = buildServer.QueryProcessTemplates(TeamProject).First(p => p.ServerPath == exdef.ProcessTemplate);
                    newBuildDefinition.DefaultDropLocation = exdef.DefaultDropLocation;
                    foreach (var sched in exdef.Schedules)
                    {
                        var newSched = newBuildDefinition.AddSchedule();
                        newSched.DaysToBuild = sched.DaysToBuild;
                        newSched.StartTime = sched.StartTime;
                        newSched.TimeZone = sched.TimeZone;
                    }

                    var process = WorkflowHelpers.DeserializeProcessParameters(newBuildDefinition.ProcessParameters);

                    foreach (var param in exdef.ProcessParameters)
                    {
                        if (param.Key != "AgentSettings" && param.Key != "BuildSettings" && param.Key != "TestSpecs")
                        {
                            Newtonsoft.Json.Linq.JArray arrayItem = param.Value as Newtonsoft.Json.Linq.JArray;
                            if (arrayItem == null)
                            {
                                Newtonsoft.Json.Linq.JObject objectItem = param.Value as Newtonsoft.Json.Linq.JObject;
                                if (objectItem == null)
                                {
                                    if (param.Key == "CleanWorkspace")
                                    {
                                        process.Add(param.Key, (CleanWorkspaceOption)Enum.Parse(typeof(CleanWorkspaceOption), param.Value.ToString()));
                                    }
                                    else if (param.Key == "RunCodeAnalysis")
                                    {
                                        process.Add(param.Key, (CodeAnalysisOption)Enum.Parse(typeof(CodeAnalysisOption), param.Value.ToString()));
                                    }
                                    else
                                    {
                                        process.Add(param.Key, param.Value);
                                    }
                                }
                                else
                                {
                                    Microsoft.TeamFoundation.Build.Common.BuildParameter paramItem = new Microsoft.TeamFoundation.Build.Common.BuildParameter(param.Value.ToString());
                                    process.Add(param.Key, paramItem);
                                }
                            }
                            else
                            {
                                string[] arrayItemList = new string[arrayItem.Count];
                                for (int i = 0; i < arrayItem.Count; i++)
                                {
                                    arrayItemList[i] = arrayItem[i].ToString();
                                }

                                process.Add(param.Key, arrayItemList);
                            }
                        }
                    }

                    if (exdef.ProjectsToBuild != null)
                    {
                        process.Add("BuildSettings", new BuildSettings { ProjectsToBuild = exdef.ProjectsToBuild, PlatformConfigurations = exdef.ConfigurationsToBuild });
                    }

                    if (exdef.TfvcAgentSettings != null)
                    {
                        process.Add("AgentSettings", new AgentSettings { MaxExecutionTime = exdef.TfvcAgentSettings.MaxExecutionTime, MaxWaitTime = exdef.TfvcAgentSettings.MaxWaitTime, Name = exdef.TfvcAgentSettings.Name, TagComparison = exdef.TfvcAgentSettings.Comparison, Tags = exdef.TfvcAgentSettings.Tags });
                    }
                    else if (exdef.GitAgentSettings != null)
                    {
                        process.Add("AgentSettings", exdef.GitAgentSettings);
                    }

                    if (exdef.AgileTestSpecs != null)
                    {
                        TestSpecList tsl = new TestSpecList();
                        foreach (var aitem in exdef.AgileTestSpecs)
                        {
                            AgileTestPlatformSpec agileSpec = new AgileTestPlatformSpec();
                            agileSpec.AssemblyFileSpec = aitem.AssemblyFileSpec;
                            agileSpec.ExecutionPlatform = aitem.ExecutionPlatform;
                            agileSpec.FailBuildOnFailure = aitem.FailBuildOnFailure;
                            agileSpec.RunName = aitem.RunName;
                            agileSpec.TestCaseFilter = aitem.TestCaseFilter;
                            agileSpec.RunSettingsForTestRun = new RunSettings();
                            agileSpec.RunSettingsForTestRun.ServerRunSettingsFile = aitem.RunSettingsFileName;
                            agileSpec.RunSettingsForTestRun.TypeRunSettings = aitem.TypeRunSettings;
                            tsl.Add(agileSpec);
                        }

                        process.Add("TestSpecs", tsl);
                    }

                    if (exdef.BuildReasons != null)
                    {
                        foreach (var key in exdef.BuildReasons.Keys)
                        {
                            if (process.ContainsKey(key))
                            {
                                process[key] = exdef.BuildReasons[key];
                            }
                        }
                    }

                    if (exdef.IntegerParameters != null)
                    {
                        foreach (var key in exdef.IntegerParameters.Keys)
                        {
                            if (process.ContainsKey(key))
                            {
                                process[key] = exdef.IntegerParameters[key];
                            }
                        }
                    }

                    if (exdef.BuildVerbosities != null)
                    {
                        foreach (var key in exdef.BuildVerbosities.Keys)
                        {
                            if (process.ContainsKey(key))
                            {
                                process[key] = exdef.BuildVerbosities[key];
                            }
                        }
                    }

                    newBuildDefinition.ProcessParameters = WorkflowHelpers.SerializeProcessParameters(process);
                    newBuildDefinition.Save();
                }
            }
            catch (Exception ex)
            {
                // failed
            }


            return 0;
        }

        private static int ProcessArguments(string[] args)
        {
            if (args.Contains("/?") || args.Contains("/help"))
            {
                Console.WriteLine(@"Syntax: ctfsbm.exe /ProjectCollection:<ProjectCollection> /TeamProject:<TeamProject> /ExportPath:<ExportPath>");
                Console.WriteLine("Argument names are case sensitive.\n");
                Console.WriteLine(@"Sample: ctfsbm.exe /ProjectCollection:http://yourcollection:8080/tfs /TeamProject:""Your Team Project"" /ExportPath:""c:\myexporteddefs""");
                return (int)ReturnCode.UsageRequested;
            }

            Console.Write("Processing Arguments");
            if (args.Length == 0)
            {
                rc = ReturnCode.ArgumentsNotSupplied;
                LogMessage();
                return (int)rc;
            }

            Regex searchTerm = new Regex(@"/ProjectCollection:.*");
            bool propertiesargumentfound = args.Select(arg => searchTerm.Match(arg)).Any(m => m.Success);
            if (propertiesargumentfound)
            {
                Arguments.Add("ProjectCollection", args.First(item => item.Contains("/ProjectCollection:")).Replace("/ProjectCollection:", string.Empty));
            }

            searchTerm = new Regex(@"/TeamProject:.*");
            propertiesargumentfound = args.Select(arg => searchTerm.Match(arg)).Any(m => m.Success);
            if (propertiesargumentfound)
            {
                Arguments.Add("TeamProject", args.First(item => item.Contains("/TeamProject:")).Replace("/TeamProject:", string.Empty));
            }

            searchTerm = new Regex(@"/ExportPath:.*");
            propertiesargumentfound = args.Select(arg => searchTerm.Match(arg)).Any(m => m.Success);
            if (propertiesargumentfound)
            {
                Arguments.Add("ExportPath", args.First(item => item.Contains("/ExportPath:")).Replace("/ExportPath:", string.Empty));
            }

            searchTerm = new Regex(@"/Username:.*");
            propertiesargumentfound = args.Select(arg => searchTerm.Match(arg)).Any(m => m.Success);
            if (propertiesargumentfound)
            {
                Arguments.Add("Username", args.First(item => item.Contains("/Username:")).Replace("/Username:", string.Empty));
            }

            searchTerm = new Regex(@"/Password:.*");
            propertiesargumentfound = args.Select(arg => searchTerm.Match(arg)).Any(m => m.Success);
            if (propertiesargumentfound)
            {
                Arguments.Add("Password", args.First(item => item.Contains("/Password:")).Replace("/Password:", string.Empty));
            }

            searchTerm = new Regex(@"/ImportPath:.*");
            propertiesargumentfound = args.Select(arg => searchTerm.Match(arg)).Any(m => m.Success);
            if (propertiesargumentfound)
            {
                Arguments.Add("ImportPath", args.First(item => item.Contains("/ImportPath:")).Replace("/ImportPath:", string.Empty));
            }

            searchTerm = new Regex(@"/Action:.*");
            propertiesargumentfound = args.Select(arg => searchTerm.Match(arg)).Any(m => m.Success);
            if (propertiesargumentfound)
            {
                Arguments.Add("Action", args.First(item => item.Contains("/Action:")).Replace("/Action:", string.Empty));
            }
            Console.Write("...Success\n");
            return 0;
        }

        private static void LogMessage(string message = null)
        {
            const string MessageBlockStart = "\n-------------------------------------------------------------------";
            const string MessageBlockEnd = "-------------------------------------------------------------------";
            Console.WriteLine(MessageBlockStart);
            if (!string.IsNullOrEmpty(message))
            {
                Console.WriteLine(message);
            }

            Console.WriteLine("Return Code: {0} ({1})", (int)rc, rc);
            Console.WriteLine(MessageBlockEnd);
        }

        private static Version GetFileVersion(Assembly asm)
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(asm.Location);
            return new Version(versionInfo.FileMajorPart, versionInfo.FileMinorPart, versionInfo.FileBuildPart, versionInfo.FilePrivatePart);
        }
    }
}
