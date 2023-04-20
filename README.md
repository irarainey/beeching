# Beeching - The Azure Axe

![Beeching](https://raw.githubusercontent.com/irarainey/beeching/main/resources/images/logo_small.png)

Beeching is a command line tool to help you quickly and easily delete Azure resources you no longer need. Insprired by [The Beeching Axe](https://blog.nationalarchives.gov.uk/the-beeching-axe/) it allows you to cull vast numbers of resources across a subscription with a single swing of the axe. It can delete multiple resource types at the same time, based on a name, part of a name, or by tag value.

Resources can be protected from the axe by specifying them in an exclusion list. This allows you to shield resources that you wish to keep. The list of resources can be further restricted to only cull certain types of resource by using another switch.

The tool is written in C# and makes direct the calls to the Azure Management API. It is a .NET Core 6.0 / 7.0 application and can be run on Windows, Linux and Mac.

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

To call to `beeching` and swing the axe, you need to run this from a user account with permissions to delete the specified resources. Authentication is performed using the `ChainedTokenCredential` provider which will look for the Azure CLI token first. Make sure to run `az login` (optionally with the `--tenant` parameter) to make sure you have an active session and have the correct subscription selected by using the `az account set` command.

## Usage

You can invoke the tool using the `beeching` command and by specifying your parameters. The most basic usage is to specify the name of the resources you want to axe. This will use your active Azure CLI subscription and will delete all resources that match the name or part of the name. You can use the `axe` command, but this is optional as it is the default command so can be omitted.

```bash
beeching axe --name my-resource
```
 This is the same as:

 ```bash
beeching --name my-resource
```

You can optionally provide a subscription id, but if you do not specify a subscription, it will use the actively selected subscription from the Azure CLI. Any subscription id you provide must be a valid subscription id for the user currently logged in to the Azure CLI.

Resources can also be selected by tags. This will delete all resources that have a tag with the specified key and value. Tags are supplied as a single string in the format `key:value`.

```bash
beeching --tag key:value
```

Once you have selected the resources you want to axe, you can optionally specify a list of resources to exclude from the axe. This allows you to protect resources that you wish to keep. The list of resources can be further restricted to only cull certain types of resource.

```bash
beeching --name my-resource --exclude my-resource-to-keep --resource-types Microsoft.Storage/storageAccounts
```

 Both of these options can be specified with multiple values separated by the `:` symbol.

```bash
beeching --name my-resource --exclude keep001:keep002 --resource-types Microsoft.Storage/storageAccounts:Microsoft.Network/virtualNetworks
```

All of these options can be combined to create a very specific axe that will only delete the resources you want to delete.

It is also possible to use the `--what-if` parameter to see which resources would face the axe. This will show you the list of resources that would be deleted, but will not actually delete them.

Before any resources are deleted, you will be prompted to confirm that you want to delete the resources. You can skip this prompt by using the `--yes` parameter.

You can also use the `--help` parameter to get a list of all available options.

```bash
beeching --help
```

Which will show:

```
USAGE:
    beeching [OPTIONS]

OPTIONS:
    -s, --subscription     The subscription id to use
    -n, --name             The name (or partial name) of the resources to axe
    -t, --tag              The tag key and value of the resources to axe
    -r, --resource-types   Restrict the types of the resources to axe
    -e, --exclude          The name of the resources to exclude from the axe
    -f, --force            Force the axe to delete the resources if locked
    -y, --yes              Skip the confirmation prompt
    -w, --what-if          Show which resources would face the axe
    -h, --help             Prints help information
    -d, --debug            Increase logging verbosity to show all debug logs
    -v, --version          Reports the application version

COMMANDS:
    axe    The mighty axe that culls the resources
```

> If the application is not working properly, you can use the `--debug` parameter to increase the logging verbosity and see more details.

## Disclaimer

**Warning:** This tool does not muck about. It really deletes your resources and there is no way to recover them. Make sure you have a backup of your resources before you use this tool. No responsibility is taken for any damage caused by this tool.

Several safety measures are in place to prevent accidental deletion of resources, such as a confirmation prompt, a what-if mode, and exclusion options, but it is still possible to delete resources you did not intend to delete. Unlike the real Beeching Axe there is no option for a heritage railway here. Use at your own risk.