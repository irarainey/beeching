## 1.0.0

- Upgraded target framework from .NET 6.0 / 7.0 to .NET 10.0
- Replaced Newtonsoft.Json with built-in System.Text.Json for all serialization
- Replaced Polly, Polly.Extensions.Http, and Microsoft.Extensions.Http.Polly with Microsoft.Extensions.Http.Resilience
- Replaced NuGet.Protocol SDK with a lightweight HTTP call for version checking
- Updated Azure.Identity from 1.9.0 to 1.21.0
- Updated Spectre.Console and Spectre.Console.Cli from 0.47.0 to 0.55.0
- Replaced dynamic role assignment deserialization with typed models
- Fixed shared ResourceLock mutation bug when the same lock applied to multiple resources
- Fixed duplicate resources appearing when searching by multiple name patterns
- Fixed missing API response status checks on resource discovery calls
- Fixed OData injection risk in tag and name filter queries
- Fixed status code comparisons to use enum values instead of string matching
- Fixed version comparison arithmetic that broke for minor/patch versions above 99
- Fixed error handling in subscription ID resolution to show a clean message
- Made CallAzCliRest private to prevent misuse
- Replaced mutable static state in AzCliHelper with thread-safe lazy initialization
- Moved per-request auth headers to avoid mutating shared HttpClient DefaultRequestHeaders
- Removed dead code: unused variables, unreachable branches, and redundant null checks
- Separated runtime state from AxeSettings into a new AxeContext model
- Refactored Axe.cs into focused helper classes: ArmClient, ResourceDiscoveryHelper, RoleHelper, and LockHelper
- Added caching for API version lookups to avoid redundant HTTP calls per provider
- Added caching for role definition lookups to avoid re-fetching built-in roles
- Removed duplicate lock skip messaging between lock detection and resource display
- Fixed sync-over-async call in delete failure handling
- Returns non-zero exit code when axe fails partially or fully
- Fixed skip message ordering so locked resources show the correct reason for skipping
- Added resource deletion ordering: type-based priority (e.g. VMs before NICs/disks) combined with depth-first sorting to handle parent-child dependencies
- Fixed typo "Resouce" to "Resource"

## 0.5.2

- Updated dependencies to resolve security advisory

## 0.5.1

- Added note about case sensitivity of tag keys and values

## 0.5.0

- Added functionality to determine role assignments and detect if you will be able to axe resources or remove locks
- Added a check to notify when a new version is available with an option to ignore if required
- Added display of subscription name with subscription id
- Added display of effective role determined for each resource found

## 0.4.0

- Added a resource lock check so it will not try to remove resources that are locked
- Implemented `--force` option to attempt to axe resources even if they are locked by removing and replacing any locks

## 0.3.0

- Amended core axe logic to only axe individual resources and not resource groups and resources in the same run
- Added the ability to specify multiple resource names to axe
- Added `--resource-group` option to axe just resource groups an all their contents
- Added a notification when a resource cannot be axed due to being locked
- Added a count of the resources being axed to the confirmation prompt
- Added additional validation of resource types to ensure they are in the correct format
- Added additional validation of tag keys and values to ensure they are in the correct format

## 0.2.0

- Added logging of user account being used
- Added better handling of failed delete requests with a retry added in case it's a dependency issue
- Added options to set the amount of retry attempts and the delay between them
- Removed `--quiet` option as it was incompatible with the confirmation prompt
- Improved logging of actions being taken
- Various refactoring to tidy up original code structure

## 0.1.2

- Intial release
