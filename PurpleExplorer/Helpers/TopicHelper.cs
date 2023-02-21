namespace PurpleExplorer.Helpers;

using System;
using PurpleExplorer.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AvaloniaEdit.Utils;
using Message = PurpleExplorer.Models.Message;

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ReactiveUI;

public class TopicHelper : BaseHelper, ITopicHelper
{
    private readonly AppSettings _appSettings;

    public TopicHelper(AppSettings appSettings)
    {
        _appSettings = appSettings;
    }

    public async Task<IList<ServiceBusTopic>> GetTopicsAndSubscriptions(ServiceBusConnectionString connectionString)
    {
        var client = GetManagementClient(connectionString);
        var topics = await GetTopicsWithSubscriptions(client);
        return topics;
    }

    private async Task<ServiceBusTopic> CreateTopicWithSubscriptions(ServiceBusAdministrationClient client, TopicProperties topicDescription)
    {
        var topic = new ServiceBusTopic(topicDescription);
        var subscriptions = await GetSubscriptions(client, topicDescription.Name);
        topic.AddSubscriptions(subscriptions.ToArray());
        return topic;
    }

    private async Task<List<ServiceBusTopic>> GetTopicsWithSubscriptions(ServiceBusAdministrationClient client)
    {
        var topics = new List<ServiceBusTopic>();
        var numberOfPages = _appSettings.TopicListFetchCount / MaxRequestItemsPerPage;
        var remainder = _appSettings.TopicListFetchCount % (numberOfPages * MaxRequestItemsPerPage);

        var pages = client.GetTopicsAsync()
            .AsPages(pageSizeHint: MaxRequestItemsPerPage);

        await foreach (var page in pages)
        {
            foreach (var topicProperty in page.Values)
            {
                var topic = await CreateTopicWithSubscriptions(client, topicProperty);
                topics.Add(topic);
            }
        }

        return topics;
    }

    private async Task<IList<ServiceBusSubscription>> GetSubscriptions(
        ServiceBusAdministrationClient client,
        string topicPath)
    {
        IList<ServiceBusSubscription> subscriptions = new List<ServiceBusSubscription>();
        var topicSubscription = client.GetSubscriptionsRuntimePropertiesAsync(topicPath).AsPages();

        await foreach (var sub in topicSubscription)
        {
            subscriptions.AddRange(sub.Values.Select(x => new ServiceBusSubscription(x)));
        }

        return subscriptions;
    }

    public async Task<IList<Message>> GetMessagesBySubscription(ServiceBusConnectionString connectionString,
        string topicName,
        string subscriptionName)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            topicName,
            subscriptionName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        
        var subscriptionMessages = await receiver.PeekMessagesAsync(_appSettings.TopicMessageFetchCount);
        var result = subscriptionMessages.Select(message => new Message(message, false)).ToList();
        return result;
    }

    public async Task<IList<Message>> GetDlqMessages(
        ServiceBusConnectionString connectionString,
        string topic,
        string subscription)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            topic,
            subscription,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.DeadLetter,
            });

        var receivedMessages = await receiver.PeekMessagesAsync(_appSettings.TopicMessageFetchCount);
        await receiver.CloseAsync();

        var result = receivedMessages.Select(message => new Message(message, true)).ToList();
        return result;
    }

    public async Task<NamespaceProperties> GetNamespaceInfo(ServiceBusConnectionString connectionString)
    {
        var client = GetManagementClient(connectionString);
        return await client.GetNamespacePropertiesAsync();
    }

    public async Task SendMessage(ServiceBusConnectionString connectionString, string topicPath, string content)
    {
        var message = new  ServiceBusMessage(body:Encoding.UTF8.GetBytes(content));
        await SendMessage(connectionString, topicPath, message);
    }

    public async Task SendMessage(
        ServiceBusConnectionString connectionString,
        string topicPath,
        ServiceBusMessage message)
    {
        await using (var topicClient = this.GetBusClient(connectionString))
        {
            topicClient.CreateSender(topicPath).SendMessageAsync(message);
        }
    }

    public async Task DeleteMessage(ServiceBusConnectionString connectionString, string topicPath,
        string subscriptionPath,
        Message message, bool isDlq)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            topicPath,
            subscriptionPath,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = isDlq ? SubQueue.DeadLetter : SubQueue.None,
            });
        
        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(_appSettings.TopicMessageFetchCount);
            if (messages == null || messages.Count == 0)
            {
                break;
            }

            var foundMessage = messages.FirstOrDefault(m => m.MessageId.Equals(message.MessageId));
            if (foundMessage != null)
            {
                await receiver.CompleteMessageAsync(foundMessage);
                break;
            }
        }
    }

    public async Task<long> PurgeMessages(ServiceBusConnectionString connectionString, string topicPath,
        string subscriptionPath,
        bool isDlq)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            topicPath,
            subscriptionPath,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
                SubQueue = isDlq ? SubQueue.DeadLetter : SubQueue.None,
            });

        long purgedCount = 0;
        var operationTimeout = TimeSpan.FromSeconds(5);
        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(_appSettings.TopicMessageFetchCount, operationTimeout);
            if (messages == null || messages.Count == 0)
            {
                break;
            }

            purgedCount += messages.Count;
        }

        return purgedCount;
    }

    public async Task<long> TransferDlqMessages(ServiceBusConnectionString connectionString, string topicPath,
        string subscriptionPath)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            topicPath,
            subscriptionPath,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
                SubQueue = SubQueue.DeadLetter,
            });
        await using var sender = client.CreateSender(topicPath);

        long transferredCount = 0;
        var operationTimeout = TimeSpan.FromSeconds(5);
        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(_appSettings.TopicMessageFetchCount, operationTimeout);
            if (messages == null || messages.Count == 0)
            {
                break;
            }

            await sender.SendMessagesAsync(messages.Select(x => new ServiceBusMessage(x)));

            transferredCount += messages.Count;
        }
        

        return transferredCount;
    }

    private async Task<ServiceBusReceivedMessage> PeekDlqMessageBySequenceNumber(ServiceBusConnectionString connectionString,
        string topicPath,
        string subscriptionPath, long sequenceNumber)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            topicPath,
            subscriptionPath,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.DeadLetter,
            });
        
        var azureMessage = await receiver.PeekMessageAsync(sequenceNumber);
        return azureMessage;
    }

    public async Task ResubmitDlqMessage(ServiceBusConnectionString connectionString, string topicPath,
        string subscriptionPath,
        Message message)
    {
        var azureMessage = await this.PeekDlqMessageBySequenceNumber(
            connectionString,
            topicPath,
            subscriptionPath,
            message.SequenceNumber);

        await SendMessage(connectionString, topicPath, new ServiceBusMessage(azureMessage));
        await DeleteMessage(connectionString, topicPath, subscriptionPath, message, true);
    }

    public async Task DeadletterMessage(ServiceBusConnectionString connectionString, string topicPath,
        string subscriptionPath,
        Message message)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            topicPath,
            subscriptionPath,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
            });

        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(_appSettings.TopicMessageFetchCount);
            if (messages == null || messages.Count == 0)
            {
                break;
            }

            var foundMessage = messages.FirstOrDefault(m => m.MessageId.Equals(message.MessageId));
            if (foundMessage != null)
            {
                await receiver.DeadLetterMessageAsync(foundMessage);
                break;
            }
        }

        await receiver.CloseAsync();
    }
}