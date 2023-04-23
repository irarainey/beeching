## 0.3.0

- Amended core axe logic to only axe individual resources and not resource groups and resources in the same run
- Added `--resource-group` option to axe just resource groups an all their contents
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
