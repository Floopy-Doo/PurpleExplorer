using PurpleExplorer.Models;

namespace PurpleExplorer.Helpers;

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

public abstract class BaseHelper
{
    protected const int MaxRequestItemsPerPage = 100;

    private readonly DefaultAzureCredentialOptions _options = new DefaultAzureCredentialOptions
    {
        ExcludeEnvironmentCredential = true,
        ExcludeAzureCliCredential = false,
        ExcludeInteractiveBrowserCredential = true,
        ExcludeManagedIdentityCredential = false,
        ExcludeVisualStudioCredential = true,
        ExcludeAzurePowerShellCredential = true,
        ExcludeSharedTokenCacheCredential = true,
        ExcludeVisualStudioCodeCredential = true,
    };

    protected ServiceBusAdministrationClient GetManagementClient(ServiceBusConnectionString connectionString)
    {
        return connectionString.UseManagedIdentity ? 
            new ServiceBusAdministrationClient (connectionString.ConnectionString, new DefaultAzureCredential(this._options)) : 
            new ServiceBusAdministrationClient (connectionString.ConnectionString);
    }

    protected ServiceBusClient GetBusClient(ServiceBusConnectionString connectionString)
    {
        return connectionString.UseManagedIdentity ? 
            new ServiceBusClient (connectionString.ConnectionString, new DefaultAzureCredential(this._options)) : 
            new ServiceBusClient (connectionString.ConnectionString);
    }
}