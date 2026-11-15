using System.Collections.Concurrent;
using Bot.Platform.Messaging;

namespace Modules.MovieNight.Tests.Helpers;

public sealed record SentMessage(long ChatId, string Text, CancellationToken Token, OutgoingMessage Message);

/// <summary>Подмена IMessageSender: запоминает отправки; поведение по получателю настраивается.</summary>
public sealed class RecordingSender : IMessageSender
{
    private readonly ConcurrentQueue<SentMessage> _sent = new();

    /// <summary>Что вернуть/бросить для получателя; по умолчанию Sent.</summary>
    public Func<long, SendResult> Behavior { get; set; } = _ => new SendResult(SendStatus.Sent, 1);

    public IReadOnlyList<SentMessage> Sent => _sent.ToArray();

    public IReadOnlyList<string> TextsTo(long chatId) =>
        _sent.Where(m => m.ChatId == chatId).Select(m => m.Text).ToArray();

    public IReadOnlyList<long> Recipients => _sent.Select(m => m.ChatId).ToArray();

    public void Clear() => _sent.Clear();

    public Task<SendResult> SendAsync(long chatId, OutgoingMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(new SentMessage(chatId, message.Text, cancellationToken, message));
        return Task.FromResult(Behavior(chatId));
    }

    public Task<SendResult> EditAsync(long chatId, int messageId, OutgoingMessage message, CancellationToken cancellationToken) =>
        Task.FromResult(new SendResult(SendStatus.Sent, messageId));
}
