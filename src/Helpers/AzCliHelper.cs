using Beeching.Models;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace Beeching.Helpers
{
    internal static class AzCliHelper
    {
        private static string _azCliExecutable = "az";

        internal static string DetermineAzCliPath()
        {
            using Process process = CreateProcess(
                Environment.OSVersion.Platform == PlatformID.Win32NT ? "where" : "which",
                _azCliExecutable
            );

            process.Start();
            string processOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                _azCliExecutable = processOutput.Split(Environment.NewLine)[0].Trim();
            }

            return _azCliExecutable
                + (Environment.OSVersion.Platform == PlatformID.Win32NT && _azCliExecutable.EndsWith(".cmd") == false ? ".cmd" : "");
        }

        internal static string GetDefaultAzureSubscriptionId()
        {
            string azCliExecutable = DetermineAzCliPath();
            using Process process = CreateProcess(azCliExecutable, "account show");

            process.Start();
            string processOuput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                using var jsonOutput = JsonDocument.Parse(processOuput);
                JsonElement root = jsonOutput.RootElement;
                if (root.TryGetProperty("id", out JsonElement idElement))
                {
                    return idElement.GetString() ?? "";
                }
                else
                {
                    throw new Exception("Unable to find the 'id' property in the JSON output.");
                }
            }
            else
            {
                string error = process.StandardError.ReadToEnd();
                throw new Exception($"Error executing '{azCliExecutable} account show': {error}");
            }
        }

        internal static List<RoleAssignment> GetRoleAssignments(string principalId)
        {
            string azCliExecutable = DetermineAzCliPath();
            using Process process = CreateProcess(azCliExecutable, $"role assignment list --all --assignee {principalId}");
            process.Start();
            string processOuput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            List<RoleAssignment> roles = new ();
            if (process.ExitCode == 0)
            {
                using var jsonOutput = JsonDocument.Parse (processOuput);
                JsonElement root = jsonOutput.RootElement;
                foreach (JsonElement element in root.EnumerateArray ())
                {
                    roles.Add (new RoleAssignment
                    {
                        Id = element.GetProperty ("id").GetString () ?? "",
                        PrincipalType = element.GetProperty ("principalType").GetString () ?? "",
                        PrincipalName = element.GetProperty ("principalName").GetString () ?? "",
                        RoleDefinitionId = element.GetProperty ("roleDefinitionId").GetString () ?? "",
                        RoleDefinitionName = element.GetProperty ("roleDefinitionName").GetString () ?? "",
                        Scope = element.GetProperty ("scope").GetString () ?? "",
                    });
                }
            }

            return roles;
        }

        internal static (string, string) GetSignedInUser()
        {
            (string, string) result = ("", "");
            string azCliExecutable = DetermineAzCliPath();
            using Process process = CreateProcess(azCliExecutable, "ad signed-in-user show");

            process.Start();
            string processOuput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                using var jsonOutput = JsonDocument.Parse(processOuput);
                JsonElement root = jsonOutput.RootElement;
                if (root.TryGetProperty("id", out JsonElement idElement))
                {
                    result.Item1 = idElement.GetString() ?? "";
                }
                else
                {
                    result.Item1 = "";
                }

                if (root.TryGetProperty ("displayName", out JsonElement displayNameElement))
                {
                    result.Item2 = displayNameElement.GetString () ?? "";
                }
                else
                {
                    result.Item2 = "";
                }

                return result;
            }
            else
            {
                string error = process.StandardError.ReadToEnd();
                throw new Exception($"Error executing '{azCliExecutable} ad signed-in-user show': {error}");
            }
        }

        private static Process CreateProcess(string filename, string arguments)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = filename,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
        }
    }
}
