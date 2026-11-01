using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Learning;
using Akagi.Users;
using Microsoft.Extensions.Logging;
using CharacterModel = Akagi.Characters.Character;

namespace Akagi.Communication.SocketComs.Transmissions;

internal class LearningRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(LearningRequestTransmission);

    private readonly ILearningService _learning;
    private readonly ILogger<LearningRequestHandler> _logger;

    public LearningRequestHandler(ILearningService learning, ILogger<LearningRequestHandler> logger)
    {
        _learning = learning;
        _logger = logger;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in LearningRequestHandler");
        LearningRequestTransmission request = GetTransmission<LearningRequestTransmission>(transmissionWrapper);
        SocketSession session = context.Session;

        // Preparing and grading take many seconds, and the session reads nothing else until this method returns
        _ = Task.Run(async () => session.SendTransmission(await ProcessAsync(user.Id!, request)));
        return Task.CompletedTask;
    }

    internal async Task<LearningResponseTransmission> ProcessAsync(string userId, LearningRequestTransmission request)
    {
        LearningResponseTransmission response = new() { RequestId = request.RequestId };
        try
        {
            switch (request.Action)
            {
                case LearningAction.Overview:
                    response.Overview = ToBridge(await _learning.GetStateAsync(userId));
                    break;

                case LearningAction.Start:
                    response.Lesson = ToBridge(await _learning.StartAsync(userId,
                        new LessonRequest(request.Mode, request.Topic, request.FocusIds, request.QuestionCount)));
                    break;

                case LearningAction.Answer:
                    response.Lesson = ToBridge(await _learning.AnswerAsync(userId, request.LessonId, request.QuestionIndex,
                        request.ChosenOption, request.AnswerText));
                    break;

                case LearningAction.Finish:
                    response.Lesson = ToBridge(await _learning.FinishAsync(userId, request.LessonId));
                    break;

                default:
                    response.Error = "Unknown learning request.";
                    break;
            }
        }
        catch (LearningException ex)
        {
            response.Error = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Learning request {Action} failed for user {UserId}", request.Action, userId);
            response.Error = "Something went wrong. Try again in a moment.";
        }
        return response;
    }

    internal static LearningOverview ToBridge(LearningState state) => new()
    {
        Tutor = ToBridge(state.Tutor),
        Struggles = [.. state.Struggles.Select(s => new LearningStruggle { Id = s.Id, Kind = s.Kind, Title = s.Title, Detail = s.Detail })],
        Current = state.Current == null ? null : ToBridge(state.Current),
        Recent = [.. state.Recent.Select(ToBridge)]
    };

    internal static LearningLesson ToBridge(Lesson lesson) => new()
    {
        Id = lesson.Id!,
        Mode = lesson.Mode,
        Status = lesson.Status,
        Topic = lesson.Topic,
        Intro = lesson.Intro,
        Closing = lesson.Closing,
        Questions = [.. lesson.Questions.Select(ToBridge)],
        CreatedAt = lesson.CreatedAt,
        Score = lesson.Score
    };

    private static LearningQuestion ToBridge(LessonQuestion question)
    {
        LearningQuestion bridge = new()
        {
            Concept = question.Concept,
            Prompt = question.Prompt,
            Options = question.Options,
            Verdict = question.Verdict,
            ChosenOption = question.ChosenOption,
            AnswerText = question.AnswerText
        };
        if (question.IsAnswered)
        {
            bridge.CorrectOption = question.Options.Length > 0 ? question.CorrectOption : null;
            bridge.ExpectedAnswer = question.Answer;
            bridge.Feedback = question.Feedback;
        }
        return bridge;
    }

    private static Character ToBridge(CharacterModel tutor) => new()
    {
        Id = tutor.Id!,
        Name = tutor.Name,
        CardId = tutor.CardId,
        HasImage = !string.IsNullOrEmpty(tutor.Card.ImageId),
        IsTutor = true
    };
}
