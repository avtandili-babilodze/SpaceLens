using SpaceLens.Core.Services.Interaction;

namespace SpaceLens.Tests.Support;

/// <summary>Answers every confirmation the same way and records what was asked.</summary>
public sealed class ScriptedDialogs(bool answer) : IConfirmationService
{
    public List<ConfirmationRequest> Requests { get; } = [];

    public bool Confirm(ConfirmationRequest request)
    {
        Requests.Add(request);
        return answer;
    }

    public void Inform(string title, string message) => Requests.Add(new ConfirmationRequest(title, message, "OK"));
}
