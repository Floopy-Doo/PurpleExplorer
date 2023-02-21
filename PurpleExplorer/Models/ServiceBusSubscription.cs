namespace PurpleExplorer.Models;

using Azure.Messaging.ServiceBus.Administration;

public class ServiceBusSubscription : MessageCollection
{
    public string Name { get; set; }
       
    public ServiceBusTopic Topic { get; set; }

    public ServiceBusSubscription(SubscriptionRuntimeProperties subscription)
        : base(subscription.ActiveMessageCount, subscription.DeadLetterMessageCount)
    {
        Name = subscription.SubscriptionName;
    }
}