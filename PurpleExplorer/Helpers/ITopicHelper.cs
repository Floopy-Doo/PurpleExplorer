using System.Collections.Generic;
using System.Threading.Tasks;
using PurpleExplorer.Models;

namespace PurpleExplorer.Helpers;

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

public interface ITopicHelper
{
    public Task<NamespaceProperties> GetNamespaceInfo(ServiceBusConnectionString connectionString);
    public Task<IList<ServiceBusTopic>> GetTopicsAndSubscriptions(ServiceBusConnectionString connectionString);
    public Task<IList<Message>> GetDlqMessages(ServiceBusConnectionString connectionString, string topic, string subscription);
    public Task<IList<Models.Message>> GetMessagesBySubscription(ServiceBusConnectionString connectionString, string topicName, string subscriptionName);
    public Task SendMessage(ServiceBusConnectionString connectionString, string topicPath, string content);
    public Task SendMessage(ServiceBusConnectionString connectionString, string topicPath, ServiceBusMessage message);
    public Task DeleteMessage(ServiceBusConnectionString connectionString, string topicPath, string subscriptionPath, Message message, bool isDlq);
    public Task<long> PurgeMessages(ServiceBusConnectionString connectionString, string topicPath, string subscriptionPath, bool isDlq);
    public Task<long> TransferDlqMessages(ServiceBusConnectionString connectionString, string topicPath, string subscriptionPath);
    public Task ResubmitDlqMessage(ServiceBusConnectionString connectionString, string topicPath, string subscriptionPath,
        Message message);
    public Task DeadletterMessage(ServiceBusConnectionString connectionString, string topicPath, string subscriptionPath,
        Message message);
}