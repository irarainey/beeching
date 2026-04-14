using Beeching.Commands;
using Beeching.Models;
using Spectre.Console;
using System.Text.Json;

namespace Beeching.Helpers
{
    internal class ResourceDiscoveryHelper
    {
        private readonly IArmClient _armClient;
        private readonly Dictionary<string, List<ApiVersion>> _apiVersionCache = new();

        public ResourceDiscoveryHelper(IArmClient armClient)
        {
            _armClient = armClient;
        }

        public async Task<List<Resource>> DiscoverResources(AxeContext context)
        {
            var settings = context.Settings;
            bool useNameFilter = !string.IsNullOrEmpty(settings.Name);
            List<Resource> resourcesFound;

            if (settings.ResourceGroups)
            {
                resourcesFound = useNameFilter
                    ? await FindResourceGroupsByName(settings)
                    : await FindResourceGroupsByTag(settings);
            }
            else
            {
                resourcesFound = useNameFilter
                    ? await FindResourcesByName(settings)
                    : await FindResourcesByTag(settings);

                if (!string.IsNullOrEmpty(settings.ResourceTypes))
                {
                    resourcesFound = FilterByResourceTypes(resourcesFound, settings.ResourceTypes);
                }
            }

            resourcesFound = Deduplicate(resourcesFound);
            resourcesFound = ApplyExclusions(resourcesFound, settings.Exclude);
            resourcesFound = await ResolveApiVersions(resourcesFound, context);

            return resourcesFound;
        }

        private async Task<List<Resource>> FindResourceGroupsByName(AxeSettings settings)
        {
            List<string> names = ParseDelimitedValues(settings.Name);
            List<Resource> found = new();

            var allGroups = await _armClient.GetListAsync<Resource>(
                $"subscriptions/{settings.Subscription}/resourcegroups?api-version=2021-04-01"
            );

            foreach (string name in names)
            {
                AnsiConsole.Markup($"[green]=> Searching for resource groups where name contains [white]{name}[/][/]\n");
                found.AddRange(
                    allGroups.Where(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                );
            }

            return found;
        }

        private async Task<List<Resource>> FindResourceGroupsByTag(AxeSettings settings)
        {
            List<string> tag = ParseDelimitedValues(settings.Tag);
            string sanitizedKey = SanitizeODataValue(tag[0]);
            string sanitizedValue = SanitizeODataValue(tag[1]);

            AnsiConsole.Markup(
                $"[green]=> Searching for resource groups where tag [white]{tag[0]}[/] equals [white]{tag[1]}[/][/]\n"
            );

            return await _armClient.GetListAsync<Resource>(
                $"subscriptions/{settings.Subscription}/resourcegroups?$filter=tagName eq '{sanitizedKey}' and tagValue eq '{sanitizedValue}'&api-version=2021-04-01"
            );
        }

        private async Task<List<Resource>> FindResourcesByName(AxeSettings settings)
        {
            List<string> names = ParseDelimitedValues(settings.Name);
            List<Resource> found = new();

            foreach (string name in names)
            {
                string sanitizedName = SanitizeODataValue(name);
                AnsiConsole.Markup($"[green]=> Searching for resources where name contains [white]{name}[/][/]\n");

                var resources = await _armClient.GetListAsync<Resource>(
                    $"subscriptions/{settings.Subscription}/resources?$filter=substringof('{sanitizedName}',name)&api-version=2021-04-01"
                );

                foreach (var resource in resources)
                {
                    PopulateResourceGroup(resource, settings.Subscription);
                    found.Add(resource);
                }
            }

            return found;
        }

        private async Task<List<Resource>> FindResourcesByTag(AxeSettings settings)
        {
            List<string> tag = ParseDelimitedValues(settings.Tag);
            string sanitizedKey = SanitizeODataValue(tag[0]);
            string sanitizedValue = SanitizeODataValue(tag[1]);

            AnsiConsole.Markup($"[green]=> Searching for resources where tag [white]{tag[0]}[/] equals [white]{tag[1]}[/][/]\n");

            var resources = await _armClient.GetListAsync<Resource>(
                $"subscriptions/{settings.Subscription}/resources?$filter=tagName eq '{sanitizedKey}' and tagValue eq '{sanitizedValue}'&api-version=2021-04-01"
            );

            foreach (var resource in resources)
            {
                PopulateResourceGroup(resource, settings.Subscription);
            }

            return resources;
        }

        private async Task<List<Resource>> ResolveApiVersions(List<Resource> resources, AxeContext context)
        {
            foreach (var resource in resources)
            {
                string[] sections = resource.Id.Split('/');
                if (sections.Length < 5)
                {
                    AnsiConsole.Markup($"[green]=> Unable to parse resource ID for {resource.Name} so will exclude[/]\n");
                    resource.ApiVersion = null;
                    continue;
                }

                string resourceGroup = sections[4];
                string provider;
                string resourceType;

                if (!context.Settings.ResourceGroups)
                {
                    if (sections.Length < 8)
                    {
                        AnsiConsole.Markup($"[green]=> Unable to parse resource ID for {resource.Name} so will exclude[/]\n");
                        resource.ApiVersion = null;
                        continue;
                    }
                    provider = sections[6];
                    resourceType = sections[7];
                    resource.OutputMessage =
                        $"[white]{resource.Type} {resource.Name}[/] [green]in resource group[/] [white]{resourceGroup}[/]";
                }
                else
                {
                    provider = "Microsoft.Resources";
                    resourceType = "resourceGroups";
                    resource.OutputMessage = $"[green]group[/] [white]{resource.Name}[/]";
                }

                string? apiVersion = await GetLatestApiVersion(context, provider, resourceType);

                if (apiVersion == null)
                {
                    AnsiConsole.Markup($"[green]=> Unable to get latest API version for {resource.OutputMessage} so will exclude[/]\n");
                }

                resource.ApiVersion = apiVersion;
            }

            return resources.Where(r => !string.IsNullOrEmpty(r.ApiVersion)).ToList();
        }

        private async Task<string?> GetLatestApiVersion(AxeContext context, string provider, string type)
        {
            if (!_apiVersionCache.TryGetValue(provider, out var allApiVersions))
            {
                using var response = await _armClient.GetAsync(
                    $"subscriptions/{context.Settings.Subscription}/providers/{provider}/resourceTypes?api-version=2021-04-01"
                );

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string apiJson = await response.Content.ReadAsStringAsync();

                if (apiJson.Contains("Microsoft.Resources' does not contain sufficient information to enforce access control policy"))
                {
                    AnsiConsole.Markup(
                        "[green]=>[/] [red]You do not have sufficient permissions determine latest API version. Please check your subscription permissions and try again[/]\n"
                    );
                    return null;
                }

                var result = JsonSerializer.Deserialize<ArmListResponse<ApiVersion>>(apiJson);
                if (result?.Value == null || result.Value.Count == 0)
                {
                    return null;
                }

                allApiVersions = result.Value;
                _apiVersionCache[provider] = allApiVersions;
            }

            var apiTypeVersion = allApiVersions.FirstOrDefault(x => x.ResourceType == type);
            if (apiTypeVersion == null)
            {
                return null;
            }

            return apiTypeVersion.DefaultApiVersion ?? apiTypeVersion.ApiVersions?.FirstOrDefault();
        }

        internal static void PopulateResourceGroup(Resource resource, Guid subscription)
        {
            string[] sections = resource.Id.Split('/');
            if (sections.Length > 4)
            {
                resource.ResourceGroup = $"/subscriptions/{subscription}/resourceGroups/{sections[4]}";
            }
        }

        internal static List<Resource> FilterByResourceTypes(List<Resource> resources, string resourceTypes)
        {
            List<string> allowedTypes = ParseDelimitedValues(resourceTypes);
            AnsiConsole.Markup("[green]=> Restricting resource types to:[/]\n");
            foreach (string type in allowedTypes)
            {
                AnsiConsole.Markup($"\t- [white]{type}[/]\n");
            }
            return resources.Where(r => allowedTypes.Contains(r.Type)).ToList();
        }

        internal static List<Resource> Deduplicate(List<Resource> resources)
        {
            return resources
                .GroupBy(r => r.Id)
                .Select(g => g.First())
                .ToList();
        }

        internal static List<Resource> ApplyExclusions(List<Resource> resources, string exclude)
        {
            if (string.IsNullOrEmpty(exclude))
            {
                return resources;
            }

            List<string> exclusions = ParseDelimitedValues(exclude);
            List<Resource> filtered = resources.Where(r => !exclusions.Contains(r.Name)).ToList();
            foreach (var resource in resources.Except(filtered))
            {
                AnsiConsole.Markup($"[green]=> Excluding [white]{resource.Name}[/][/]\n");
            }
            return filtered;
        }

        internal static List<string> ParseDelimitedValues(string value)
        {
            return string.IsNullOrEmpty(value) ? [] : [.. value.Split(':')];
        }

        internal static string SanitizeODataValue(string value)
        {
            return value.Replace("'", "''");
        }
    }
}
