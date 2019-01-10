//----------------------------------------------------------------------------------------------
// <copyright file="ParameterHelper.cs" company="Microsoft Corporation">
//     Licensed under the MIT License. See LICENSE.TXT in the project root license information.
// </copyright>
// <copyright file="ParameterHelper.cs" company="Unity Technologies">
//     Modified under the MIT License. See LICENSE.TXT in the project root license information.
// </copyright>
//----------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace DevicePortalTool
{
    public class ParameterHelper
    {
        public static readonly string HelpFlag = "?";
        public static readonly string VerboseFlag = "v";
        public static readonly string Operation = "op";
        public static readonly string DeviceIpAddress = "ip";
        public static readonly string WdpUser = "user";
        public static readonly string WdpPassword = "pwd";
        public static readonly string StdinCredentials = "stdincred";

        public OpperationArea Area { get; private set; } = OpperationArea.None;

        private Dictionary<string, string> parameters = new Dictionary<string, string>();
        private List<string> flags = new List<string>();

        public void AddParameter(string name, string value)
        {
            this.parameters.Add(name, value);
        }

        public void AddOrUpdateParameter(string name, string value)
        {
            if (this.parameters.ContainsKey(name))
            {
                this.parameters[name] = value;
            }
            else this.parameters.Add(name, value);
        }

        public string GetParameterValue(string key)
        {
            if (this.parameters.ContainsKey(key))
            {
                return this.parameters[key];
            }
            else
            {
                return String.Empty;
            }
        }

        public bool HasParameter(string key)
        {
            return this.parameters.ContainsKey(key);
        }

        public bool HasFlag(string flag)
        {
            return this.flags.Contains(flag);
        }

        public void ParseCommandLine(string[] args)
        {
            // If nothing specified then add "help" flag to display usage
            if (args.Length == 0)
            {
                this.flags.Add(ParameterHelper.HelpFlag);
                return;
            }

            // The operation area must always be the 1st parameter
            // NOTE: In C# args[0] is program name
            Area = Program.OperationAreaStringToEnum(args[0]);

            // Parse the command line args
            for (int i = 0; i < args.Length; ++i)
            {
                string arg = args[i];
                if (!arg.StartsWith("/") && !arg.StartsWith("-"))
                {
                    // We expect the first parameter to be the "area" which isn't prefixed with a slash
                    if (i == 0) continue;

                    throw new Exception(string.Format("Unrecognized argument: {0}", arg));
                }

                arg = arg.Substring(1);

                int valueIndex = arg.IndexOf(':');
                string value = null;

                // If this contains a colon, separate it into the param and value. Otherwise add it as a flag
                if (valueIndex > 0)
                {
                    value = arg.Substring(valueIndex + 1);
                    arg = arg.Substring(0, valueIndex);

                    this.parameters.Add(arg.ToLowerInvariant(), value);
                }
                else
                {
                    this.flags.Add(arg.ToLowerInvariant());
                }
            }
        }

        public static string EncodeBase64(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(plainTextBytes);
        }

        public static string DecodeBase64(string base64Text)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64Text);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }
    }
}
