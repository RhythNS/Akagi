using Akagi.Users;

namespace Akagi.LLMs;

internal interface ILLMFactory
{
    public ILLM Create(User user);
}
