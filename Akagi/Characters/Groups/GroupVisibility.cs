namespace Akagi.Characters.Groups;

[Flags]
internal enum GroupVisibility
{
    None = 0,
    FinalOnly = 1,
    Intermediate = 2,
    CompletionNotice = 4,
}
