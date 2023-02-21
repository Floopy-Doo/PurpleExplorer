namespace PurpleExplorer.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Models;

public class QueueHelper : BaseHelper, IQueueHelper
{
    private readonly AppSettings _appSettings;

    public QueueHelper(AppSettings appSettings) => this._appSettings = appSettings;

    public async Task<IList<ServiceBusQueue>> GetQueues(ServiceBusConnectionString connectionString)
    {
        var client = this.GetManagementClient(connectionString);
        var queues = await this.GetQueues(client);
        return queues;
    }

    public async Task SendMessage(ServiceBusConnectionString connectionString, string queueName, string content)
    {
        var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(content));
        await this.SendMessage(connectionString, queueName, message);
    }

    public async Task SendMessage(
        ServiceBusConnectionString connectionString,
        string queueName,
        ServiceBusMessage message)
    {
        await using var client = this.GetBusClient(connectionString);
        var sender = client.CreateSender(queueName);
        await sender.SendMessageAsync(message);
    }

    public async Task<IList<Message>> GetMessages(ServiceBusConnectionString connectionString, string queueName)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            queueName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        var messages = await receiver.PeekMessagesAsync(this._appSettings.QueueMessageFetchCount);
        return messages.Select(msg => new Message(msg, false)).ToList();
    }

    public async Task<IList<Message>> GetDlqMessages(ServiceBusConnectionString connectionString, string queueName)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            queueName,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.DeadLetter,
            });
        var messages = await receiver.PeekMessagesAsync(this._appSettings.QueueMessageFetchCount);

        return messages.Select(message => new Message(message, true)).ToList();
    }

    public async Task DeadletterMessage(ServiceBusConnectionString connectionString, string queue, Message message)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            queue,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });

        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(this._appSettings.QueueMessageFetchCount);
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
    }

    public async Task DeleteMessage(
        ServiceBusConnectionString connectionString,
        string queue,
        Message message,
        bool isDlq)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            queue,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = isDlq ? SubQueue.DeadLetter : SubQueue.None,
            });

        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(this._appSettings.QueueMessageFetchCount);
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

    public async Task ResubmitDlqMessage(ServiceBusConnectionString connectionString, string queue, Message message)
    {
        var azureMessage = await this.PeekDlqMessageBySequenceNumber(
            connectionString,
            queue,
            message.SequenceNumber);
        var clonedMessage = new ServiceBusMessage(azureMessage);

        await this.SendMessage(connectionString, queue, clonedMessage);

        await this.DeleteMessage(connectionString, queue, message, true);
    }

    public async Task<long> PurgeMessages(ServiceBusConnectionString connectionString, string queue, bool isDlq)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            queue,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete,
                SubQueue = isDlq ? SubQueue.DeadLetter : SubQueue.None,
            });

        var operationTimeout = TimeSpan.FromSeconds(5);
        long purgedCount = 0;
        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(
                this._appSettings.QueueMessageFetchCount,
                operationTimeout);
            if (messages == null || messages.Count == 0)
            {
                break;
            }

            purgedCount += messages.Count;
        }

        return purgedCount;
    }

    public async Task<long> TransferDlqMessages(ServiceBusConnectionString connectionString, string queuePath)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            queuePath,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.DeadLetter,
            });
        await using var sender = client.CreateSender(queuePath);

        var operationTimeout = TimeSpan.FromSeconds(5);
        long transferredCount = 0;
        while (true)
        {
            var messages =
                await receiver.ReceiveMessagesAsync(this._appSettings.QueueMessageFetchCount, operationTimeout);
            if (messages == null || messages.Count == 0)
            {
                break;
            }

            await sender.SendMessagesAsync(messages.Select(x => new ServiceBusMessage(x)));

            transferredCount += messages.Count;
        }


        return transferredCount;
    }

    public async Task MoveMessage(
        string messageId,
        ServiceBusConnectionString connectionString,
        string queueName,
        string destinationQueueTopic)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver( 
            queueName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        await using var sender = client.CreateSender(destinationQueueTopic);

        var operationTimeout = TimeSpan.FromSeconds(5);
        var messagesPeekedButNotProcessed = new List<ServiceBusReceivedMessage>();
        while (true)
        {
            var messages = await receiver.ReceiveMessagesAsync(this._appSettings.TopicMessageFetchCount, operationTimeout);
            messagesPeekedButNotProcessed.AddRange(messages.Where(x => x.MessageId.Equals(messageId) == false));
            
            var foundMessage = messages.FirstOrDefault(m => m.MessageId.Equals(messageId));
            if (foundMessage != null)
            {
                await sender.SendMessageAsync(new ServiceBusMessage(foundMessage));
                await receiver.CompleteMessageAsync(foundMessage);
                break;
            }
        }
        
        foreach (var message in messagesPeekedButNotProcessed)
        {
            await receiver.AbandonMessageAsync(message);
        }
    }

    private async Task<ServiceBusReceivedMessage> PeekDlqMessageBySequenceNumber(
        ServiceBusConnectionString connectionString,
        string queue,
        long sequenceNumber)
    {
        await using var client = this.GetBusClient(connectionString);
        await using var receiver = client.CreateReceiver(
            queue,
            new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.DeadLetter,
            });

        return await receiver.PeekMessageAsync(sequenceNumber);
    }

    private async Task<List<ServiceBusQueue>> GetQueues(ServiceBusAdministrationClient client)
    {
        var queueInfos = new List<QueueRuntimeProperties>();
        var pages = client
            .GetQueuesRuntimePropertiesAsync()
            .AsPages(pageSizeHint: MaxRequestItemsPerPage);


        await foreach (var page in pages)
        {
            queueInfos.AddRange(page.Values);
        }

        return queueInfos.Select(x => new ServiceBusQueue(x)).ToList();
    }
}