
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Tools.WindowsDevicePortal;
using static Microsoft.Tools.WindowsDevicePortal.DevicePortal;

namespace DevicePortalTool
{
    class AppOperation
    {
        public enum Operation
        {
            None,
            ListInstalledApps,
            Install,
            Run,
            Uninstall,
        }

        public static readonly string AvailableOperationsText =
            "Supported App operations are the following:\n"
            + "    list\n"
            + "    install\n"
            + "    run\n"
            + "    uninstall\n"
            ;

        public static readonly string AppOperationUsageText =
            "Execute an Application operation on the remote device:\n"
            + "    /op:<operation> [<operation arguments>]\n"
            + "        Executes the app operation with operation-specific parameters\n"
            + "\n"
            + "    /op:<operation> /?\n"
            + "        Shows usage for specified operation\n"
            + "\n"
            + AvailableOperationsText
            ;

        public static readonly string ListOpUsageText =
            "Lists the apps currently installed on the device\n"
            ;

        public static readonly string InstallOpUsageText =
            "Installs the specified app package to the device and optionally runs the specified app\n"
            + "    /appx:<path to Appx file> [/cert:<path to certificate file>] [/launch]\n"
            + "        Installs the given AppX package and optionally launches the app on the remote device\n"
            ;

        public static readonly string RunOpUsageText =
            "Runs the specified app on the device\n"
            + "    /package:<full package name> /aumid:<app's user model ID>\n"
            + "        Launches the app installed specified by package and aumid on the remote device\n"
            ;

        public static readonly string UninstallOpUsageText =
            "Uninstall the specified app from the device\n"
            + "    /package:<app package name>\n"
            + "        Uninstall the app matching the specified full package name from the device\n"
            ;

        public static readonly string ListAppsOpUsageTExt =
            "Outputs a list of installed app packages on the device to stdout\n"
            ;

        public static readonly string ParameterAppx = "appx";
        public static readonly string ParameterCert = "cert";
        public static readonly string ParameterLaunch = "launch";
        public static readonly string parameterPackage = "package";
        public static readonly string ParameterAumid = "aumid";

        public static Operation OperationStringToEnum(string operationName)
        {
            if (String.IsNullOrWhiteSpace(operationName))
            {
                return Operation.None;
            }
            if (operationName.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                return Operation.ListInstalledApps;
            }
            if (operationName.Equals("install", StringComparison.OrdinalIgnoreCase))
            {
                return Operation.Install;
            }
            if (operationName.Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                return Operation.Run;
            }
            if (operationName.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
            {
                return Operation.Uninstall;
            }

            return Operation.None;
        }

        public static string OperationSpecificUsageText(Operation op)
        {
            switch (op)
            {
                case Operation.Install:
                    return InstallOpUsageText;

                case Operation.Run:
                    return RunOpUsageText;

                case Operation.Uninstall:
                    return UninstallOpUsageText;

                default: return String.Empty;
            }
        }

        public AppOperation(DevicePortal portal)
        {
            if (portal == null)
                throw new System.ArgumentNullException("Must specify a valid DevicePortal object");

            _portal = portal;
            _runningOperation = new SemaphoreSlim(1, 1);
        }

        public void ExecuteOperation(Operation op, ParameterHelper parameters)
        {
            ExecuteOperationInternal(op, parameters);
        }

        public static bool TryExecuteApplicationOperation(DevicePortal portal, ParameterHelper parameters)
        {
            try
            {
                var appOperation = new AppOperation(portal);
                appOperation.ExecuteOperation(AppOperation.OperationStringToEnum(parameters.GetParameterValue(ParameterHelper.Operation)), parameters);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }


        // Class fields
        private DevicePortal _portal;
        private SemaphoreSlim _runningOperation;

        // During "install" operation contains the Identity data extracted from appx Manifest
        private AppPackageIdentity _installedAppId;

        private bool _verbose;
        private bool _helpFlag;

        private ApplicationInstallStatusEventArgs _lastInstallStatus;

        // Internal methods
        private void ExecuteOperationInternal(Operation op, ParameterHelper parameters)
        {
            if (!_runningOperation.Wait(0))
            {
                throw new SemaphoreFullException("Existing operation still running");
            }

            _verbose = parameters.HasFlag(ParameterHelper.VerboseFlag);
            _helpFlag = parameters.HasFlag(ParameterHelper.HelpFlag);

            try
            {
                if (_helpFlag)
                {
                    OutputOperationSpecificUsageText(op);
                    return;
                }

                switch (op)
                {
                    case Operation.ListInstalledApps:
                        ExecuteListInstalledAppsOperation(parameters);
                        break;

                    case Operation.Install:
                        ExecuteInstallOperation(parameters);

                        // Optionally run app on the device if "/launch" switch used
                        if (parameters.HasFlag(ParameterLaunch))
                        {
                            ExecuteRunOperation(parameters);
                        }
                        break;

                    case Operation.Run:
                        ExecuteRunOperation(parameters);
                        break;

                    case Operation.Uninstall:
                        ExecuteUninstallyOperation(parameters);
                        break;

                    default:
                        OutputDefaultUsageText();
                        break;
                }
            }
            catch (Exception ex)
            {
                var errorMessage = new StringBuilder();
                if (_verbose)
                {
                    errorMessage.Append(ex.ToString());
                    errorMessage.Append("\n\n");
                }
                else
                {
                    string message = ex.Message;
                    if (String.IsNullOrWhiteSpace(message))
                    {
                        message = "No exception message";
                    }
                    errorMessage.Append("App operation '" + op.ToString() + "' failed: " + message + "\n\n");
                }

                var wdpEx = ex as DevicePortalException;
                if (wdpEx != null)
                {
                    errorMessage.Append("DevicePortal exception details: \n");
                    errorMessage.Append("RequestURI: " + wdpEx.RequestUri + "\n");
                    errorMessage.Append("Reason: " + wdpEx.Reason + "\n");
                    errorMessage.Append("HTTP Status: " + wdpEx.StatusCode.ToString() + "\n");
                    errorMessage.Append("\n");
                }

                Console.Out.WriteLine(errorMessage.ToString());
                throw;
            }
            finally
            {
                _runningOperation.Release();
            }
        }

        private void OutputOperationSpecificUsageText(Operation op)
        {
            switch (op)
            {

                case Operation.ListInstalledApps:
                    Console.Out.WriteLine(ListAppsOpUsageTExt);
                    break;

                case Operation.Install:
                    Console.Out.WriteLine(InstallOpUsageText);
                    break;

                case Operation.Run:
                    Console.Out.WriteLine(RunOpUsageText);
                    break;

                case Operation.Uninstall:
                    Console.Out.WriteLine(UninstallOpUsageText);
                    break;

                default:
                    OutputDefaultUsageText();
                    break;
            }
        }

        private void OutputDefaultUsageText()
        {
            string errorMessage = "";

            if (!_helpFlag)
            {
                errorMessage = "Invalid App operation\n";
            }

            Console.Out.WriteLine(errorMessage);
            Console.Out.WriteLine(AppOperationUsageText);
        }

        private void ExecuteListInstalledAppsOperation(ParameterHelper parameters)
        {
            Task<AppPackages> packagesTask = _portal.GetInstalledAppPackagesAsync();
            packagesTask.Wait();

            var packages = packagesTask.Result;
            Console.Out.WriteLine(packages.ToString());
        }

        private void ExecuteInstallOperation(ParameterHelper parameters)
        {
            // Parse app and dependency filenames from the parameters
            string appxFile = parameters.GetParameterValue(ParameterAppx);
            string certificate = parameters.GetParameterValue(ParameterCert);

            _portal.AppInstallStatus += OnAppInstallStatus;
            _lastInstallStatus = null;
            try
            {
                if (!String.IsNullOrWhiteSpace(appxFile))
                {
                    ExecuteInstallAppx(parameters, appxFile, certificate);
                }
                else
                {
                    throw new System.ArgumentNullException("Must specify an appx file to install");
                }
            }
            finally
            {
                _portal.AppInstallStatus -= OnAppInstallStatus;
                _lastInstallStatus = null;
            }
        }

        private void ExecuteInstallAppx(ParameterHelper parameters, string appxFile, string certificate)
        {
            if (_verbose)
            {
                Console.Out.WriteLine("Starting Appx installation...");
            }

            var file = new FileInfo(Path.GetFullPath(appxFile));
            if (!file.Exists)
            {
                throw new System.IO.FileNotFoundException("Specified appx file '" + appxFile + "' wasn't found");
            }

            if (!String.IsNullOrWhiteSpace(certificate))
            {
                var certFile = new FileInfo(certificate);
                if (!certFile.Exists)
                {
                    throw new System.IO.FileNotFoundException("Specified certificate file '" + certFile + "' wasn't found");
                }
                certificate = certFile.FullName;
            }
            else
            {
                // Must pass in null instead of empty string if certificate is omitted
                certificate = null;
            }

            // Parse the AppxManfest contained in the Appx file to extract the package name, dependencies, AppID, etc.
            AppxManifest appxData;
            try
            {
                appxData = AppxManifest.Get(appxFile);
            }
            catch (Exception ex)
            {
                Console.Out.WriteLine("Failed to parse Appx manifest: " + ex.Message);
                throw;
            }
            if (!appxData.IsValid)
            {
                throw new System.ArgumentException("Specified Appx '" + appxFile + "' contains an invalid AppxManifest");
            }

            // Construct an "identity" object from the AppxManifest data which can be referenced later to launch the installed app
            var appIdentity = new AppPackageIdentity(appxData);

            // Query for app packages already installed on the device and uninstall them if necessary
            // NOTE: Check for any uninstall any package matching this appx PackageName and Publisher to
            //  ensure a clean install of the new build

            List<PackageInfo> matchingPackages;
            if (TryRetrieveInstalledPackages(appIdentity, out matchingPackages) && matchingPackages.Count() > 0)
            {
                if (_verbose)
                {
                    Console.Out.WriteLine("Uninstalling previous app..");
                }

                foreach (var package in matchingPackages)
                {
                    try
                    {
                        if (_verbose)
                        {
                            Console.Out.WriteLine("Uninstalling package: " + package.FullName);
                        }
                        Task uninstallTask = _portal.UninstallApplicationAsync(package.FullName);
                    }
                    catch (AggregateException ex)
                    {
                        // NOTE: We really shouldn't continue with installation if we failed to remove a previous version of the app.
                        // If a version of the app remains on the device, the Install API will NOT replace it but still reports "success",
                        // meaning the user could be running old code and not know it. A hard fail is the only way to ensure this doesn't happen.
                        Console.Out.WriteLine("Uninstall of package '" + package.FullName + "' failed: " + ex.InnerException.Message);
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    }
                }

                if (_verbose)
                {
                    Console.Out.WriteLine("Finished uninstalling previous app packages");
                }
            }

            Task installTask = _portal.InstallApplicationAsync(null, file.FullName, appxData.Dependencies, certificate, 500, 1, false);
            try
            {
                installTask.Wait();
                Console.Out.WriteLine("Installation completed successfully");
            }
            catch (AggregateException ex)
            {
                Console.Out.WriteLine("Installation of Appx failed!");

                HandleInstallOperationException(ex);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }

            // Save AppIdentity to field after successful installation
            _installedAppId = appIdentity;

            // If the app Identity is "complete" we have the FullPackageName and AUMID parameters, so we'll
            // add them to our parameter set to later launch the app; no need to query package info from the device
            if (_installedAppId.CompleteIdentity)
            {
                parameters.AddOrUpdateParameter(parameterPackage, _installedAppId.PackageFullName);
                parameters.AddOrUpdateParameter(ParameterAumid, _installedAppId.LaunchId);
            }
        }

        private void HandleInstallOperationException(AggregateException ex)
        {
            if (ex == null) return;

            // If available, log the last status update before the exception
            if (_lastInstallStatus != null)
            {
                string errorMessage;
                errorMessage = String.Format("Installation failed in phase {0} - last status: {1}", _lastInstallStatus.Phase, _lastInstallStatus.Message);
                if (_verbose)
                {
                    Console.Out.WriteLine(errorMessage);
                }
            }

            // If multiple exception were encountered we want to log each of them
            // The "main" exception will be handled by the top-level ExecuteOperation method
            if (ex.InnerExceptions.Count > 1)
            {
                var sb = new StringBuilder();
                sb.Append("Multiple exception were thrown during operation:\n");

                foreach (var item in ex.InnerExceptions)
                {
                    sb.Append("  " + item.Message + "\n");
                }

                if (_verbose)
                {
                    Console.Out.WriteLine(sb.ToString());
                }
            }
        }

        private void ExecuteRunOperation(ParameterHelper parameters)
        {
            string packageName = parameters.GetParameterValue(parameterPackage);
            string launchId = parameters.GetParameterValue(ParameterAumid);

            // These parameters are required unless we just performed and install operation
            if (String.IsNullOrWhiteSpace(packageName) && _installedAppId == null)
            {
                throw new System.ArgumentException("Must provide full name of app package to launch");
            }
            if (String.IsNullOrWhiteSpace(launchId) && _installedAppId == null)
            {
                throw new System.ArgumentException("Must provide the AUMID of the app to launch from the specified package");
            }

            // Just installed an app but unable to retrieve package's full name and/or family name
            // So we'll try to query the values from the remote device
            if (_installedAppId != null && !_installedAppId.CompleteIdentity)
            {

                if (!TryRetrievePackageNameAndLaunchIdFromDevice(_installedAppId, 4, out packageName, out launchId))
                {
                    throw new System.Exception("Failed to retrieve necessary app package info from the remote device; cannot launch app");
                }
            }
            else if (_installedAppId != null)
            {
                // If we just installed the app, must wait until it's fully installed/registered with the OS, otherwise launch operation will fail
                WaitUnilAppIsFullyInstalled(packageName);
            }
            
            Task launchTask = _portal.LaunchApplicationAsync(launchId, packageName);
            try
            {
                launchTask.Wait();

                Console.WriteLine("Application launched");
            }
            catch (AggregateException ex)
            {
                Console.Out.WriteLine("App launch failed!");

                HandleInstallOperationException(ex);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }

        private void ExecuteUninstallyOperation(ParameterHelper parameters)
        {
            string package = parameters.GetParameterValue(parameterPackage);

            if (String.IsNullOrWhiteSpace(package))
            {
                throw new System.ArgumentException("Must provide full name of app package to uninstall");
            }

            Task installTask = _portal.UninstallApplicationAsync(package);
            try
            {
                installTask.Wait();
                Console.Out.WriteLine("Uninstall completed successfully");
            }
            catch (AggregateException ex)
            {
                Console.Out.WriteLine("Uninstall of app failed!");

                HandleInstallOperationException(ex);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }

        private void OnAppInstallStatus(object sender, ApplicationInstallStatusEventArgs args)
        {
            if (_verbose)
            {
                Console.Out.WriteLine(args.Message);
            }
            _lastInstallStatus = args;
        }

        private bool TryRetrievePackageNameAndLaunchIdFromDevice(AppPackageIdentity packageId, int numAttempts, out string packageFullName, out string launchId)
        {
            // Already have the info locally and don't need to query from device
            if (packageId.CompleteIdentity)
            {
                packageFullName = packageId.PackageFullName;
                launchId = packageId.LaunchId;
                return true;
            }

            if (_verbose)
            {
                Console.Out.WriteLine("Attempting to query PackageFullName and AUMID from remote device");
            }

            packageFullName = String.Empty;
            launchId = String.Empty;
            bool successful = false;

            while (numAttempts > 0 && !successful)
            {
                numAttempts--;

                try
                {
                    Task<AppPackages> packagesTask = _portal.GetInstalledAppPackagesAsync();
                    packagesTask.Wait();

                    // This basic query should provide the matching package in most cases
                    //  -PackageName must exactly match
                    //  -Publisher must exactly match
                    //  -AUMID (called "AppId" in PackageInfo class) must contain our AppId value (registered app entry point)
                    //  -Version string must exactly match
                    var matchingPackages =
                        (from package in packagesTask.Result.Packages
                         where
                             package.Name == packageId.PackageName &&
                             package.Publisher == package.Publisher &&
                             package.AppId.Contains(package.AppId) &&        // AppId from PackageInfo is actually full AUMID
                             package.Version.ToString() == packageId.Version
                         select package).ToList();

                    PackageInfo matchingPackage = null;

                    if (matchingPackages.Count() == 0)
                    {
                        if (_verbose)
                        {
                            Console.Out.Write("Failed to find '" + packageId.PackageName + "' package installed on the device...");
                            Console.Out.WriteLine((numAttempts > 0) ? "trying again" : "giving up");
                        }
                    }
                    else if (matchingPackages.Count() > 1)
                    {
                        // It's technically possible for the above query to return multiple packages, in which case we need to
                        // disambiguate using the optional package identifiers (CPU architecture and ResourceId).
                        // Since PackageInfo doesn't provide this fields directly, need to split PackageFullName
                        // into component its component parts =>
                        //  [0] PackageName
                        //  [1] Version
                        //  [2] CPU architecture
                        //  [3] ResourceId (if present)
                        //  [4] Publisher name hash

                        foreach (var package in matchingPackages)
                        {
                            var nameParts = package.FullName.Split(new char[] { '_' }, 5);
                            if (nameParts.Length < 5) continue;

                            // ResoruceId will be an empty string if not present, which should match our manifest data
                            if (nameParts[2] == packageId.CpuArchitecture && nameParts[3] == packageId.ResourceId)
                            {
                                matchingPackage = package;
                                break;
                            }
                        }
                    }
                    else matchingPackage = matchingPackages.First();

                    if (matchingPackage != null)
                    {
                        packageFullName = matchingPackage.FullName;
                        launchId = matchingPackage.AppId;
                        successful = true;
                    }

                    // Query attempt may failed because it a few seconds before newly installed apps are reported
                    // So if we have more attempts then wait a bit before trying again
                    if (numAttempts > 0 && !successful)
                    {
                        Thread.Sleep(2000);
                    }
                }
                catch (Exception ex)
                {
                    if (_verbose)
                    {
                        Console.Out.WriteLine("Failed to acquire list of installed apps from device: " + ex.Message);
                    }
                }
            }

            if (_verbose)
            {
                if (!successful)
                {
                    Console.Out.WriteLine("Failed to retrieve FullPackageName and AUMID from remote device");
                }
                else Console.Out.WriteLine("Successfully retrieved FullPackageName and AUMID from remote device");
            }

            return successful;
        }

        private bool TryRetrieveInstalledPackages(AppPackageIdentity packageId, out List<PackageInfo> matchingPackages)
        {
            if (_verbose)
            {
                Console.Out.WriteLine("Attempting to query installed packages matching app");
            }

            matchingPackages = null;
            bool successful = false;

            try
            {
                Task<AppPackages> packagesTask = _portal.GetInstalledAppPackagesAsync();
                packagesTask.Wait();

                // We want to find all packages that *loosely* match our appx in case something like
                // architecture or configuration changed
                matchingPackages =
                    (from package in packagesTask.Result.Packages
                     where
                         package.Name == packageId.PackageName &&
                         package.Publisher == package.Publisher
                     select package).ToList();

                successful = true;
            }
            catch (Exception ex)
            {
                if (_verbose)
                {
                    Console.Out.WriteLine("Failed to acquire list of installed apps from device: " + ex.Message);
                }
            }

            if (_verbose)
            {
                if (!successful)
                {
                    Console.Out.WriteLine("Failed to retrieve installed packages from the device");
                }
                else Console.Out.WriteLine("Successfully retrieved installed packages matching app from the device");
            }

            return successful;
        }

        private void WaitUnilAppIsFullyInstalled(string fullPackageName)
        {
            PackageInfo appPackage = null;
            int numAttempts = 5;

            // DevicePortal API has an annoying quirk in which the "install" call will return before the app is fully ready
            // on the remote device. It takes a few extra seconds before Windows can launch it, and attempting to launch the app before
            // it's ready results in an error. So, wait until we can successfully query the package from the device using the "list"
            // operation, once we see it in the returned results we know the app is ready and can be launched.

            do
            {
                Thread.Sleep(3000);

                try
                {
                    Task<AppPackages> packagesTask = _portal.GetInstalledAppPackagesAsync();
                    packagesTask.Wait();

                    appPackage = packagesTask.Result.Packages.FirstOrDefault(package => (package.FullName == fullPackageName));
                }
                catch {; }

                numAttempts--;

            } while (appPackage == null && numAttempts > 0);
        }
    }
}
