using System;
using System.Text;

namespace PurpleExplorer.Models;

using System.Collections.Generic;
using System.Linq;
using Azure.Messaging.ServiceBus;
using ReactiveUI;

public class Message
{
    public string MessageId { get; set; }
    public string ContentType { get; set; }
    public string Content { get; set; }
    public string Label { get; set; }
    public long Size { get; set; }
    public string CorrelationId { get; set; }
    public int DeliveryCount { get; set; }
    public long SequenceNumber { get; set; }
    public TimeSpan TimeToLive { get; set; }
    public DateTime EnqueueTimeUtc { get; set; }
    public string DeadLetterReason { get; set; }
    public IReadOnlyList<MessageCustomProperty> CustomProperties { get; set; }
    public bool IsDlq { get; }


    public Message(ServiceBusReceivedMessage azureMessage, bool isDlq)
    {
        this.Content = azureMessage.Body is not null ? Encoding.UTF8.GetString(azureMessage.Body) : string.Empty;
        this.MessageId = azureMessage.MessageId;
        this.CorrelationId = azureMessage.CorrelationId;
        this.DeliveryCount = azureMessage.DeliveryCount;
        this.ContentType = azureMessage.ContentType;
        this.Label = azureMessage.Subject;
        this.SequenceNumber = azureMessage.SequenceNumber;
        this.Size = -1;
        this.TimeToLive = azureMessage.TimeToLive;
        this.IsDlq = isDlq;
        this.EnqueueTimeUtc = azureMessage.EnqueuedTime.UtcDateTime;
        this.DeadLetterReason = azureMessage.DeadLetterReason;
        this.CustomProperties = azureMessage.ApplicationProperties.Select(x => new MessageCustomProperty(x.Key, x.Value)).ToList();
    }
}

public record MessageCustomProperty (string Header, object Value);