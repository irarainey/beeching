## 0.5.0

- Added a check to notify when a new version is available
- Added display of subscription name with subscription id
- Added functionality to determine role assignments and detect if you will be able to axe resources

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
