using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Tools.WindowsDevicePortal;

namespace DevicePortalTool
{
    public enum OpperationArea
    {
        None,
        Application,
    };

    public enum ProgramErrorCodes : int
    {
        Success = 0,
        SuccessHelp = 1,
        OperationFailed = -1,
        InvalidParameters = -2,
        AuthenticationFailed = -3,
        ConnectionFailed = -4,
        UnexpectedError = -5,
    };

    class Program
    {
        private static readonly string GeneralUsageMessage =
            "Executes a Windows DevicePortal (WDP) operation on a remote Windows 10 device\n"
            + "\n"
            + "Usage:\n"
            + "DevicePortalTool <operation area> /op:<operation type> [operation parameters]] /ip:<device IP address> [/stdincred | /user:WDP username /pwd:<WDP password] [/v]\n"
            + "    <operation area> - must be one of the following values:\n"
            + "        app - execute an Application related operation\n"
            + "    /op - area specific operation value with additional parameters\n"
            + "    /ip - IP address of remote device to operate on\n"
            + "    /stdincred - WDP credentials are read from stdin stream instead of command line (no prompts)\n"
            + "        credentials read as two separate lines of Base64 encoded strings (user name then password)\n"
            + "    /user - Name of WDP user on the device\n"
            + "    /pwd - Password of WDP user on the device\n"
            + "    /v - Verbose logging output\n"
            + "\n"
            + "DevicePortalTool <operation area> /?\n"
            + "    Display area specific usage\n"
            + "\n"
            + "DevicePortalTool /?\n"
            + "    Display this usage\n"
            ;

        public static OpperationArea OperationAreaStringToEnum(string areaName)
        {
            if (String.IsNullOrWhiteSpace(areaName))
            {
                return OpperationArea.None;
            }
            if (areaName.Equals("app", StringComparison.OrdinalIgnoreCase))
            {
                return OpperationArea.Application;
            }

            return OpperationArea.None;
        }

        public static int Main(string[] args)
        {
            var resultCode = ExecuteMain(args);

            Console.Out.WriteLine("ExitCode: " + (int)resultCode + " (" + resultCode.ToString() + ")");
            return (int)resultCode;
        }

        private static ProgramErrorCodes ExecuteMain(string[] args)
        {
            ParameterHelper parameters;
            ProgramErrorCodes errorCode;
            Uri targetDevice = null;
            bool helpFlag;
            bool verbose;

            errorCode = ParseParametersAndPerformBasicValidation(args, out parameters, out targetDevice, out verbose, out helpFlag);
            if (errorCode != ProgramErrorCodes.Success)
                return errorCode;

            if (!helpFlag && parameters.HasFlag(ParameterHelper.StdinCredentials))
            {
                try
                {
                    string username;
                    string password;

                    if (!TryReadCredentialsFromStdin(out username, out password))
                    {
                        Console.Out.WriteLine("Failed to read WDP credentials from stdin");
                        Console.Out.WriteLine();

                        return ProgramErrorCodes.InvalidParameters;
                    }

                    parameters.AddOrUpdateParameter(ParameterHelper.WdpUser, username);
                    parameters.AddOrUpdateParameter(ParameterHelper.WdpPassword, password);
                }
                catch (Exception ex)
                {
                    Console.Out.WriteLine("Fatal error reading WDP credentials from stdin: " + ex.Message);
                    Console.Out.WriteLine();

                    return ProgramErrorCodes.UnexpectedError;
                }
            }

            DevicePortal portal;
            if (!helpFlag)
            {
                try
                {
                    if (!TryOpenDevicePortalConnection(targetDevice, parameters, out portal))
                    {
                        if (portal != null && portal.ConnectionHttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            Console.Out.WriteLine("Aborting due to failed authentication");
                            Console.Out.WriteLine();
                            return ProgramErrorCodes.AuthenticationFailed;
                        }
                        else
                        {
                            Console.Out.WriteLine("Aborting due to failed connection with remote device");
                            Console.Out.WriteLine();
                            return ProgramErrorCodes.ConnectionFailed;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Out.WriteLine("Fatal error initializing DevicePortal: " + ex.Message);
                    Console.Out.WriteLine();

                    return ProgramErrorCodes.UnexpectedError;
                }
            }
            else
            {
                // Need to create a dummy DevicePortal so can safely call into area-specific operation processor
                // The processor can then output operation specific help info
                portal = new DevicePortal(new DefaultDevicePortalConnection("http://0.0.0.0", "", ""));
            }

            var operationResult = ProgramErrorCodes.Success;
            switch (parameters.Area)
            {
                case OpperationArea.Application:
                    if (!AppOperation.TryExecuteApplicationOperation(portal, parameters))
                    {
                        operationResult = ProgramErrorCodes.OperationFailed;
                    }
                    break;

                default:
                    // This case should have already been handled by a parameter check above
                    operationResult = ProgramErrorCodes.InvalidParameters;
                    break;
            }

            // If successful but help switch was set return a different resultCode
            if (operationResult == ProgramErrorCodes.Success && helpFlag)
            {
                operationResult = ProgramErrorCodes.SuccessHelp;
            }

            // If a debugger is attached, don't close but instead loop here until
            // closed.
            while (System.Diagnostics.Debugger.IsAttached)
            {
                System.Threading.Thread.Sleep(0);
            }

            return operationResult;
        }

        private static ProgramErrorCodes ParseParametersAndPerformBasicValidation(string[] args, out ParameterHelper parameters, out Uri targetDevice, out bool verbose, out bool helpFlag)
        {
            parameters = new ParameterHelper();
            targetDevice = null;
            verbose = false;
            helpFlag = false;

            try
            {
                parameters.ParseCommandLine(args);
            }
            catch (Exception ex)
            {
                Console.Out.WriteLine("Fatal error parsing command arguments: " + ex.Message);
                Console.Out.WriteLine();

                return ProgramErrorCodes.UnexpectedError;
            }

            verbose = parameters.HasFlag(ParameterHelper.VerboseFlag);
            helpFlag = parameters.HasFlag(ParameterHelper.HelpFlag);

            if (parameters.Area == OpperationArea.None)
            {
                if (helpFlag)
                {
                    Console.WriteLine(GeneralUsageMessage);
                    Console.WriteLine();
                }
                else
                {
                    Console.Out.WriteLine("Invalid parameters: Must specify a valid operation area as the first parameter");
                    Console.Out.WriteLine();
                }

                return ProgramErrorCodes.InvalidParameters;
            }

            if (!helpFlag)
            {
                string address = parameters.GetParameterValue(ParameterHelper.DeviceIpAddress);
                string invalidReason = null;

                if (String.IsNullOrWhiteSpace(address))
                {
                    invalidReason = "Must specify IP address of remote Windows Device Portal with /ip switch";
                }
                else if (!Uri.TryCreate(address, UriKind.Absolute, out targetDevice))
                {
                    invalidReason = "isn't a proper URI";
                }
                else if (targetDevice.Scheme != "http" && targetDevice.Scheme != "https")
                {
                    invalidReason = "must specify http or https scheme";
                }
                else if (targetDevice.IsDefaultPort)
                {
                    invalidReason = "doesn't specify a WDP port number";
                }

                if (invalidReason != null)
                {
                    Console.Out.WriteLine("Invalid parameters: IP address '" + address + "'; " + invalidReason);
                    Console.Out.WriteLine("IP address must be in the following format: http(s)://<host address>:<WDP port>");
                    Console.Out.WriteLine("The correct address string can be found under 'Developer' settings on the host device");
                    Console.Out.WriteLine();
                    return ProgramErrorCodes.InvalidParameters;
                }
            }

            if (!helpFlag && parameters.HasFlag(ParameterHelper.StdinCredentials))
            {
                if (parameters.HasParameter(ParameterHelper.WdpUser) || parameters.HasParameter(ParameterHelper.WdpPassword))
                {
                    Console.Out.WriteLine("Invalid parameters: Cannot pass WDP credentials on command line (/user /pwd) when using /stdincred switch; use one method or the other");
                    Console.Out.WriteLine();
                    return ProgramErrorCodes.InvalidParameters;
                }
            }

            return ProgramErrorCodes.Success;
        }

        private static bool TryReadCredentialsFromStdin(out string userName, out string password)
        {
            userName = String.Empty;
            password = String.Empty;

            // We expect username/password (base64 encoded) to be passed "piped" in from a calling process
            // So there's no prompt and we'll fail if don't read anything after a few seconds
            {
                var readTask = Console.In.ReadLineAsync();
                if (!readTask.Wait(5000))
                    return false;

                userName = ParameterHelper.DecodeBase64(readTask.Result);
            }

            {
                var readTask = Console.In.ReadLineAsync();
                if (!readTask.Wait(5000))
                    return false;

                password = ParameterHelper.DecodeBase64(readTask.Result);
            }

            return true;
        }

        private static bool TryOpenDevicePortalConnection(Uri targetDevice, ParameterHelper parameters, out DevicePortal portal)
        {
            string userName = parameters.GetParameterValue(ParameterHelper.WdpUser);
            string password = parameters.GetParameterValue(ParameterHelper.WdpPassword);

            bool success = true;
            portal = new DevicePortal(new DefaultDevicePortalConnection(targetDevice.ToString(), userName, password));
            try
            {
                // We need to handle this event otherwise remote connection will be rejected if
                // device isn't trusted by local PC
                portal.UnvalidatedCert += DoCertValidation;

                var connectTask = portal.ConnectAsync(updateConnection: false);
                connectTask.Wait();

                if (portal.ConnectionHttpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    if (portal.ConnectionHttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        throw new System.UnauthorizedAccessException("Connection rejected due to missing/incorrect credentials; specify valid credentials with /user and /pwd switches");
                    }
                    else if (!string.IsNullOrEmpty(portal.ConnectionFailedDescription))
                    {
                        throw new System.OperationCanceledException(string.Format("WDP connection failed (HTTP {0}) : {1}", (int)portal.ConnectionHttpStatusCode, portal.ConnectionFailedDescription));
                    }
                    else
                    {
                        throw new System.OperationCanceledException(string.Format("WDP connection failed (HTTP {0}) : no additional information", (int)portal.ConnectionHttpStatusCode));
                    }
                }
            }
            catch (Exception ex)
            {
                bool verbose = parameters.HasFlag(ParameterHelper.VerboseFlag);
                Console.Out.WriteLine("Failed to open DevicePortal connection to '" + portal.Address + "'\n" + (verbose ? ex.ToString() : ex.Message));

                success = false;
            }

            return success;
        }

        private static bool DoCertValidation(DevicePortal sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // We're not validating the remote host
            return true;
        }
    }
}
