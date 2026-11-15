using System.Collections.Concurrent;
using Bot.Platform.Messaging;

namespace Modules.MovieNight.Tests.Helpers;

public sealed record SentMessage(long ChatId, string Text, CancellationToken Token, OutgoingMessage Message);

/// <summary>Запись общего упорядоченного журнала: отправка (SendAsync) или редактирование (EditAsync).</summary>
public sealed record SenderEntry(bool IsEdit, long ChatId, int? MessageId, OutgoingMessage Message);

/// <summary>Подмена IMessageSender: запоминает отправки; поведение по получателю настраивается.</summary>
public sealed class RecordingSender : IMessageSender
{
    private readonly ConcurrentQueue<SentMessage> _sent = new();
    private readonly ConcurrentQueue<SenderEntry> _journal = new();

    /// <summary>Что вернуть/бросить для получателя; по умолчанию Sent.</summary>
    public Func<long, SendResult> Behavior { get; set; } = _ => new SendResult(SendStatus.Sent, 1);

    public IReadOnlyList<SentMessage> Sent => _sent.ToArray();

    public IReadOnlyList<string> TextsTo(long chatId) =>
        _sent.Where(m => m.ChatId == chatId).Select(m => m.Text).ToArray();

    public IReadOnlyList<long> Recipients => _sent.Select(m => m.ChatId).ToArray();

    /// <summary>Все отправки и редактирования в порядке вызова.</summary>
    public IReadOnlyList<SenderEntry> Journal => _journal.ToArray();

    public IReadOnlyList<SenderEntry> Edits => _journal.Where(e => e.IsEdit).ToArray();

    public void Clear()
    {
        _sent.Clear();
        _journal.Clear();
    }

    public Task<SendResult> SendAsync(long chatId, OutgoingMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(new SentMessage(chatId, message.Text, cancellationToken, message));
        _journal.Enqueue(new SenderEntry(false, chatId, null, message));
        return Task.FromResult(Behavior(chatId));
    }

    public Task<SendResult> EditAsync(long chatId, int messageId, OutgoingMessage message, CancellationToken cancellationToken)
    {
        _journal.Enqueue(new SenderEntry(true, chatId, messageId, message));
        return Task.FromResult(new SendResult(SendStatus.Sent, messageId));
    }
}
