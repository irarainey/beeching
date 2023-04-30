# Beeching - The Azure Axe

![Beeching](https://raw.githubusercontent.com/irarainey/beeching/main/resources/images/logo_small.png)

Beeching is a command line tool to help you quickly and easily delete Azure resources you no longer need. Insprired by [The Beeching Axe](https://blog.nationalarchives.gov.uk/the-beeching-axe/) it allows you to cull vast numbers of resources across a subscription with a single swing of the axe. It can delete multiple resource types at the same time, based on a name, part of a name, or by tag value.

Resources can be protected from the axe by specifying them in an exclusion list. This allows you to shield resources that you wish to keep. The list of resources can be further restricted to only cull certain types of resource by using another switch.

The tool is written in C# and makes direct the calls to the Azure Management API. It is a .NET 6.0 / 7.0 application and can be run on Windows, Linux and Mac.

## Installation

You can install this tool globally, using the dotnet tool command:

```bash
dotnet tool install --global beeching 
```

When a new version is available, you can use the `dotnet tool update` command to upgrade your installation:

```bash
dotnet tool update --global beeching 
```

## Authentication

To call to `beeching` and swing the axe, you need to run the command from a user account with permissions to delete the specified resources, and resource locks if using the `--force` option. Authentication is performed using the `ChainedTokenCredential` provider which will look for the Azure CLI token first. Make sure to run `az login` (optionally with the `--tenant` parameter) to make sure you have an active session and have the correct subscription selected by using the `az account set` command.

## Usage

You can invoke the axe using the `beeching` command and by specifying your parameters. The most basic usage is to specify the name of the resources you want to axe. This will use your active Azure CLI subscription and will delete all resources that match the name or part of the name. You can use the `axe` command, but this is optional as it is the default command so can be omitted.

```bash
beeching axe --name my-resource
```
 
This is the same as:

```bash
beeching --name my-resource
```

Multiple name values can be supplied in a single string by separating them with the `:` symbol as in this example.

```bash
beeching --name my-resource-001:my-resource-002
```

## Specifying a Subscription Id

You can optionally provide a subscription id, but if you do not specify a subscription, it will use the actively selected subscription from the Azure CLI. Any subscription id you provide must be a valid subscription id for the user currently logged in to the Azure CLI.

## Select Resources by Tag

Resources can also be selected by tags. This will delete all resources that have a tag with the specified key and value. Tags must be supplied as a single string in the format `key:value`.

```bash
beeching --tag key:value
```

## Exclude Resources

Once you have selected the resources you want to axe, you can optionally specify a list of resources to exclude from the axe using the `--exclude` option. This allows you to protect resources you wish to keep.

```bash
beeching --name my-resource --exclude my-resource-to-keep
```

Multiple name values can be supplied in a single string by separating them with the `:` symbol.

```bash
beeching --name my-resource --exclude keep001:keep002
```

## Restrict Resource Types

The list of resources can be further restricted to only cull certain types of resource using the `--resource-types` option. This example will only axe resources of the type `Microsoft.Storage/storageAccounts`.

```bash
beeching --name my-resource --resource-types Microsoft.Storage/storageAccounts
```

Again multiple options can be specified by single string separating them with a `:` symbol, as shown in this example which will axe only storage accounts and virtual networks.

```bash
beeching --name my-resource --resource-types Microsoft.Storage/storageAccounts:Microsoft.Network/virtualNetworks
```

## Resource Groups

By default the axe will only cull individual resource types. If you want to axe an entire resource group and all the resources within it, you can use the `--resource-group` option. This will axe the resource group and all resources in it. This option can be used with the `--name` or `--tag` options to axe resource group that match the name, or partial name, or tag key and value.

```bash
beeching --name my-resource-group --resource-group
```

All of these options can be combined to create a very specific axe that will only delete the resources you want to delete.

## Resource Locks

Resource locks can be applied to Azure resources at the resource, resource group or subscription level. If a resource is locked, it cannot be axed. Beeching will check for resource locks and will not attempt to axe any resources that are locked. If you want to axe a resource that is locked, you will need to remove all applicable locks first, or use the `--force` option to override the locks.

```bash
beeching --name my-resource --force
```

Using the `--force` option will attempt to remove any resource locks before axing the resource. This can be useful if you have a resource that is locked, but you know that it is safe to delete. This option should be used with caution as it can lead to accidental deletion of resources.

Following the axing of a locked resource, any relevant locks, such as subscription locks or resource group locks will be reapplied. This is to prevent accidental deletion of resources that are locked for a reason.

## What If?

It is also possible to use the `--what-if` parameter to see which resources would face the axe. This will show you the list of resources that would be deleted, but will not actually delete anything.

```bash
beeching --name my-resource --what-if
```

## Confirmation

Before any resources are actually deleted, you will be prompted to confirm that you really want to delete the resources. For automated deletion such as in a CI/CD pipeline you can skip this prompt by using the `--yes` parameter.

```bash
beeching --name my-resource --yes
```

## Retries

A built-in retry mechanism is in place to handle transient network errors. By default, the axe will retry each request 3 times at the API level.

Occasionally deletion requests can fail if other dependent resources have yet to be deleted. In this instance a further retry mechanism is in place with will pause for 10 seconds between each retry attempt, and each action will be retried 6 times. These two values are configurable and can be set using the `--max-retry` and `--retry-pause` parameters.

```bash
beeching --name my-resource --max-retry 10 --retry-pause 30
```

## Full List of Options

You can also use the `--help` parameter to get a list of all available options.

```bash
beeching --help
```

Which will show:

```
USAGE:
    beeching [OPTIONS]

OPTIONS:
    -h, --help                Prints this help information
    -s, --subscription        The subscription id to use. If not specified, the active Azure CLI subscription will be used
    -n, --name                The name (or partial name) of the resources to axe. Multiple values can be specified separated by :
    -t, --tag                 The tag key and value of the resources to axe. Must be supplied as a single string in the format key:value
    -r, --resource-types      Restrict the types of the resources to axe. Multiple values can be specified separated by :
    -e, --exclude             The name of resources to exclude from the axe. Multiple values can be specified separated by :
    -g, --resource-groups     Axe resource groups and contents rather than individual resource types
    -f, --force               Force the axe to delete the resources if locked
    -y, --yes                 Skip the confirmation prompt
    -w, --what-if             Show which resources would face the axe without actually culling anything
    -m, --max-retry           Sets the maximum amount to retry attempts when axe fails (default = 6)
    -p, --retry-pause         Sets the pause in seconds for the retry attempts (default = 10)
    -d, --debug               Increase logging verbosity to show all debug logs
    -v, --version             Reports the application version
    -i, --ignore-update       Do not check for update on startup

COMMANDS:
    axe    The mighty axe that culls the resources
```

> If the application is not working properly, you can use the `--debug` parameter to increase the logging verbosity and see more details.

## Disclaimer

**Warning:** This tool does not muck about. It really deletes your resources and resource groups and there is no way to recover them. Make sure you have a backup of your resources before you use this tool. No responsibility is taken for any damage caused by this tool.

Several safety measures are in place to prevent accidental deletion of resources, such as a confirmation prompt, a what-if mode, and exclusion options, but it is still possible to delete resources you did not intend to delete. Unlike the real Beeching Axe there is no option for a heritage railway here. Use at your own risk.