using System.Text;

namespace Akagi.Communication.Commands;

internal abstract class ListCommand : TextCommand
{
    protected static string GetList(string[] ids, string[] names)
    {
        if (ids.Length != names.Length)
        {
            throw new ArgumentException("Commands and names must have the same length");
        }
        StringBuilder sb = new();
        for (int i = 0; i < ids.Length; i++)
        {
            sb.Append(names[i]);
            sb.Append(" (");
            sb.Append(ids[i]);
            sb.Append(')');
            if (i < ids.Length - 1)
            {
                sb.Append(", ");
            }
        }
        return sb.ToString();
    }

    protected static string GetCommandListChoice(string command, string[] ids, string[] names)
    {
        if (ids.Length != names.Length)
        {
            throw new ArgumentException("Commands and names must have the same length");
        }

        StringBuilder sb = new();
        for (int i = 0; i < ids.Length; i++)
        {
            sb.Append(names[i]);
            sb.Append(":\n/");
            sb.Append(command);
            sb.Append(' ');
            sb.Append(ids[i]);
            if (i < ids.Length - 1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }
}
