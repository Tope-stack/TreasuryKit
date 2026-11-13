using TreasuryKit.BuildingBlocks.Infrastructure.Auditing;

namespace TreasuryKit.BuildingBlocks.Infrastructure.Inbox;

/// <summary>Records that a handler in this module has processed a message. Primary key: (message_id, handler).</summary>
public sealed class InboxMessage : INotAudited
{
    public InboxMessage(Guid messageId, string handler, DateTimeOffset processedAt)
    {
        MessageId = messageId;
        Handler = handler;
        ProcessedAt = processedAt;
    }

    public Guid MessageId { get; private set; }

    public string Handler { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }
}
