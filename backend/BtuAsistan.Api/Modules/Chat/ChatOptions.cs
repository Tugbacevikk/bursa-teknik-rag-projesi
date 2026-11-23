using System.ComponentModel.DataAnnotations;

namespace BtuAsistan.Api.Modules.Chat;

public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    // Shown when the documents do not answer the question (PLANNING C7).
    [Required]
    public string NotFoundMessage { get; init; } =
        "Belgelerde bu sorunun cevabını bulamadım. İMEP Koordinatörlüğü veya Öğrenci İşleri'ne başvurabilirsiniz.";

    // Shown when the answer cannot be written (C12).
    [Required]
    public string UnavailableMessage { get; init; } =
        "Şu an cevap veremiyorum, lütfen biraz sonra tekrar deneyin.";

    // Earlier messages of the chat given to the model, so follow-up questions make sense
    // (C4). Only the last question is searched.
    [Range(0, 20)]
    public int HistoryMessageCount { get; init; } = 6;

    // From the question to the end of the answer. The AI service gives Gemini 60 s, and the
    // Gemini client retries on its own when Google is overloaded; past this the student
    // gets UnavailableMessage.
    [Range(5, 600)]
    public int GenerationTimeoutSeconds { get; init; } = 90;

    // Answers written at the same time, for everyone together (C11): keeps a rush of
    // questions from using up the Gemini quota at once. Further questions wait in line;
    // when the line is full they get 503 and BusyMessage.
    [Range(1, 100)]
    public int MaxConcurrentAnswers { get; init; } = 4;

    [Range(0, 1000)]
    public int MaxQueuedAnswers { get; init; } = 20;

    [Required]
    public string BusyMessage { get; init; } =
        "Şu an çok sayıda soru geliyor, lütfen biraz sonra tekrar deneyin.";

    // Chats idle for longer are deleted by RetentionService (C9, D7).
    [Range(1, 3650)]
    public int RetentionDays { get; init; } = 90;

    // How often RetentionService looks for them (also once at startup).
    [Range(1, 168)]
    public int RetentionIntervalHours { get; init; } = 24;

    // Chats deleted per statement.
    [Range(1, 100_000)]
    public int RetentionBatchSize { get; init; } = 1000;
}
